using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Balance;
using PixelMmo.Data;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

public enum PlayerState
{
    Normal,
    Dash,
    Dead,
}

/// <summary>
/// IPlayerContext 구현체. 전투/세이브/디버그 어느 쪽도 이 클래스를 직접 참조하지 않고
/// 인터페이스만 본다. (CLAUDE.md 규칙 2)
///
/// 시각 표현은 파란 사각형 ColorRect 하나. (§A 아트 방침)
/// </summary>
public partial class PlayerCharacter : CharacterBody2D, IPlayerContext, IDamageable, IAnimationDriver
{
    /// <summary>몬스터가 플레이어를 찾는 그룹 이름.</summary>
    public const string Group = "player";

    /// <summary>겉모습(색 슬롯). 피규어를 이 데이터로 그린다.</summary>
    private CharacterAppearance _appearance = CharacterAppearance.Default();

    /// <summary>무적 점멸에 따른 이번 프레임 투명도. 그리기에서 파츠 색에 곱한다.</summary>
    private float _blinkAlpha = 1f;

    /// <summary>애니메이션 위상 누적. 대기·걸음 들썩임을 만든다.</summary>
    private float _animTime;

    /// <summary>8방향 스프라이트. 에셋이 없으면 IsLoaded=false 라 도형 피규어로 폴백한다.</summary>
    private PlayerSprite _sprite;

    private readonly RandomNumberGenerator _rng = new();

    private readonly HashSet<string> _flags = new();
    private readonly Dictionary<string, int> _counters = new();
    private readonly Dictionary<string, int> _baseStats = new();
    private readonly SkillRunner _attack = new();

    private PlayerState _state = PlayerState.Normal;

    private Vector2 _facing = Vector2.Right;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private float _dashInvulnTimer;
    private Vector2 _dashDirection = Vector2.Right;
    private float _respawnTimer;

    private float _iframeTimer;
    private float _flashTimer;
    private float _knockbackTimer;
    private float _knockbackTotal;
    private Vector2 _knockbackVelocity;

    /// <summary>스킬 id → 남은 쿨다운(초).</summary>
    private readonly Dictionary<string, float> _cooldowns = new();

    /// <summary>후딜 중에 누른 입력을 기억한다. (§C-7 입력 버퍼)</summary>
    private string _bufferedAction;
    private float _bufferTimer;

    private bool _guarding;
    private float _guardElapsed;
    private float _riposteTimer;

    /// <summary>가드를 뗀 뒤 다시 올리기까지의 쿨다운. 퍼펙트 가드는 이를 0 으로 초기화한다.</summary>
    private float _guardCooldown;

    /// <summary>이번 가드 유지 동안 퍼펙트가 있었는가. 뗄 때 쿨다운을 걸지 결정한다.</summary>
    private bool _perfectThisGuard;

    private float _buffTimer;
    private float _buffAttackMultiplier = 1f;

    /// <summary>sk_bash 의 전방 돌진. 선딜 동안만 흐른다.</summary>
    private float _lungeTimer;
    private Vector2 _lungeVelocity;

    /// <summary>이번 스윙의 시작 각도와 지난 프레임 칼날 각도. 판정 띠를 만드는 데 쓴다.</summary>
    private float _swingStartAngle;
    private float _prevBladeAngle;

    /// <summary>시선 고정 대상. 잡혀 있으면 이동과 무관하게 계속 바라본다.</summary>
    private MonsterBase _lockTarget;

    /// <summary>좌클릭 엣지 감지 — 누르고 있는 동안 매 프레임 재지정되지 않게.</summary>
    private bool _lockClickHeld;

    /// <summary>클릭으로 대상을 집을 때의 반지름 여유(px).</summary>
    private static readonly float LockClickSlackPx = 10f;

    /// <summary>§C-8 에서 스킬에 묶인 키들. 버퍼가 감시하는 대상.</summary>
    private static readonly string[] SkillActions =
    {
        InputSetup.Attack,
        InputSetup.Bash,
        InputSetup.Warcry,
    };

    /// <summary>이 값이 0 보다 크면 이 엔티티만 시간이 멈춘다. (§C-3)</summary>
    private float _hitstopTimer;

    public JobState Jobs { get; } = new();
    public MasteryTracker Mastery { get; } = new();

    public int Level { get; private set; } = PlayerTuning.StartLevel;
    public float Hp { get; private set; } = PlayerTuning.BaseMaxHp;
    public float MaxHp { get; private set; } = PlayerTuning.BaseMaxHp;
    public float HpRatio => MaxHp > 0f ? Hp / MaxHp : 0f;
    public bool IsAlive => _state != PlayerState.Dead;
    public Vector2 WorldPosition => GlobalPosition;
    public ulong InstanceId => GetInstanceId();

    public PlayerState State => _state;
    public Vector2 Facing => _facing;
    public bool IsDashing => _state == PlayerState.Dash;
    public bool IsInvulnerable => _dashInvulnTimer > 0f || _iframeTimer > 0f;
    public float DashCooldownRemaining => Mathf.Max(0f, _dashCooldownTimer);
    public SkillRunner Attack => _attack;

    /// <summary>화면 중앙 한 줄 메시지. World 가 받아서 띄운다.</summary>
    public event Action<string> Announced;

    /// <summary>대시 무적으로 공격을 흘렸을 때. sk_hidden_deathline 트리거가 여기에 붙는다. (§F)</summary>
    public event Action DodgeSucceeded;

    /// <summary>스킬 1회 사용의 숙련 판정 결과. 디버그 오버레이가 받아 적는다. (§I)</summary>
    public event Action<MasteryResult> SkillUsed;

    public override void _Ready()
    {
        Name = "Player";
        AddToGroup(Group);
        CollisionLayer = CollisionLayers.Player;
        CollisionMask = CollisionLayers.World;
        MotionMode = MotionModeEnum.Floating;
        ZIndex = 10;

        BuildShapes();
        DodgeSucceeded += OnDodgeSucceeded;
        ResetToNewGame();
    }

    private void BuildShapes()
    {
        // 충돌은 전투 판정용 크기 그대로. 시각은 스프라이트(있으면) 또는 _Draw 도형.
        float size = PlayerTuning.BodySize;
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(size, size) },
        };
        AddChild(shape);

        // art/player 에셋이 있으면 스프라이트로 그린다. 없으면 도형 피규어로 폴백.
        _sprite = new PlayerSprite();
        AddChild(_sprite);
    }

    /// <summary>새 게임 상태. 세이브가 없거나 로드가 실패했을 때의 폴백. (§H)</summary>
    public void ResetToNewGame()
    {
        _flags.Clear();
        _counters.Clear();
        _baseStats.Clear();
        foreach (var pair in PlayerTuning.BaseStats)
            _baseStats[pair.Key] = pair.Value;

        Level = PlayerTuning.StartLevel;
        MaxHp = PlayerTuning.BaseMaxHp;
        Hp = MaxHp;
        _state = PlayerState.Normal;
        Velocity = Vector2.Zero;
        GlobalPosition = WorldLayout.SpawnPoint;

        Mastery.Restore(null);
        ResetCombatState();
        _appearance = CharacterAppearance.Default();

        // P1 은 job_warrior R1 고정. 랭크 1 스킬을 GameDb 에서 실제로 지급받는다. (§D-1)
        Jobs.RestoreFrom("", null, null, null, null);
        Jobs.StartJob(PlayerTuning.StartingJobId, GameDatabase.Instance);
        SetFlag($"held_{PlayerTuning.StartingJobId}");
    }

    /// <summary>전투 중 임시 상태를 전부 지운다. 부활·로드·새 게임에서 공유한다.</summary>
    private void ResetCombatState()
    {
        _attack.Cancel();
        _cooldowns.Clear();
        _bufferedAction = null;
        _bufferTimer = 0f;
        _guarding = false;
        _guardElapsed = 0f;
        _guardCooldown = 0f;
        _perfectThisGuard = false;
        _riposteTimer = 0f;
        _buffTimer = 0f;
        _buffAttackMultiplier = 1f;
        _lungeTimer = 0f;
        _hitstopTimer = 0f;
        _knockbackTimer = 0f;
        _lockTarget = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 히트스톱: 이 엔티티만 멈춘다. 화면 전체가 아니다. (§C-3)
        if (_hitstopTimer > 0f)
        {
            _hitstopTimer -= dt;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            UpdateVisuals(dt);
            return;
        }

        TickTimers(dt);

        if (_state == PlayerState.Dead)
        {
            Velocity = Vector2.Zero;
        }
        else if (_knockbackTimer > 0f)
        {
            UpdateKnockback(dt);
        }
        else if (_state == PlayerState.Dash)
        {
            UpdateDash(dt);
        }
        else
        {
            UpdateNormal(dt);
        }

        MoveAndSlide();
        UpdateVisuals(dt);

        // 피규어가 늘 보이고(점멸·방향·모션이 매 프레임 바뀜) 비용도 미미하므로 항상 다시 그린다.
        QueueRedraw();
    }

    private void TickTimers(float dt)
    {
        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= dt;
        if (_dashInvulnTimer > 0f)
            _dashInvulnTimer -= dt;
        if (_iframeTimer > 0f)
            _iframeTimer -= dt;
        if (_flashTimer > 0f)
            _flashTimer -= dt;
        if (_riposteTimer > 0f)
            _riposteTimer -= dt;

        if (_buffTimer > 0f)
        {
            _buffTimer -= dt;
            if (_buffTimer <= 0f)
                _buffAttackMultiplier = 1f;
        }

        TickCooldowns(dt);

        if (_state == PlayerState.Dead)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f)
                Respawn();
        }
    }

    private void TickCooldowns(float dt)
    {
        if (_cooldowns.Count == 0)
            return;

        // 순회 중 수정을 피하려고 키를 먼저 뽑는다.
        var keys = new string[_cooldowns.Count];
        _cooldowns.Keys.CopyTo(keys, 0);
        foreach (string key in keys)
        {
            float value = _cooldowns[key] - dt;
            if (value <= 0f)
                _cooldowns.Remove(key);
            else
                _cooldowns[key] = value;
        }
    }

    private void UpdateNormal(float dt)
    {
        Vector2 input = ReadMoveInput();

        UpdateLockOn();

        // 시선 고정 중에는 이동 입력이 방향을 바꾸지 않는다 — 그래서 뒷걸음질이 된다.
        // 선딜 중에만 방향 전환 가능. 판정이 시작되면 고정된다. (§C-7)
        if (_lockTarget == null && input != Vector2.Zero && (!_attack.IsBusy || _attack.CanTurn))
        {
            _facing = input;
            _attack.Aim(input);
        }

        AdvanceAttack(dt);

        // 겉모습 무작위 — 정식 커스터마이즈 UI 전까지의 임시 진입점. (C)
        if (Input.IsActionJustPressed(InputSetup.Customize))
            RandomizeAppearance();

        if (!_attack.IsBusy && Input.IsActionJustPressed(InputSetup.Dash) && _dashCooldownTimer <= 0f)
        {
            StartDash(input);
            return;
        }

        ReadSkillInput(dt);

        // sk_bash 의 전방 돌진은 이동 입력을 덮어쓴다.
        if (_lungeTimer > 0f)
        {
            _lungeTimer -= dt;
            Velocity = _lungeVelocity;
            return;
        }

        // 후딜에는 이동 불가. (§C-2) 가드 중에는 40%. (§E) Shift 홀드면 달리기.
        Vector2 target = _attack.CanMove
            ? input * CurrentMoveSpeed() * _attack.MoveScale * GuardMoveScale()
            : Vector2.Zero;

        ApplyAcceleration(target, dt, _attack.CanMove && input != Vector2.Zero);
    }

    /// <summary>§C-8 의 스킬 키 4개. 버퍼 → 캔슬 창 → 발동 순서로 처리한다.</summary>
    private void ReadSkillInput(float dt)
    {
        foreach (string action in SkillActions)
        {
            if (Input.IsActionJustPressed(action))
            {
                _bufferedAction = action;
                _bufferTimer = CombatTuning.InputBuffer;
            }
        }

        UpdateGuard(dt);

        if (_bufferTimer <= 0f)
        {
            _bufferedAction = null;
            return;
        }

        _bufferTimer -= dt;

        // 놀고 있거나, 후딜의 캔슬 창 안이면 다음 스킬로 이어진다. (§C-2 콤보)
        if (_attack.IsBusy && !_attack.InCancelWindow)
            return;

        var skill = ResolveSkillForAction(_bufferedAction);
        if (TryStartSkill(skill))
        {
            _bufferedAction = null;
            _bufferTimer = 0f;
        }
    }

    /// <summary>공격 프레임을 진행하고, 판정 프레임이면 히트박스를 돌린다.</summary>
    private void AdvanceAttack(float dt)
    {
        if (!_attack.IsBusy)
            return;

        _attack.Tick(dt);

        // 돌진은 판정과 함께 시작한다. 그래야 돌진 경로 전체가 판정 범위가 된다.
        // (선딜에 돌진하면 판정이 도착점에서만 열려 맞히기가 지나치게 어렵다.)
        if (_attack.JustEnteredActive)
        {
            StartLunge(_attack.Skill);

            // 스윙 시작 각도를 잡아 둔다. 판정은 여기서부터 훑어 나간다.
            _swingStartAngle = PoseFor(_attack.Skill, SkillPhase.Active, 0f).AngleOffset;
            _prevBladeAngle = _swingStartAngle;

            SpawnSkillVfx(_attack.Skill);
        }

        if (_attack.IsActive)
        {
            ProcessActiveHitbox();
            _prevBladeAngle = PoseFor(_attack.Skill, _attack.Phase, _attack.PhaseProgress).AngleOffset;
        }
    }

    /// <summary>스킬이 판정에 들어가는 순간의 연출 — 베기 궤적 / 함성 충격파.</summary>
    private void SpawnSkillVfx(SkillDefinition skill)
    {
        var fb = CombatFeedback.Instance;
        if (fb == null)
            return;

        // 함성 — 자신 중심 큰 충격파.
        if (skill.Stun != null)
        {
            fb.ShockAt(GlobalPosition, CombatTuning.VfxWarcryShock, new Color(1f, 0.9f, 0.5f));
            return;
        }

        // 강타(돌진) — 돌진 방향으로 꼬깔(원뿔)을 뻗는다. 충격파는 명중 순간(Strike).
        if (skill.Dash != null)
        {
            fb.ConeAt(GlobalPosition, _attack.LockedFacing, new Color(1f, 0.75f, 0.4f));
            return;
        }

        // 근접 타격은 이펙트를 쓰지 않는다 — 주먹이 닿는 것 자체가 판정이고,
        // 손맛은 히트스톱·넉백·화면 흔들림·스파크로 낸다. (이펙트가 동작을 가린다)
    }

    private void StartLunge(SkillDefinition skill)
    {
        float distance = skill?.Dash?.DistancePx ?? 0f;
        if (distance <= 0f || CombatTuning.AttackActive <= 0f)
            return;

        _lungeTimer = CombatTuning.AttackActive;
        _lungeVelocity = _attack.LockedFacing * (distance / CombatTuning.AttackActive);
    }

    protected bool TryStartSkill(SkillDefinition skill)
    {
        if (skill == null || !skill.IsActive || skill.Kind == "hold")
            return false;

        // 캔슬 창 밖에서는 새 스킬을 시작할 수 없다.
        if (_attack.IsBusy && !_attack.InCancelWindow)
            return false;

        if (CooldownRemaining(skill.Id) > 0f)
            return false;

        // 가드를 공격으로 끊는 것도 '뗀 것'으로 본다 — 가드↔공격 왕복으로
        // 쿨다운을 회피하지 못하게 한다.
        ReleaseGuard(GuardSkill()?.Guard);
        _attack.Begin(skill, _facing);

        if (skill.Cooldown > 0f)
            _cooldowns[skill.Id] = skill.Cooldown;

        // 돌진 자체는 판정 프레임에 들어갈 때 시작한다. StartLunge 참고.

        // 자기 강화 버프는 사용 즉시 걸린다. (§E sk_warcry)
        if (skill.Buff != null && skill.Buff.DurationSeconds > 0f)
        {
            _buffTimer = skill.Buff.DurationSeconds;
            _buffAttackMultiplier = skill.Buff.AttackPowerMultiplier;
            DebugLog.Add($"{skill.Id} 공격력 x{_buffAttackMultiplier:0.##} {_buffTimer:0.#}s");
        }

        return true;
    }

    public float CooldownRemaining(string skillId)
        => _cooldowns.TryGetValue(skillId, out float value) ? Mathf.Max(0f, value) : 0f;

    // --- 가드 (§E sk_guard) ------------------------------------------------

    public bool IsGuarding => _guarding;
    public bool IsRiposting => _riposteTimer > 0f;

    private SkillDefinition GuardSkill() => ResolveSkillForAction(InputSetup.Guard);

    public float GuardCooldownRemaining => Mathf.Max(0f, _guardCooldown);

    // --- 시선 고정 ---------------------------------------------------------

    public bool IsLockedOn => _lockTarget != null;

    /// <summary>
    /// 시선 고정 — 홀드가 아니라 토글이다. (Shift 는 달리기로 넘어감)
    ///   Tab       : 가장 가까운 적으로 고정 / 다시 누르면 해제
    ///   좌클릭    : 클릭한 적으로 고정 (빈 곳을 클릭하면 해제)
    /// 고정 중에는 이동 입력이 방향을 덮지 않아 옆걸음·뒷걸음으로 거리를 잰다.
    /// 대상이 죽거나 멀어지면 가장 가까운 적으로 자동 재획득하고, 없으면 해제된다.
    /// </summary>
    private void UpdateLockOn()
    {
        float range = CombatTuning.LockOnRange;

        // Tab 토글.
        if (Input.IsActionJustPressed(InputSetup.LockOn))
        {
            _lockTarget = _lockTarget != null ? null : FindLockTarget(range);
            AnnounceLock();
        }

        // 좌클릭으로 대상 지정 — 커서 아래 몬스터를 집는다.
        if (Input.IsMouseButtonPressed(MouseButton.Left) && !_lockClickHeld)
        {
            _lockClickHeld = true;
            _lockTarget = MonsterUnderCursor();
            AnnounceLock();
        }
        else if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _lockClickHeld = false;
        }

        if (_lockTarget == null)
            return;

        // 죽었거나 사거리를 벗어나면 가장 가까운 적으로 갈아탄다. 없으면 해제.
        if (!IsValidLockTarget(_lockTarget, range))
        {
            _lockTarget = FindLockTarget(range);
            if (_lockTarget == null)
                return;
        }

        Vector2 toTarget = _lockTarget.GlobalPosition - GlobalPosition;
        if (toTarget.LengthSquared() <= 0.0001f)
            return;

        // 판정이 시작된 뒤에는 스킬 방향을 돌리지 않는다. (§C-7)
        _facing = toTarget.Normalized();
        _attack.Aim(_facing);
    }

    private void AnnounceLock()
        => DebugLog.Add(_lockTarget != null ? $"시선 고정: {_lockTarget.Stats.Id}" : "시선 고정 해제");

    /// <summary>마우스 커서 아래의 몬스터. 반지름에 여유를 둬 작은 적도 집기 쉽게.</summary>
    private MonsterBase MonsterUnderCursor()
    {
        Vector2 world = GetGlobalMousePosition();
        MonsterBase best = null;
        float bestDistance = float.MaxValue;

        foreach (Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
        {
            if (node is not MonsterBase monster || !IsInstanceValid(monster) || !monster.IsAlive)
                continue;

            float distance = world.DistanceTo(monster.GlobalPosition);
            if (distance <= monster.Stats.Radius + LockClickSlackPx && distance < bestDistance)
            {
                bestDistance = distance;
                best = monster;
            }
        }
        return best;
    }

    private bool IsValidLockTarget(MonsterBase target, float range)
        => target != null && IsInstanceValid(target) && target.IsAlive
            && GlobalPosition.DistanceTo(target.GlobalPosition) <= range;

    private MonsterBase FindLockTarget(float range)
    {
        MonsterBase best = null;
        float bestDistance = float.MaxValue;

        foreach (Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
        {
            if (node is not MonsterBase monster || !IsInstanceValid(monster) || !monster.IsAlive)
                continue;

            float distance = GlobalPosition.DistanceTo(monster.GlobalPosition);
            if (distance <= range && distance < bestDistance)
            {
                bestDistance = distance;
                best = monster;
            }
        }
        return best;
    }

    private void UpdateGuard(float dt)
    {
        if (_guardCooldown > 0f)
            _guardCooldown -= dt;

        var skill = GuardSkill();
        if (skill?.Guard == null || _attack.IsBusy)
        {
            ReleaseGuard(skill?.Guard);
            return;
        }

        bool held = Input.IsActionPressed(InputSetup.Guard);

        if (!held)
        {
            ReleaseGuard(skill.Guard);
            return;
        }

        // 쿨다운 중에는 새로 올릴 수 없다. 뗐다가 바로 다시 누르는 남발을 막는다.
        // (누르고 있는 동안은 계속 유지되지만, 한 번 떼면 쿨다운을 기다려야 한다.)
        if (!_guarding && _guardCooldown > 0f)
            return;

        if (!_guarding)
        {
            _guardElapsed = 0f;
            _perfectThisGuard = false;
        }
        else
        {
            _guardElapsed += dt;
        }

        _guarding = true;
    }

    /// <summary>가드를 내린다. 퍼펙트로 막지 못했으면 쿨다운을 건다. (§E)</summary>
    private void ReleaseGuard(SkillGuard guard)
    {
        if (_guarding && !_perfectThisGuard && guard != null)
            _guardCooldown = guard.CooldownSeconds;

        _guarding = false;
    }

    /// <summary>
    /// 지금 프레임의 기본 이동 속도. Shift 를 누르고 있으면 달리기(140), 아니면 걷기(90).
    /// 가드 중에는 달릴 수 없다 — 방패를 든 채 뛰면 가드의 대가가 사라진다.
    /// </summary>
    public bool IsRunning => Input.IsActionPressed(InputSetup.Run) && !_guarding && !_attack.IsBusy;

    private float CurrentMoveSpeed()
        => IsRunning ? CombatTuning.RunSpeed : CombatTuning.MoveSpeed;

    private float GuardMoveScale()
    {
        var guard = GuardSkill()?.Guard;
        return _guarding && guard != null ? guard.MoveSpeedMultiplier : 1f;
    }

    /// <summary>
    /// 보유 스킬 중 이 입력 액션에 묶인 것을 찾는다.
    /// 진화로 스킬이 교체돼도(sk_slash → sk_slash_heavy) 같은 키가 계속 동작한다. (§E)
    /// </summary>
    protected SkillDefinition ResolveSkillForAction(string action)
    {
        var db = GameDatabase.Instance;
        if (db == null)
            return null;

        foreach (string skillId in Jobs.LearnedSkills)
        {
            var skill = db.GetSkill(skillId);
            if (skill != null && skill.IsActive && skill.InputAction == action)
                return skill;
        }
        return null;
    }

    private void ProcessActiveHitbox()
    {
        var skill = _attack.Skill;
        if (skill?.Shape == null)
            return;

        // 경직만 주는 스킬(sk_warcry)은 데미지 경로를 타지 않는다.
        bool support = skill.Stun != null;
        MonsterBase firstAffected = null;

        // 무기 스킬은 이번 프레임에 칼날이 훑고 간 띠가 판정 범위다.
        // 지원 스킬(고리)은 형태 정의(circle)를 그대로 쓴다.
        var pose = PoseFor(skill, _attack.Phase, _attack.PhaseProgress);
        float baseAngle = _attack.LockedFacing.Angle();

        foreach (Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
        {
            if (node is not MonsterBase monster || !IsInstanceValid(monster))
                continue;

            bool hit = support
                ? Hitbox.Overlaps(skill.Shape, GlobalPosition, _attack.LockedFacing,
                    monster.GlobalPosition, monster.Stats.Radius)
                : Hitbox.SweptArc(GlobalPosition, baseAngle, _prevBladeAngle, pose.AngleOffset,
                    pose.Inner, pose.Outer, pose.Width, monster.GlobalPosition, monster.Stats.Radius);

            if (!hit)
                continue;

            // 한 스윙에 같은 대상은 한 번만.
            if (!_attack.TryMarkHit(monster.InstanceId))
                continue;

            if (support)
            {
                if (!monster.IsAlive)
                    continue;
                monster.Stun(skill.Stun.DurationSeconds);
                firstAffected ??= monster;
            }
            else
            {
                Strike(monster, skill);
            }
        }

        // 지원 스킬은 '한 명이라도 걸렸는가' 가 유효한 사용의 기준이다. (§F)
        if (support && firstAffected != null)
            RegisterMastery(skill, firstAffected, targetWasAlive: true);
    }

    private void Strike(MonsterBase monster, SkillDefinition skill)
    {
        bool targetWasAlive = monster.IsAlive;
        float damage = DamageMath.Compute(this, skill, AttackPowerMultiplier());

        // 퍼펙트 가드 직후의 반격은 강타격으로 들어간다.
        bool heavy = skill.Heavy || _riposteTimer > 0f;

        Vector2 direction = monster.GlobalPosition - GlobalPosition;
        if (direction == Vector2.Zero)
            direction = _attack.LockedFacing;

        // 숙련 판정을 먼저 돌린다. 유효/무효와 무관하게 항상 기록되어야 하고,
        // 처치 로그보다 먼저 찍혀야 로그 순서가 실제 인과와 맞는다. (§F, §I)
        RegisterMastery(skill, monster, targetWasAlive);

        bool landed = monster.TakeDamage(new DamageInfo
        {
            Amount = damage,
            Direction = direction.Normalized(),
            Heavy = heavy,
            SkillId = skill.Id,
            Source = this,
        });

        if (!landed)
            return;

        // 돌진 중에 맞으면 그 자리에서 멈춘다. 부딪쳤는데 그대로 지나가면
        // 때린 느낌이 아니라 스쳐 지나간 느낌이 된다.
        if (skill.Dash != null && _lungeTimer > 0f)
        {
            _lungeTimer = 0f;
            Velocity = Vector2.Zero;
        }

        bool killed = !monster.IsAlive;
        float hitstop = killed
            ? CombatTuning.HitstopKill
            : heavy ? CombatTuning.HitstopHeavy : CombatTuning.HitstopNormal;

        // 공격자와 피격자 양쪽이 같이 멈춘다. 이게 타격감의 80%. (§C-3)
        ApplyHitstop(hitstop);
        CombatFeedback.Instance?.OnHit(monster.GlobalPosition, damage, heavy, killed);

        // 강타 명중 — 충격파 링.
        if (skill.Dash != null)
            CombatFeedback.Instance?.ShockAt(monster.GlobalPosition,
                CombatTuning.VfxBashShock, new Color(1f, 0.6f, 0.3f));

        // 처치 보너스 — 보스 같은 대상은 마지막 일격 스킬 숙련이 대폭 오른다. (§F)
        if (killed && monster.Stats.MasteryKillBonus > 0f)
            GrantKillBonus(skill, monster.Stats.MasteryKillBonus);
    }

    private void GrantKillBonus(SkillDefinition skill, float amount)
    {
        var result = Mastery.AddBonus(skill.Id, amount, skill);
        SkillUsed?.Invoke(result);
        DebugLog.Add($"처치 보너스 +{amount:0} → {skill.Id} 숙련 {result.Total:0.0}");

        // 보너스로 진화 임계값을 넘겼으면 진화 연출이 안내문을 대신 띄운다.
        if (!string.IsNullOrEmpty(result.EvolvedInto))
            OnMasteryResult(result, skill);
        else
            CombatFeedback.Instance?.Announce($"{skill.Name} 숙련 대폭 상승!");
    }

    private void RegisterMastery(SkillDefinition skill, MonsterBase target, bool targetWasAlive)
    {
        var result = Mastery.RegisterUse(new SkillUseContext
        {
            SkillId = skill.Id,
            Hit = true,
            TargetAlive = targetWasAlive,
            TargetLevel = target.Level,
            TargetInstanceId = target.InstanceId,
            SelfLevel = Level,
            SelfHpRatio = HpRatio,
            NowSeconds = Time.GetTicksMsec() / 1000.0,
            TrainingTarget = target.IsTrainingDummy,
        }, skill);

        SkillUsed?.Invoke(result);
        OnMasteryResult(result, skill);
    }

    /// <summary>
    /// 숙련이 진화 임계값을 넘었을 때. 플래그 조건을 확인하고 스킬을 교체한다. (§F)
    /// 화면에는 한 줄만 뜬다 — 조건은 설명하지 않는다. (CLAUDE.md 규칙 4)
    /// </summary>
    private void OnMasteryResult(MasteryResult result, SkillDefinition skill)
    {
        if (string.IsNullOrEmpty(result.EvolvedInto))
            return;

        var evolution = skill.Mastery?.Evolution;
        var evolved = GameDatabase.Instance?.GetSkill(result.EvolvedInto);
        if (evolution == null || evolved == null)
            return;

        foreach (string flag in evolution.RequiredFlags)
        {
            if (!HasFlag(flag))
            {
                DebugLog.Add($"{skill.Id} 진화 보류: {flag} 없음");
                return;
            }
        }

        ReplaceSkill(skill.Id, evolved.Id);
        Mastery.Transfer(skill.Id, evolved.Id);
        _attack.Cancel();

        DebugLog.Add($"{skill.Id} → {evolved.Id} 진화");
        Announce(evolution.Announce);
    }

    /// <summary>
    /// 대시 무적으로 공격을 흘렸을 때. data/skills 의 situational_repeat 트리거를
    /// 전부 훑어 조건을 만족하면 카운터를 올리고, 도달하면 습득한다. (§F)
    /// 어떤 스킬이 걸려 있는지는 코드가 알지 않는다 — JSON 이 정한다. (규칙 1)
    /// </summary>
    private void OnDodgeSucceeded()
    {
        var db = GameDatabase.Instance;
        if (db == null)
            return;

        foreach (var skill in db.Skills.Values)
        {
            var trigger = skill.LearnTrigger;
            if (trigger == null || trigger.Type != SituationalRepeat)
                continue;
            if (Jobs.Has(skill.Id) || string.IsNullOrEmpty(trigger.Counter))
                continue;
            if (HpRatio > trigger.HpRatioAtMost)
                continue;

            int count = AddCounter(trigger.Counter);
            if (count < trigger.RequiredCount)
                continue;

            LearnSkill(skill.Id);
            Announce(trigger.Announce);
            DebugLog.Add($"{skill.Id} 습득");
        }
    }

    /// <summary>data/skills 의 learnTrigger.type 값.</summary>
    private const string SituationalRepeat = "situational_repeat";

    /// <summary>sk_warcry 등 자기 강화 버프. (§E)</summary>
    public float AttackPowerMultiplier() => _buffTimer > 0f ? _buffAttackMultiplier : 1f;

    public float BuffRemaining => Mathf.Max(0f, _buffTimer);

    private void UpdateDash(float dt)
    {
        Velocity = _dashDirection * CombatTuning.DashSpeed;

        _dashTimer -= dt;
        if (_dashTimer <= 0f)
        {
            _state = PlayerState.Normal;
            // 대시 종료 속도를 그대로 물려주면 미끄러진다. 이동 속도까지 즉시 깎는다.
            Velocity = _dashDirection * CombatTuning.MoveSpeed;
        }
    }

    private void StartDash(Vector2 input)
    {
        _dashDirection = input != Vector2.Zero ? input : _facing;
        _facing = _dashDirection;
        _state = PlayerState.Dash;
        _dashTimer = CombatTuning.DashDuration;
        _dashCooldownTimer = CombatTuning.DashCooldown;
        _dashInvulnTimer = DashInvulnSeconds();

        CombatFeedback.Instance?.DashWindAt(GlobalPosition, _dashDirection,
            new Color(0.85f, 0.95f, 1f));
    }

    /// <summary>
    /// 기본 0.10s. sk_hidden_deathline 을 습득했다면 그 패시브 값(0.20s)으로 덮어쓴다. (§E)
    /// </summary>
    protected float DashInvulnSeconds()
    {
        float value = CombatTuning.DashInvuln;

        foreach (string skillId in Jobs.LearnedSkills)
        {
            var passive = GameDatabase.Instance?.GetSkill(skillId)?.Passive;
            if (passive != null && passive.DashInvulnSeconds > value)
                value = passive.DashInvulnSeconds;
        }

        return value;
    }

    /// <summary>
    /// 가속/감속. 목표 속도까지 걸리는 시간이 AccelTime / DecelTime 이 되도록
    /// 초당 변화량을 계산한다. 프레임레이트에 의존하지 않는다. (§C-1)
    /// </summary>
    private void ApplyAcceleration(Vector2 target, float dt, bool accelerating)
    {
        float rampTime = accelerating ? CombatTuning.AccelTime : CombatTuning.DecelTime;
        if (rampTime <= 0f)
        {
            Velocity = target;
            return;
        }

        float ratePerSecond = CombatTuning.MoveSpeed / rampTime;
        Velocity = Velocity.MoveToward(target, ratePerSecond * dt);
    }

    /// <summary>8방향. 대각선은 반드시 정규화한다. (§C-1)</summary>
    private static Vector2 ReadMoveInput()
    {
        var input = new Vector2(
            Input.GetActionStrength(InputSetup.MoveRight) - Input.GetActionStrength(InputSetup.MoveLeft),
            Input.GetActionStrength(InputSetup.MoveDown) - Input.GetActionStrength(InputSetup.MoveUp));

        return input.LengthSquared() > 1f ? input.Normalized() : input;
    }

    /// <summary>
    /// 공격 모션. 선딜에 뒤로 당겼다가 판정에 내지르고 후딜에 돌아온다.
    /// 스프라이트 없이 '지금 때리는 중'을 읽히게 하는 유일한 수단이다. (규칙 5)
    /// </summary>
    private Vector2 AttackMotionOffset()
    {
        if (!_attack.IsBusy)
            return Vector2.Zero;

        Vector2 dir = _attack.LockedFacing;
        float t = EaseOut(_attack.PhaseProgress);

        return _attack.Phase switch
        {
            SkillPhase.Windup => -dir * (CombatTuning.MotionWindupBack * t),
            SkillPhase.Active => dir * (CombatTuning.MotionActiveForward * t),
            SkillPhase.Recovery => dir * (CombatTuning.MotionActiveForward * (1f - t)),
            _ => Vector2.Zero,
        };
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void UpdateVisuals(float dt)
    {
        _animTime += dt;

        // 무적 동안 10Hz 점멸. (§C-5) 실제 그리기는 _Draw 에서 이 값을 곱한다.
        bool blinkOff = false;
        if (_iframeTimer > 0f && CombatTuning.IFrameBlinkHz > 0f)
        {
            float period = 1f / CombatTuning.IFrameBlinkHz;
            blinkOff = Mathf.PosMod(_iframeTimer, period) < period * 0.5f;
        }
        _blinkAlpha = blinkOff ? 0.25f : 1f;

        // 스프라이트가 있으면 방향/걷기 프레임을 갱신하고, 상태 색과 점멸을 그대로 입힌다.
        if (_sprite != null && _sprite.IsLoaded)
        {
            Vector2 aim = _attack.IsBusy ? _attack.LockedFacing : _facing;
            (string clip, float? progress) = SpriteClip();
            _sprite.UpdateFrame(aim, clip, progress, dt);

            // 공격 모션·들썩임을 스프라이트에도 실어 준다.
            _sprite.Offset = FigureMotionOffset() / _sprite.Scale.X;
            _sprite.Modulate = Tinted(Colors.White);
            _sprite.Visible = _state != PlayerState.Dead;
        }

        Visible = _state != PlayerState.Dead;
    }

    /// <summary>
    /// 파츠 색에 현재 상태(피격 플래시·대시·가드·반격·버프)와 점멸을 입힌다.
    /// 색 슬롯(꾸미기)을 지우지 않도록 상태는 '덮어쓰기(플래시)'만 전체색이고
    /// 나머지는 원래 색을 상태색 쪽으로 살짝 섞는다. (§A 아트 방침)
    /// </summary>
    private Color Tinted(Color part)
    {
        Color c = part;

        if (_flashTimer > 0f)
            c = Colors.White;                              // 피격 순간 — 전체 흰색
        else if (_state == PlayerState.Dash)
            c = part.Lightened(0.3f);
        else if (_riposteTimer > 0f)
            c = part.Lerp(PlayerTuning.RiposteColor, 0.45f);
        else if (_guarding)
            c = part.Lerp(PlayerTuning.GuardColor, 0.4f);
        else if (_buffTimer > 0f)
            c = part.Lerp(PlayerTuning.BuffColor, 0.4f);

        c.A *= _blinkAlpha;
        return c;
    }

    // --- IAnimationDriver --------------------------------------------------
    //
    // 애니메이션 상태머신이 '읽기만' 하는 창구. 전투 로직은 여기 아무것도 의존하지 않으므로
    // 머신을 떼어내도 게임은 그대로 돌아간다. (관찰자 방식)

    /// <summary>지금 프레임의 애니메이션 상태. 우선순위: 사망 > 피격 > 스킬 > 대시 > 가드 > 이동.</summary>
    public string CurrentAnimationState
    {
        get
        {
            if (_state == PlayerState.Dead)
                return AnimationStates.Dead;
            if (_knockbackTimer > 0f || _flashTimer > 0f)
                return AnimationStates.Hurt;

            if (_attack.IsBusy)
            {
                // 돌진이 붙은 스킬(강타)은 별도 상태 — 후딜이 캔슬 불가라 구분이 필요하다.
                bool heavy = _attack.Skill?.Dash != null;
                return _attack.Phase switch
                {
                    SkillPhase.Windup => heavy ? AnimationStates.HeavyWindup : AnimationStates.AttackWindup,
                    SkillPhase.Active => heavy ? AnimationStates.HeavyActive : AnimationStates.AttackActive,
                    SkillPhase.Recovery => heavy ? AnimationStates.HeavyRecovery : AnimationStates.AttackRecovery,
                    _ => AnimationStates.Idle,
                };
            }

            if (_state == PlayerState.Dash)
                return AnimationStates.Dash;
            if (_guarding)
                return AnimationStates.GuardHold;

            if (Velocity.Length() > 5f)
                return IsRunning ? AnimationStates.Run : AnimationStates.Walk;

            return AnimationStates.Idle;
        }
    }

    /// <summary>판정 프레임이 열려 있는가. 실제 히트박스가 도는 구간과 정확히 같다.</summary>
    public bool AnimationHitboxActive => _attack.IsActive;

    /// <summary>
    /// 지금 프레임에 재생할 스프라이트 클립과 진행도.
    /// progress 가 있으면 1회성(선딜→판정→후딜 전체를 한 번 훑는다), null 이면 루프.
    /// 에셋이 없는 클립은 PlayerSprite 가 idle 로 폴백한다.
    /// </summary>
    private (string clip, float? progress) SpriteClip()
    {
        if (_state == PlayerState.Dash)
            return ("dash", null);

        if (_attack.IsBusy)
        {
            var skill = _attack.Skill;
            string clip = skill?.Stun != null ? "shout"
                : skill?.Dash != null ? "heavy"
                : "punch";

            // 스킬 한 번을 클립 한 바퀴에 매핑한다 — 선딜/판정/후딜 비율 그대로.
            float w = CombatTuning.AttackWindup;
            float a = CombatTuning.AttackActive;
            float r = CombatTuning.AttackRecovery;
            float total = Mathf.Max(0.0001f, w + a + r);
            float done = _attack.Phase switch
            {
                SkillPhase.Windup => w * _attack.PhaseProgress,
                SkillPhase.Active => w + a * _attack.PhaseProgress,
                SkillPhase.Recovery => w + a + r * _attack.PhaseProgress,
                _ => 0f,
            };
            return (clip, done / total);
        }

        if (_guarding)
            return ("guard", null);

        if (_state == PlayerState.Normal && Velocity.Length() > 5f)
            return (IsRunning ? "run" : "walk", null);

        // 대기 — 숨쉬기 클립이 있으면 그걸 루프, 없으면 idle 한 장.
        return ("breathe", null);
    }

    /// <summary>겉모습을 바꾸고 저장한다. (임시 데모 진입점 — C 키)</summary>
    private void RandomizeAppearance()
    {
        _appearance.Randomize(_rng);
        QueueRedraw();
        SaveSystem.Instance?.Save(this, "customize");
        DebugLog.Add("겉모습 변경");
    }

    public override void _Draw()
    {
        if (_state == PlayerState.Dead)
            return;

        DrawFigure();
        DrawLockMarker();
        DrawWeapon();
        DrawHitboxDebug();
    }

    /// <summary>
    /// 세워 그린 사람 피규어 — 그림자·다리·몸통·팔·손·머리·머리카락·눈.
    /// 파츠가 색 슬롯별로 나뉘어 있어 꾸미기가 바로 얹힌다. 도형만. (규칙 5)
    /// 공격 모션(AttackMotionOffset)은 몸에만 싣고 그림자는 제자리에 둔다.
    /// </summary>
    /// <summary>
    /// 공격 모션 + 대기/걸음 들썩임을 합친 몸 오프셋.
    /// 도형 피규어와 스프라이트가 같은 값을 써야 연출이 어긋나지 않는다.
    /// </summary>
    private Vector2 FigureMotionOffset()
    {
        float s = PlayerTuning.FigureScale;
        Vector2 m = AttackMotionOffset();

        // 스프라이트가 그 동작의 프레임을 직접 갖고 있으면 도형 시절의 들썩임은 얹지 않는다.
        // (걷기·달리기는 발이 프레임에 그려져 있고, 대기는 숨쉬기 클립이 대신한다.)
        bool spriteHandlesMotion = _sprite != null && _sprite.IsLoaded;

        if (!_attack.IsBusy && !spriteHandlesMotion)
        {
            bool moving = _state == PlayerState.Normal && Velocity.Length() > 5f;
            bool running = moving && IsRunning;

            float amp = running ? PlayerTuning.RunBobAmp
                : moving ? PlayerTuning.WalkBobAmp : PlayerTuning.IdleBobAmp;
            float hz = running ? PlayerTuning.RunBobHz
                : moving ? PlayerTuning.WalkBobHz : PlayerTuning.IdleBobHz;

            // 절댓값 sine — 바닥을 딛고 튀어오르는 것처럼 위로만 들썩인다.
            m.Y -= Mathf.Abs(Mathf.Sin(_animTime * hz * Mathf.Pi)) * amp * s;

            // 달릴 때는 진행 방향으로 살짝 기운다.
            if (running)
                m += Velocity.Normalized() * (PlayerTuning.RunLeanPx * s);
        }

        return m;
    }

    private void DrawFigure()
    {
        float s = PlayerTuning.FigureScale;
        Vector2 m = FigureMotionOffset();

        // 그림자 — 발밑 납작한 타원. 스프라이트를 쓸 때도 그림자는 여기서 그린다.
        DrawSetTransform(new Vector2(0f, 8f * s), 0f, new Vector2(1f, 0.4f));
        DrawCircle(Vector2.Zero, 5f * s, PlayerTuning.ShadowColor);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // 스프라이트가 몸을 그리면 도형 파츠는 건너뛴다(그림자만 남긴다).
        if (_sprite != null && _sprite.IsLoaded)
            return;

        // 색 슬롯 + 상태 틴트. 명암용 밝은/어두운 변형도 같이 만든다(광원 좌상단 가정).
        Color skin = Tinted(_appearance.Skin);
        Color skinDark = Tinted(_appearance.Skin.Darkened(0.16f));
        Color hair = Tinted(_appearance.Hair);
        Color hairLit = Tinted(_appearance.Hair.Lightened(0.28f));
        Color shirt = Tinted(_appearance.Shirt);
        Color shirtLit = Tinted(_appearance.Shirt.Lightened(0.22f));
        Color shirtDark = Tinted(_appearance.Shirt.Darkened(0.3f));
        Color pants = Tinted(_appearance.Pants);
        Color pantsDark = Tinted(_appearance.Pants.Darkened(0.32f));
        Color line = Tinted(PlayerTuning.OutlineColor);
        Color belt = Tinted(PlayerTuning.BeltColor);

        int face = FaceDir();   // 0 아래(정면) 1 위(뒤) 2 좌 3 우

        // 다리 + 부츠(아래쪽 그늘)
        var legL = new Rect2(m.X - 3f * s, m.Y + 3f * s, 2.4f * s, 5f * s);
        var legR = new Rect2(m.X + 0.6f * s, m.Y + 3f * s, 2.4f * s, 5f * s);
        OutlinedRect(legL, pants, line);
        OutlinedRect(legR, pants, line);
        DrawRect(new Rect2(legL.Position.X, m.Y + 6.4f * s, legL.Size.X, 1.6f * s), pantsDark);
        DrawRect(new Rect2(legR.Position.X, m.Y + 6.4f * s, legR.Size.X, 1.6f * s), pantsDark);

        // 팔 + 손
        var armL = new Rect2(m.X - 5.6f * s, m.Y - 2f * s, 1.8f * s, 5.5f * s);
        var armR = new Rect2(m.X + 3.8f * s, m.Y - 2f * s, 1.8f * s, 5.5f * s);
        OutlinedRect(armL, shirt, line);
        OutlinedRect(armR, shirt, line);
        DrawRect(new Rect2(m.X - 5.6f * s, m.Y + 3f * s, 1.8f * s, 1.8f * s), skin);
        DrawRect(new Rect2(m.X + 3.8f * s, m.Y + 3f * s, 1.8f * s, 1.8f * s), skin);

        // 몸통(가슴 갑옷) — 외곽선 + 왼쪽 그늘 + 가슴판 하이라이트 + 벨트 + 버클
        var torso = new Rect2(m.X - 4f * s, m.Y - 3f * s, 8f * s, 7f * s);
        OutlinedRect(torso, shirt, line);
        DrawRect(new Rect2(torso.Position.X, m.Y - 3f * s, 1.4f * s, 7f * s), shirtDark);
        DrawRect(new Rect2(m.X - 1.6f * s, m.Y - 3f * s, 3.2f * s, 4.8f * s), shirtLit);
        DrawRect(new Rect2(torso.Position.X, m.Y + 2.6f * s, torso.Size.X, 1.3f * s), belt);
        DrawRect(new Rect2(m.X - 0.7f * s, m.Y + 2.7f * s, 1.4f * s, 1.1f * s), shirtLit);

        // 어깨 패드 — 팔 위에 얹는 넓은 조각.
        DrawRect(new Rect2(armL.Position.X - 0.5f * s, m.Y - 2.6f * s, armL.Size.X + 1f * s, 1.9f * s), shirtDark);
        DrawRect(new Rect2(armR.Position.X - 0.5f * s, m.Y - 2.6f * s, armR.Size.X + 1f * s, 1.9f * s), shirtDark);

        // 머리 — 외곽선 + 피부 + 턱 그늘
        Vector2 head = new(m.X, m.Y - 7f * s);
        float headR = 4f * s;
        DrawCircle(head, headR + PlayerTuning.OutlineWidth, line);
        DrawCircle(head, headR, skin);
        DrawArc(head, headR * 0.72f, 0.3f, Mathf.Pi - 0.3f, 10, skinDark, 1.1f * s);

        // 머리카락 + 하이라이트(위쪽 얇은 밝은 호)
        float hairInner = face == 1 ? 3.2f * s : 2.6f * s;
        DrawArc(head, headR - hairInner * 0.5f, Mathf.Pi, Mathf.Tau, 16, hair, hairInner + 1.2f * s);
        DrawArc(head, headR - hairInner * 0.35f, Mathf.Pi + 0.5f, Mathf.Tau - 0.5f, 10, hairLit, 1f * s);
        if (face == 1)   // 뒤통수 — 얼굴 대신 머리로 채운다
            DrawCircle(head, headR * 0.7f, hair);

        // 눈 — 바라보는 쪽에만. 뒤를 보면 안 그린다.
        if (face != 1)
            DrawEyes(head, headR, face, skin);
    }

    /// <summary>외곽선 있는 사각 파츠 — 살짝 큰 어두운 사각을 뒤에 깔고 색을 얹는다.</summary>
    private void OutlinedRect(Rect2 rect, Color fill, Color outline)
    {
        DrawRect(rect.Grow(PlayerTuning.OutlineWidth), outline);
        DrawRect(rect, fill);
    }

    private void DrawEyes(Vector2 head, float headR, int face, Color skin)
    {
        var eye = new Color(0.12f, 0.12f, 0.16f, skin.A);
        float r = headR * 0.15f;
        float y = head.Y + headR * 0.05f;

        // 좌/우를 볼 땐 두 눈을 그쪽으로 몰고, 정면이면 좌우로 벌린다.
        float cx = head.X + face switch { 2 => -headR * 0.32f, 3 => headR * 0.32f, _ => 0f };
        float spread = face == 0 ? headR * 0.4f : headR * 0.24f;

        DrawCircle(new Vector2(cx - spread, y), r, eye);
        DrawCircle(new Vector2(cx + spread, y), r, eye);
    }

    /// <summary>바라보는 4방향. 0 아래(정면), 1 위(뒤), 2 좌, 3 우.</summary>
    private int FaceDir()
    {
        Vector2 f = _attack.IsBusy ? _attack.LockedFacing : _facing;
        if (f == Vector2.Zero)
            return 0;
        if (Mathf.Abs(f.X) > Mathf.Abs(f.Y))
            return f.X < 0f ? 2 : 3;
        return f.Y < 0f ? 1 : 0;
    }

    /// <summary>고정된 대상에 표식. 누구를 보고 있는지 알 수 없으면 시선 고정이 무의미하다.</summary>
    private void DrawLockMarker()
    {
        if (_lockTarget == null || !IsInstanceValid(_lockTarget))
            return;

        Vector2 local = _lockTarget.GlobalPosition - GlobalPosition;
        float radius = _lockTarget.Stats.Radius + 4f;
        var color = new Color(1f, 0.95f, 0.6f, 0.75f);

        // 네 방향 짧은 갈고리 — 조준 표식처럼 보이게. (규칙 5)
        for (int i = 0; i < 4; i++)
        {
            float angle = Mathf.Tau * i / 4f + Mathf.Pi / 4f;
            Vector2 dir = Vector2.Right.Rotated(angle);
            DrawLine(local + dir * radius, local + dir * (radius + 3f), color, 1f);
        }
    }

    /// <summary>
    /// 스킬 종류에 맞는 무기 모션. 스킬 id 를 박아 넣지 않고 데이터의 성질로 고른다
    /// (돌진이 있는가 / 경직만 주는가 / 강타격인가). 규칙 1·5.
    /// </summary>
    private void DrawWeapon()
    {
        // 스프라이트가 손에 든 것과 자세를 이미 그린다 — 도형 무기를 겹쳐 그리면
        // 동작이 가려지고 두 개로 보인다. 스프라이트가 있으면 함성 고리만 남긴다.
        bool hasSprite = _sprite != null && _sprite.IsLoaded;

        if (_attack.IsBusy && _attack.Skill != null)
        {
            var skill = _attack.Skill;

            if (skill.Stun != null)
                DrawWarcryRing();          // 함성 — 퍼지는 고리
            else if (!hasSprite)
                DrawBlade(skill, PoseFor(skill, _attack.Phase, _attack.PhaseProgress));
            return;
        }

        if ((_guarding || _guardCooldown > 0f) && !hasSprite)
            DrawShield();
    }

    /// <summary>
    /// 무기의 현재 자세. 자루(Inner) ~ 칼끝(Outer) 거리와 바라보는 방향 기준 각도(rad).
    /// 그리기와 판정이 같은 함수를 쓰므로 보이는 궤적과 히트박스가 어긋날 수 없다.
    /// </summary>
    private readonly record struct WeaponPose(float AngleOffset, float Inner, float Outer, float Width);

    private static WeaponPose PoseFor(SkillDefinition skill, SkillPhase phase, float progress)
    {
        float t = EaseOut(progress);

        // 돌진이 붙은 스킬(강타)은 찌르기 — 각도 고정, 앞뒤로 뺐다가 내민다.
        if (skill.Dash != null)
        {
            float reach = phase switch
            {
                SkillPhase.Windup => Mathf.Lerp(6f, 2f, t),
                SkillPhase.Active => Mathf.Lerp(2f, 1f, t),
                _ => Mathf.Lerp(1f, 6f, t),
            };
            float thrust = PlayerTuning.SwordLength * 1.3f;
            return new WeaponPose(0f, reach, reach + thrust, PlayerTuning.SwordWidth + 1f);
        }

        // 그 외는 베기 — 뒤로 감아올렸다가 호를 그리며 베어 낸다.
        float half = Mathf.DegToRad(skill.Shape?.AngleDeg ?? 90f) * 0.5f;
        float swing = phase switch
        {
            SkillPhase.Windup => Mathf.Lerp(0f, -half * 1.2f, t),
            SkillPhase.Active => Mathf.Lerp(-half * 1.2f, half, t),
            _ => Mathf.Lerp(half, 0f, t),
        };
        float length = PlayerTuning.SwordLength * (skill.Heavy ? 1.25f : 1f);
        return new WeaponPose(swing, 3f, length, PlayerTuning.SwordWidth);
    }

    /// <summary>칼날 + 이번 스윙에서 훑은 궤적. 궤적 띠가 곧 히트박스다.</summary>
    private void DrawBlade(SkillDefinition skill, WeaponPose pose)
    {
        float baseAngle = _attack.LockedFacing.Angle();
        Vector2 dir = Vector2.Right.Rotated(baseAngle + pose.AngleOffset);

        Color color = _attack.IsActive
            ? (skill.Heavy ? PlayerTuning.SwordHotColor : PlayerTuning.SwordColor)
            : PlayerTuning.SwordColor.Darkened(0.3f);

        // 판정 중이면 스윙 시작점부터 지금까지 훑은 띠를 먼저 깔고 칼날을 그린다.
        if (_attack.IsActive && !Mathf.IsEqualApprox(_swingStartAngle, pose.AngleOffset))
        {
            float lo = Mathf.Min(_swingStartAngle, pose.AngleOffset);
            float hi = Mathf.Max(_swingStartAngle, pose.AngleOffset);
            float mid = (pose.Inner + pose.Outer) * 0.5f;

            DrawArc(Vector2.Zero, mid, baseAngle + lo, baseAngle + hi, 12,
                new Color(color.R, color.G, color.B, 0.28f), pose.Outer - pose.Inner);
        }

        DrawLine(dir * pose.Inner, dir * pose.Outer, color, pose.Width);

        // 돌진 중엔 뒤로 속도선.
        if (_attack.IsActive && skill.Dash != null)
            DrawLine(-dir * 4f, -dir * 14f, new Color(1f, 1f, 1f, 0.25f), 2f);
    }

    /// <summary>함성 — 몸에서 퍼져 나가는 고리.</summary>
    private void DrawWarcryRing()
    {
        float radius = _attack.Skill?.Stun?.RadiusPx ?? 0f;
        if (radius <= 0f)
            return;

        float t = _attack.Phase == SkillPhase.Windup
            ? _attack.PhaseProgress * 0.3f
            : 0.3f + EaseOut(_attack.PhaseProgress) * 0.7f;

        DrawArc(Vector2.Zero, radius * t, 0f, Mathf.Tau, 32,
            new Color(1f, 0.8f, 0.4f, 1f - t), 2f);
    }

    /// <summary>
    /// 방패. 바라보는 쪽에 세운다. 퍼펙트 창 동안은 밝게, 쿨다운 중에는 흐리게 —
    /// 지금 막을 수 있는지가 색으로 보여야 한다.
    /// </summary>
    private void DrawShield()
    {
        var guard = GuardSkill()?.Guard;
        Vector2 dir = _facing != Vector2.Zero ? _facing.Normalized() : Vector2.Right;
        float angle = dir.Angle();

        Color color;
        if (_guardCooldown > 0f && !_guarding)
            color = PlayerTuning.ShieldCooldownColor;
        else if (guard != null && _guardElapsed <= guard.PerfectWindow)
            color = PlayerTuning.ShieldPerfectColor;   // 퍼펙트 판정 창
        else
            color = PlayerTuning.ShieldColor;

        // 방패를 바라보는 방향에 직사각형으로 세운다.
        Vector2 center = dir * PlayerTuning.ShieldDistance;
        Vector2 across = Vector2.Right.Rotated(angle + Mathf.Pi / 2f);
        Vector2 along = dir;

        float hw = PlayerTuning.ShieldWidth * 0.5f;
        float hh = PlayerTuning.ShieldHeight * 0.5f;

        var corners = new[]
        {
            center + across * hh + along * hw,
            center - across * hh + along * hw,
            center - across * hh - along * hw,
            center + across * hh - along * hw,
        };
        DrawColoredPolygon(corners, color);
    }

    /// <summary>
    /// F2 — 판정 범위 표시. 실제 판정과 같은 값으로 그린다.
    /// 무기 스킬은 칼날이 훑는 띠, 지원 스킬은 형태 정의의 원. (§I)
    /// </summary>
    private void DrawHitboxDebug()
    {
        if (!DebugFlags.ShowHitbox || !_attack.IsBusy || _attack.Skill == null)
            return;

        var skill = _attack.Skill;

        // 선딜은 옅게, 판정 프레임은 진하게.
        Color color = _attack.IsActive
            ? new Color(1f, 0.3f, 0.3f, 0.35f)
            : new Color(1f, 1f, 1f, 0.12f);

        if (skill.Stun != null)
        {
            if (skill.Shape != null)
                DrawCircle(Vector2.Zero, skill.Shape.RangePx, color);
            return;
        }

        var pose = PoseFor(skill, _attack.Phase, _attack.PhaseProgress);
        float baseAngle = _attack.LockedFacing.Angle();

        // 판정 중이면 스윙 시작 ~ 현재, 선딜이면 지금 자세만.
        float from = _attack.IsActive ? _swingStartAngle : pose.AngleOffset;
        float lo = Mathf.Min(from, pose.AngleOffset);
        float hi = Mathf.Max(from, pose.AngleOffset);

        DrawArc(Vector2.Zero, (pose.Inner + pose.Outer) * 0.5f,
            baseAngle + lo, baseAngle + hi, 16, color, pose.Outer - pose.Inner);
    }

    // --- 피해 / 사망 -------------------------------------------------------

    /// <summary>이 엔티티만 dt 초 동안 멈춘다. (§C-3)</summary>
    public void ApplyHitstop(float seconds)
    {
        if (seconds > _hitstopTimer)
            _hitstopTimer = seconds;
    }

    public bool TakeDamage(in DamageInfo info)
    {
        if (!IsAlive)
            return false;

        // 대시 무적으로 흘렸다 — 회피 성공. (§F sk_hidden_deathline 트리거)
        if (_dashInvulnTimer > 0f)
        {
            DodgeSucceeded?.Invoke();
            return false;
        }

        if (_iframeTimer > 0f)
            return false;

        float amount = ApplyDamageReduction(info, out bool nullified);
        if (nullified)
            return false;

        Hp -= amount;
        _iframeTimer = CombatTuning.PlayerIFrames;
        _flashTimer = CombatTuning.HitFlashTime;
        ApplyHitstop(CombatTuning.HitstopNormal);
        ApplyKnockback(info.Direction, info.Heavy);
        CombatFeedback.Instance?.OnPlayerHurt(GlobalPosition, amount);
        DebugLog.Add($"피격 {info.SkillId} -{amount:0.#} (hp {Hp:0}/{MaxHp:0})");

        if (Hp <= 0f)
            Die();

        return true;
    }

    /// <summary>
    /// 가드 중이면 피해 60% 감소. 가드를 올린 지 PerfectWindow 안에 맞았으면
    /// 퍼펙트 가드 — 피해 0 + 반격 상태 + 근접 상대 카운터. (§E sk_guard)
    /// </summary>
    private float ApplyDamageReduction(in DamageInfo info, out bool nullified)
    {
        nullified = false;

        var guard = GuardSkill()?.Guard;
        if (!_guarding || guard == null)
            return info.Amount;

        if (_guardElapsed <= guard.PerfectWindow)
        {
            nullified = true;
            _riposteTimer = guard.RiposteSeconds;
            // 정확히 막았다 — 쿨다운을 초기화해 곧바로 다시 막을 수 있게 한다. (§E)
            _perfectThisGuard = true;
            _guardCooldown = 0f;
            CombatFeedback.Instance?.Popup(GlobalPosition, 0f, heavy: true, onPlayer: false);
            DebugLog.Add($"퍼펙트 가드 (반격 {guard.RiposteSeconds:0.##}s)");
            TryCounter(info, guard);
            return 0f;
        }

        DebugLog.Add($"가드 x{guard.DamageMultiplier:0.##}");
        return info.Amount * guard.DamageMultiplier;
    }

    /// <summary>
    /// 퍼펙트 가드 카운터. 되받아칠 상대가 카운터 거리 안의 근접 몹일 때만,
    /// 받을 뻔한 피해의 CounterMultiplier 배를 강타격으로 돌려주고 기절시킨다.
    /// 화면 흔들림은 강타격 경로(CombatFeedback.OnHit)가 함께 처리한다.
    /// </summary>
    private void TryCounter(in DamageInfo info, SkillGuard guard)
    {
        if (guard.CounterMultiplier <= 0f || info.Source is not MonsterBase attacker || !attacker.IsAlive)
            return;

        // 화살처럼 멀리서 온 공격은 되받아치지 않는다 — 근접 몹만 카운터 대상.
        if (GlobalPosition.DistanceTo(attacker.GlobalPosition) > guard.CounterRange)
            return;

        float damage = info.Amount * guard.CounterMultiplier;
        Vector2 toAttacker = (attacker.GlobalPosition - GlobalPosition).Normalized();

        bool landed = attacker.TakeDamage(new DamageInfo
        {
            Amount = damage,
            Direction = toAttacker,
            Heavy = true,
            SkillId = "guard_counter",
            Source = this,
        });

        if (guard.CounterStunSeconds > 0f)
            attacker.Stun(guard.CounterStunSeconds);

        // 반격은 플레이어가 손해 없이 반격했다는 신호이므로 크게 확인시켜 준다.
        ApplyHitstop(CombatTuning.HitstopHeavy);
        CombatFeedback.Instance?.OnHit(attacker.GlobalPosition, damage, heavy: true, killed: !attacker.IsAlive);
        DebugLog.Add($"카운터! {attacker.Stats.Id} -{damage:0.#}"
            + (guard.CounterStunSeconds > 0f ? $" 기절 {guard.CounterStunSeconds:0.##}s" : ""));
    }

    /// <summary>ease-out 넉백. 총 이동 거리가 정확히 명세값이 되게 잡는다. (§C-4)</summary>
    private void ApplyKnockback(Vector2 direction, bool heavy)
    {
        if (direction == Vector2.Zero)
            return;

        float distance = heavy ? CombatTuning.KnockbackHeavyDistance : CombatTuning.KnockbackDistance;
        float time = heavy ? CombatTuning.KnockbackHeavyTime : CombatTuning.KnockbackTime;
        if (distance <= 0f || time <= 0f)
            return;

        _attack.Cancel();
        _knockbackVelocity = direction.Normalized() * (2f * distance / time);
        _knockbackTotal = time;
        _knockbackTimer = time;
    }

    private void UpdateKnockback(float dt)
    {
        _knockbackTimer -= dt;
        Velocity = _knockbackVelocity * Mathf.Max(0f, _knockbackTimer / _knockbackTotal);
    }

    private void Respawn()
    {
        GlobalPosition = WorldLayout.SpawnPoint;
        Hp = MaxHp;
        Velocity = Vector2.Zero;
        _state = PlayerState.Normal;
        ResetCombatState();
        _iframeTimer = CombatTuning.PlayerIFrames;
    }

    /// <summary>사망 시 마을 지점 부활 + 플래그 기록. (§D-1)</summary>
    protected virtual void Die()
    {
        _state = PlayerState.Dead;
        Hp = 0f;
        Velocity = Vector2.Zero;
        _respawnTimer = PlayerTuning.RespawnDelay;
        _attack.Cancel();
        _lungeTimer = 0f;

        CombatFeedback.Instance?.DeathBurstAt(GlobalPosition, PlayerTuning.BodyColor);

        SetFlag(PlayerTuning.FlagDiedOnce);
        AddCounter(PlayerTuning.CounterDeaths);
    }

    // --- IPlayerContext ----------------------------------------------------

    public int GetBaseStat(string stat)
        => _baseStats.TryGetValue(stat, out int value) ? value : 0;

    /// <summary>기본 스탯 + 직업 랭크 보너스. 전투 계산은 항상 이쪽. (§D-1)</summary>
    public int GetStat(string stat) => GetBaseStat(stat) + Jobs.StatTotal(stat);

    public bool HasFlag(string flag) => _flags.Contains(flag);

    public void SetFlag(string flag)
    {
        if (!string.IsNullOrEmpty(flag))
            _flags.Add(flag);
    }

    public IReadOnlyCollection<string> Flags => _flags;
    public IReadOnlyDictionary<string, int> Counters => _counters;

    public int GetCounter(string key)
        => _counters.TryGetValue(key, out int value) ? value : 0;

    public int AddCounter(string key, int delta = 1)
    {
        int value = GetCounter(key) + delta;
        _counters[key] = value;
        return value;
    }

    public void Announce(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Announced?.Invoke(message);
    }

    public void ReplaceSkill(string oldSkillId, string newSkillId)
        => Jobs.Replace(oldSkillId, newSkillId);

    public void LearnSkill(string skillId) => Jobs.Learn(skillId);

    public SaveData CaptureSave() => new()
    {
        Version = SaveSystem.CurrentVersion,
        Level = Level,
        Hp = Hp,
        Position = new SavePosition { X = GlobalPosition.X, Y = GlobalPosition.Y },
        BaseStats = new Dictionary<string, int>(_baseStats),
        JobState = new SaveJobState
        {
            CurrentJobId = Jobs.CurrentJobId,
            History = Jobs.CopyHistory(),
            LearnedSkills = Jobs.CopyLearnedSkills(),
            CarriedSkillSlots = Jobs.CopyCarriedSlots(),
            InheritedStats = Jobs.CopyInheritedStats(),
        },
        Mastery = Mastery.Snapshot(),
        Flags = new List<string>(_flags),
        Counters = new Dictionary<string, int>(_counters),
        Appearance = _appearance.ToSave(),
    };

    public void RestoreSave(SaveData data)
    {
        if (data == null)
        {
            ResetToNewGame();
            return;
        }

        _baseStats.Clear();
        if (data.BaseStats != null && data.BaseStats.Count > 0)
        {
            foreach (var pair in data.BaseStats)
                _baseStats[pair.Key] = pair.Value;
        }
        else
        {
            foreach (var pair in PlayerTuning.BaseStats)
                _baseStats[pair.Key] = pair.Value;
        }

        Level = Mathf.Max(1, data.Level);
        MaxHp = PlayerTuning.BaseMaxHp;
        Hp = Mathf.Clamp(data.Hp, 1f, MaxHp);

        var job = data.JobState ?? new SaveJobState();
        Jobs.RestoreFrom(job.CurrentJobId, job.History, job.LearnedSkills,
            job.CarriedSkillSlots, job.InheritedStats);

        // 세이브가 비었거나 깨졌으면 시작 직업으로 되돌린다 — 크래시 대신 폴백. (§H)
        if (string.IsNullOrEmpty(Jobs.CurrentJobId))
            Jobs.StartJob(PlayerTuning.StartingJobId, GameDatabase.Instance);

        Mastery.Restore(data.Mastery);

        _flags.Clear();
        if (data.Flags != null)
        {
            foreach (string flag in data.Flags)
                SetFlag(flag);
        }

        _counters.Clear();
        if (data.Counters != null)
        {
            foreach (var pair in data.Counters)
                _counters[pair.Key] = pair.Value;
        }

        if (data.Position != null)
            GlobalPosition = new Vector2(data.Position.X, data.Position.Y);

        _appearance = CharacterAppearance.FromSave(data.Appearance);

        _state = PlayerState.Normal;
        Velocity = Vector2.Zero;
        ResetCombatState();
        QueueRedraw();
    }
}
