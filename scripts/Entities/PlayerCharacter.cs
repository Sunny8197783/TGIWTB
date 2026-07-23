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
public partial class PlayerCharacter : CharacterBody2D, IPlayerContext, IDamageable
{
    /// <summary>몬스터가 플레이어를 찾는 그룹 이름.</summary>
    public const string Group = "player";

    private ColorRect _body;
    private ColorRect _facingMarker;

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
        ResetToNewGame();
    }

    private void BuildShapes()
    {
        float size = PlayerTuning.BodySize;

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(size, size) },
        };
        AddChild(shape);

        _body = new ColorRect
        {
            Size = new Vector2(size, size),
            Position = new Vector2(-size * 0.5f, -size * 0.5f),
            Color = PlayerTuning.BodyColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_body);

        float marker = PlayerTuning.FacingMarkerSize;
        _facingMarker = new ColorRect
        {
            Size = new Vector2(marker, marker),
            Color = PlayerTuning.FacingColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_facingMarker);
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
        _attack.Cancel();

        // P1 은 job_warrior R1 고정. 랭크 1 스킬을 GameDb 에서 실제로 지급받는다. (§D-1)
        Jobs.RestoreFrom("", null, null, null, null);
        Jobs.StartJob(PlayerTuning.StartingJobId, GameDatabase.Instance);
        SetFlag($"held_{PlayerTuning.StartingJobId}");
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

        if (DebugFlags.ShowHitbox)
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

        if (_state == PlayerState.Dead)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f)
                Respawn();
        }
    }

    private void UpdateNormal(float dt)
    {
        Vector2 input = ReadMoveInput();

        // 선딜 중에만 방향 전환 가능. 판정이 시작되면 고정된다. (§C-7)
        if (input != Vector2.Zero && (!_attack.IsBusy || _attack.CanTurn))
        {
            _facing = input;
            _attack.Aim(input);
        }

        AdvanceAttack(dt);

        if (!_attack.IsBusy && Input.IsActionJustPressed(InputSetup.Dash) && _dashCooldownTimer <= 0f)
        {
            StartDash(input);
            return;
        }

        ReadSkillInput();

        // 후딜에는 이동 불가. (§C-2)
        Vector2 target = _attack.CanMove
            ? input * CombatTuning.MoveSpeed * _attack.MoveScale
            : Vector2.Zero;

        ApplyAcceleration(target, dt, _attack.CanMove && input != Vector2.Zero);
    }

    /// <summary>M3 에서는 J(sk_slash) 하나. M4 에서 나머지 키가 붙는다.</summary>
    protected virtual void ReadSkillInput()
    {
        if (Input.IsActionJustPressed(InputSetup.Attack))
            TryStartSkill(ResolveSkillForAction(InputSetup.Attack));
    }

    /// <summary>공격 프레임을 진행하고, 판정 프레임이면 히트박스를 돌린다.</summary>
    private void AdvanceAttack(float dt)
    {
        if (!_attack.IsBusy)
            return;

        _attack.Tick(dt);

        if (_attack.IsActive)
            ProcessActiveHitbox();
    }

    protected bool TryStartSkill(SkillDefinition skill)
    {
        if (skill == null || _attack.IsBusy)
            return false;

        _attack.Begin(skill, _facing);
        return true;
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

        foreach (Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
        {
            if (node is not MonsterBase monster || !IsInstanceValid(monster))
                continue;

            if (!Hitbox.Overlaps(skill.Shape, GlobalPosition, _attack.LockedFacing,
                    monster.GlobalPosition, monster.Stats.Radius))
                continue;

            // 한 스윙에 같은 대상은 한 번만.
            if (!_attack.TryMarkHit(monster.InstanceId))
                continue;

            Strike(monster, skill);
        }
    }

    private void Strike(MonsterBase monster, SkillDefinition skill)
    {
        bool targetWasAlive = monster.IsAlive;
        float damage = DamageMath.Compute(this, skill, AttackPowerMultiplier());

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
            Heavy = skill.Heavy,
            SkillId = skill.Id,
            Source = this,
        });

        if (!landed)
            return;

        bool killed = !monster.IsAlive;
        float hitstop = killed
            ? CombatTuning.HitstopKill
            : skill.Heavy ? CombatTuning.HitstopHeavy : CombatTuning.HitstopNormal;

        // 공격자와 피격자 양쪽이 같이 멈춘다. 이게 타격감의 80%. (§C-3)
        ApplyHitstop(hitstop);
        CombatFeedback.Instance?.OnHit(monster.GlobalPosition, damage, skill.Heavy, killed);
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
        }, skill);

        SkillUsed?.Invoke(result);
        OnMasteryResult(result, skill);
    }

    /// <summary>진화 처리는 M5 에서 붙인다.</summary>
    protected virtual void OnMasteryResult(MasteryResult result, SkillDefinition skill) { }

    /// <summary>함성 등 공격력 버프. M4 에서 붙인다.</summary>
    protected virtual float AttackPowerMultiplier() => 1f;

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

    private void UpdateVisuals(float dt)
    {
        _facingMarker.Position = _facing * PlayerTuning.FacingMarkerDistance
            - Vector2.One * (PlayerTuning.FacingMarkerSize * 0.5f);

        _body.Color = _flashTimer > 0f
            ? Colors.White
            : _state == PlayerState.Dash
                ? PlayerTuning.BodyColor.Lightened(0.35f)
                : BodyColorForState();

        // 무적 동안 10Hz 점멸. (§C-5)
        bool blinkOff = false;
        if (_iframeTimer > 0f && CombatTuning.IFrameBlinkHz > 0f)
        {
            float period = 1f / CombatTuning.IFrameBlinkHz;
            blinkOff = Mathf.PosMod(_iframeTimer, period) < period * 0.5f;
        }

        float alpha = blinkOff ? 0.25f : 1f;
        _body.Modulate = new Color(1f, 1f, 1f, alpha);
        _facingMarker.Modulate = new Color(1f, 1f, 1f, alpha);

        Visible = _state != PlayerState.Dead;
    }

    /// <summary>가드/버프 등 상태에 따른 몸 색. M4 에서 확장한다.</summary>
    protected virtual Color BodyColorForState() => PlayerTuning.BodyColor;

    /// <summary>F2 — 판정 범위 반투명 표시. (§I)</summary>
    public override void _Draw()
    {
        if (!DebugFlags.ShowHitbox || !_attack.IsBusy || _attack.Skill?.Shape == null)
            return;

        var shape = _attack.Skill.Shape;

        // 선딜은 옅게, 판정 프레임은 진하게.
        Color color = _attack.IsActive
            ? new Color(1f, 0.3f, 0.3f, 0.35f)
            : new Color(1f, 1f, 1f, 0.12f);

        if (shape.Kind == "circle")
        {
            DrawCircle(Vector2.Zero, shape.RangePx, color);
            return;
        }

        if (shape.Kind != "cone")
            return;

        const int segments = 16;
        float half = Mathf.DegToRad(shape.AngleDeg) * 0.5f;
        float baseAngle = _attack.LockedFacing.Angle();

        var points = new Vector2[segments + 2];
        points[0] = Vector2.Zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = baseAngle - half + 2f * half * i / segments;
            points[i + 1] = Vector2.Right.Rotated(angle) * shape.RangePx;
        }
        DrawColoredPolygon(points, color);
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

        float amount = ApplyDamageReduction(info.Amount, out bool nullified);
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

    /// <summary>가드/퍼펙트 가드는 M4 에서 붙인다.</summary>
    protected virtual float ApplyDamageReduction(float amount, out bool nullified)
    {
        nullified = false;
        return amount;
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
        _hitstopTimer = 0f;
        _knockbackTimer = 0f;
        _iframeTimer = CombatTuning.PlayerIFrames;
        _attack.Cancel();
    }

    /// <summary>사망 시 마을 지점 부활 + 플래그 기록. (§D-1)</summary>
    protected virtual void Die()
    {
        _state = PlayerState.Dead;
        Hp = 0f;
        Velocity = Vector2.Zero;
        _respawnTimer = PlayerTuning.RespawnDelay;
        _attack.Cancel();

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

        _state = PlayerState.Normal;
        Velocity = Vector2.Zero;
        _hitstopTimer = 0f;
        _knockbackTimer = 0f;
        _attack.Cancel();
    }
}
