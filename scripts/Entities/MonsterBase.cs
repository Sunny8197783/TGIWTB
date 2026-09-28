using System;
using Godot;
using PixelMmo.Balance;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

public enum MonsterState
{
    Idle,
    Chase,
    Windup,
    Attack,
    Recover,
    Hurt,
    Dead,
}

/// <summary>
/// 몬스터 공통 — 상태머신, 피해 수신, 넉백, 히트스톱, 흰색 플래시, 처치 연출.
/// 개별 몬스터는 UpdateAi 와 DrawShape 만 채운다. (§D-2)
/// </summary>
public abstract partial class MonsterBase : CharacterBody2D, IDamageable, IAnimationDriver
{
    public const string Group = "monsters";

    private float _hp;
    private float _hitstopTimer;
    private float _flashTimer;
    private float _knockbackTimer;
    private float _knockbackTotal;
    private Vector2 _knockbackVelocity;
    private float _deathTimer;
    private bool _burstSpawned;
    private float _stunTimer;

    protected MonsterState State { get; private set; } = MonsterState.Idle;
    protected float StateTimer;

    /// <summary>SetState 로 받은 원래 길이. 모션 진행도를 내는 데만 쓴다.</summary>
    private float _stateLength;

    /// <summary>현재 상태의 진행도 0~1. 그리기(선딜 예고 등)에 쓴다.</summary>
    protected float StateProgress => _stateLength > 0f
        ? Mathf.Clamp(1f - (StateTimer / _stateLength), 0f, 1f)
        : 1f;
    protected PlayerCharacter Player { get; private set; }
    protected Vector2 Facing { get; set; } = Vector2.Right;
    public Vector2 ArtFacing => Facing;
    public bool HitFlashing => _flashTimer > 0f;

    public abstract MonsterStats Stats { get; }

    public float Hp => _hp;
    public float HpRatio => Stats.MaxHp > 0f ? _hp / Stats.MaxHp : 0f;
    public bool IsAlive => State != MonsterState.Dead;
    public ulong InstanceId => GetInstanceId();
    public int Level => Stats.Level;

    /// <summary>처치되어 사라질 때. 스포너가 리스폰 타이머를 건다. (§G)</summary>
    public event Action<MonsterBase> Died;

    public override void _Ready()
    {
        Name = Stats.Id;
        AddToGroup(Group);
        CollisionLayer = CollisionLayers.Monster;
        CollisionMask = CollisionLayers.World;
        MotionMode = MotionModeEnum.Floating;

        var shape = new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = Stats.Radius },
        };
        AddChild(shape);

        _hp = Stats.MaxHp;
        Player = GetTree().GetFirstNodeInGroup(PlayerCharacter.Group) as PlayerCharacter;
    }

    /// <summary>
    /// 이 거리 밖의 몬스터는 아예 돌지 않는다.
    ///
    /// 3배 줌 1280x720 이면 화면에 들어오는 반경이 약 245px 이다. 그 1.7배 —
    /// 화면 끝에서 걸어 들어오는 놈이 이미 깨어 있을 만큼은 넓고,
    /// 맵 반대편 100마리가 매 프레임 MoveAndSlide 를 돌지는 않을 만큼은 좁다.
    /// </summary>
    private const float AwakeRangePx = 420f;

    /// <summary>화면에서 한참 떨어져 있는가. 죽는 중인 놈은 거리와 무관하게 계속 돈다(시체가 안 사라지면 샌다).</summary>
    private bool Sleeping()
    {
        if (State == MonsterState.Dead || Player == null || !IsInstanceValid(Player))
            return false;

        return GlobalPosition.DistanceSquaredTo(Player.GlobalPosition)
            > AwakeRangePx * AwakeRangePx;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 100마리가 전부 AI + MoveAndSlide 를 돌면 물리 스크립트에만 20ms 가 나간다.
        // 보이지도 않는 놈들이라 멈춰 있어도 화면에서 달라지는 게 없다.
        if (Sleeping())
            return;

        float dt = (float)delta;

        // 히트스톱 — 이 엔티티만 멈춘다. (§C-3)
        if (_hitstopTimer > 0f)
        {
            _hitstopTimer -= dt;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            if (_flashTimer <= 0f)
                QueueRedraw();
        }

        // 깨어 있는 동안은 매 프레임 다시 그린다 — 체력바·공격 모션이 다 여기서 갱신된다.
        // 자는 놈은 위에서 이미 빠져나갔다. 예전에는 하위 클래스(Ironjaw/GoblinArcher/
        // TrainingDummy)가 각자 _PhysicsProcess 를 덮어써서 조건 없이 이걸 불렀고,
        // 그래서 화면 밖 100마리가 프레임당 700번씩 다시 그려졌다. 부르는 곳은 여기 하나다.
        QueueRedraw();

        if (State == MonsterState.Dead)
        {
            UpdateDeath(dt);
            return;
        }

        // 경직 — sk_warcry. 넉백보다 우선한다. (§E)
        if (_stunTimer > 0f)
        {
            _stunTimer -= dt;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        StateTimer -= dt;

        if (_knockbackTimer > 0f)
        {
            UpdateKnockback(dt);
            MoveAndSlide();
            return;
        }

        if (Player == null || !IsInstanceValid(Player))
            Player = GetTree().GetFirstNodeInGroup(PlayerCharacter.Group) as PlayerCharacter;

        UpdateAi(dt);
        MoveAndSlide();
    }

    /// <summary>개별 몬스터의 행동. 넉백/히트스톱/사망 중에는 호출되지 않는다.</summary>
    protected abstract void UpdateAi(float delta);

    /// <summary>도형으로만 그린다. 스프라이트 금지. (CLAUDE.md 규칙 5)</summary>
    protected abstract void DrawShape(Color color);

    // 명암용 — 플레이어 피규어와 같은 방식(광원 좌상단). 피격 흰색이 들어와도
    // Lightened/Darkened 가 자연스럽게 처리된다.
    protected static readonly Color OutlineColor = new(0.06f, 0.06f, 0.08f);
    protected static readonly float OutlineWidth = 1.0f;
    protected static Color Lit(Color c) => c.Lightened(0.24f);
    protected static Color Dark(Color c) => c.Darkened(0.3f);

    /// <summary>진단용 — 자는 몬스터가 실제로 다시 그려지고 있는지 세는 카운터.</summary>
    public static int DrawCalls;

    public override void _Draw()
    {
        DrawCalls++;

        // 공격 모션만큼 몸을 밀어서 그린다. 위치(물리)는 건드리지 않는다.
        Vector2 motion = AttackMotionOffset();
        if (motion != Vector2.Zero)
            DrawSetTransform(motion, 0f, Vector2.One);

        // 피격 순간 0.08s 동안 흰색. (§C-6)
        if (!DrawPixelBody())
            DrawShape(_flashTimer > 0f ? Colors.White : Stats.Color);
        else
        {
            DrawAttackOverlay();
            float width = Stats.Radius * 2f;
            DrawRect(new Rect2(-width / 2f, Stats.Radius + 3f, width, 2f), new Color(0f, 0f, 0f, .65f));
            DrawRect(new Rect2(-width / 2f, Stats.Radius + 3f, width * HpRatio, 2f), new Color(.85f, .28f, .25f));
        }

        // 판정 범위는 모션과 무관하게 실제 위치에 그린다 — 모션만큼 어긋나면 안 된다.
        if (DebugFlags.ShowHitbox)
        {
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawCircle(Vector2.Zero, Stats.Radius, new Color(1f, 0f, 0f, 0.25f));
        }
    }

    protected virtual void DrawAttackOverlay() { }

    private bool DrawPixelBody()
    {
        string key = this is Slime ? "slime_blob" : this is GoblinArcher ? "goblin" : this is Ironjaw ? "ironjaw" : null;
        if (key == null) return false;
        float height = this is Ironjaw ? 43f : this is Slime ? 19f : 28f;
        float pulse = State == MonsterState.Windup ? StateProgress : 0f;
        float hurt = _knockbackTimer > 0f ? _knockbackTimer / Mathf.Max(.001f, _knockbackTotal) : 0f;
        Vector2 scale = new(1f + pulse * .1f + hurt * .12f, 1f - pulse * .1f - hurt * .1f);
        Color tint = _flashTimer > 0f ? new Color(2f, 2f, 2f) : Colors.White;
        if (State == MonsterState.Dead) tint.A = Mathf.Clamp(_deathTimer / (float)CombatTuning.DeathLinger, 0f, 1f);
        return ActorArt.Draw(this, key, Facing, height, Stats.Radius, tint, scale);
    }

    /// <summary>
    /// 선딜에 뒤로 웅크렸다가 공격에 앞으로 튀어나온다.
    /// 예고 동작이 없으면 원거리·보스 공격을 피할 방법이 없다.
    /// </summary>
    private Vector2 AttackMotionOffset()
    {
        float scale = CombatTuning.MotionMonsterScale;
        if (scale <= 0f || _stateLength <= 0f)
            return Vector2.Zero;

        float t = Mathf.Clamp(1f - (StateTimer / _stateLength), 0f, 1f);
        float eased = 1f - (1f - t) * (1f - t);

        return State switch
        {
            MonsterState.Windup => -Facing * (CombatTuning.MotionWindupBack * scale * eased),
            MonsterState.Attack => Facing * (CombatTuning.MotionActiveForward * scale * eased),
            MonsterState.Recover => Facing * (CombatTuning.MotionActiveForward * scale * (1f - eased)),
            _ => Vector2.Zero,
        };
    }

    protected void SetState(MonsterState state, float duration = 0f)
    {
        State = state;
        StateTimer = duration;
        _stateLength = duration;
    }

    protected float DistanceToPlayer()
        => Player != null && IsInstanceValid(Player)
            ? GlobalPosition.DistanceTo(Player.GlobalPosition)
            : float.MaxValue;

    protected Vector2 DirectionToPlayer()
    {
        if (Player == null || !IsInstanceValid(Player))
            return Vector2.Zero;
        Vector2 delta = Player.GlobalPosition - GlobalPosition;
        return delta.LengthSquared() > 0.0001f ? delta.Normalized() : Vector2.Zero;
    }

    protected bool PlayerIsAlive => Player != null && IsInstanceValid(Player) && Player.IsAlive;

    /// <summary>훈련용 허수아비인가. 숙련 판정에서 같은 대상 감쇠를 면제받는다.</summary>
    public virtual bool IsTrainingDummy => false;

    // --- IAnimationDriver --------------------------------------------------
    // 플레이어와 같은 상태머신을 몬스터도 그대로 쓴다. 읽기 전용이라 AI 로직에 영향 없음.

    /// <summary>몬스터 상태머신을 애니메이션 상태 id 로 옮긴다.</summary>
    public string CurrentAnimationState => State switch
    {
        MonsterState.Dead => AnimationStates.Dead,
        MonsterState.Hurt => AnimationStates.Hurt,
        MonsterState.Windup => AnimationStates.AttackWindup,
        MonsterState.Attack => AnimationStates.AttackActive,
        MonsterState.Recover => AnimationStates.AttackRecovery,
        MonsterState.Chase => Velocity.Length() > 5f ? AnimationStates.Walk : AnimationStates.Idle,
        _ => AnimationStates.Idle,
    };

    /// <summary>공격 판정이 도는 구간.</summary>
    public bool AnimationHitboxActive => State == MonsterState.Attack;
    public string ArtClip => State switch
    {
        MonsterState.Windup or MonsterState.Attack or MonsterState.Recover => "attack",
        MonsterState.Chase when Velocity.Length() > 5f => "walk",
        _ => null,
    };
    public float? ArtProgress => State switch
    {
        MonsterState.Windup => Mathf.Lerp(0f,this is GoblinArcher ? 5f/9f : 4f/9f,Mathf.Min(StateProgress,.999f)),
        MonsterState.Attack => Mathf.Lerp(4f/9f,7f/9f,Mathf.Min(StateProgress,.999f)),
        MonsterState.Recover => Mathf.Lerp(this is GoblinArcher ? 5f/9f : 7f/9f,.999f,StateProgress),
        _ => null,
    };

    /// <summary>플래그·카운터는 반드시 인터페이스를 통해 건드린다. (CLAUDE.md 규칙 2)</summary>
    protected IPlayerContext Context => Player;

    /// <summary>플레이어에게 피해를 넣는다. 무적으로 흘렸으면 false.</summary>
    protected bool StrikePlayer(float amount, Vector2 direction, bool heavy = false)
    {
        if (!PlayerIsAlive || !CombatCollision.ClearSight(this, GlobalPosition, Player.GlobalPosition))
            return false;

        return Player.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Direction = direction,
            Heavy = heavy,
            SkillId = Stats.Id,
            Source = this,
        });
    }

    // --- IDamageable -------------------------------------------------------

    /// <summary>절대 죽지 않는가. 허수아비 같은 훈련용. HP 도 닳지 않는다.</summary>
    protected virtual bool Immortal => false;

    /// <summary>넉백에 밀리지 않는가. 제자리를 지켜야 하는 대상(허수아비).</summary>
    protected virtual bool Immovable => false;

    public bool TakeDamage(in DamageInfo info)
    {
        if (!IsAlive)
            return false;

        if (!Immortal)
            _hp -= info.Amount;
        _flashTimer = CombatTuning.HitFlashTime;
        QueueRedraw();

        bool killed = !Immortal && _hp <= 0f;
        if (!Immovable)
            ApplyKnockback(info.Direction, info.Heavy);

        if (killed)
        {
            Kill();
            return true;
        }

        ApplyHitstop(info.Heavy ? CombatTuning.HitstopHeavy : CombatTuning.HitstopNormal);

        // 선딜·판정 중에는 상태를 덮어쓰지 않는다. 덮어쓰면 예고 동작이 취소되고,
        // 연타로 미니보스가 영영 패턴을 못 꺼내는 상태가 된다.
        // 넉백은 상태와 무관하게 따로 흐르므로 피격 반응은 그대로 보인다.
        if (State != MonsterState.Windup && State != MonsterState.Attack)
            SetState(MonsterState.Hurt, 0f);

        return true;
    }

    public void ApplyHitstop(float seconds)
    {
        if (seconds > _hitstopTimer)
            _hitstopTimer = seconds;
    }

    /// <summary>경직. 그동안 아무것도 못 한다. (§E sk_warcry)</summary>
    public void Stun(float seconds)
    {
        if (!IsAlive || seconds <= _stunTimer)
            return;

        _stunTimer = seconds;
        Velocity = Vector2.Zero;
        SetState(MonsterState.Hurt);
    }

    public bool IsStunned => _stunTimer > 0f;

    /// <summary>
    /// ease-out 넉백. 총 이동 거리가 정확히 KnockbackDistance 가 되도록
    /// 초기 속도를 2D/T 로 잡고 선형으로 0 까지 줄인다. (§C-4)
    /// </summary>
    protected void ApplyKnockback(Vector2 direction, bool heavy)
    {
        if (direction == Vector2.Zero)
            return;

        float distance = heavy ? CombatTuning.KnockbackHeavyDistance : CombatTuning.KnockbackDistance;
        float time = heavy ? CombatTuning.KnockbackHeavyTime : CombatTuning.KnockbackTime;
        if (distance <= 0f || time <= 0f)
            return;

        _knockbackVelocity = direction.Normalized() * (2f * distance / time);
        _knockbackTotal = time;
        _knockbackTimer = time;
    }

    private void UpdateKnockback(float dt)
    {
        _knockbackTimer -= dt;
        float remaining = Mathf.Max(0f, _knockbackTimer / _knockbackTotal);
        Velocity = _knockbackVelocity * remaining;

        if (_knockbackTimer <= 0f && State == MonsterState.Hurt)
            SetState(MonsterState.Chase);
    }

    private void Kill()
    {
        SetState(MonsterState.Dead);
        Velocity = Vector2.Zero;

        // 처치 히트스톱 동안 멈춰 있다가 터진다. 이 정지가 처치의 손맛이다. (§C-3)
        // 시체는 히트스톱보다 조금 더 남는다 — CombatTuning.DeathLinger 주석 참고.
        _deathTimer = Mathf.Max(CombatTuning.HitstopKill, CombatTuning.DeathLinger);
        _hitstopTimer = 0f;

        // 시체는 사라질 때까지 흰색으로 굳어 있는다 — 살아 있는 것과 헷갈리지 않게.
        _flashTimer = _deathTimer;
        QueueRedraw();

        // 그룹에서 바로 빼지 않는다 — 죽는 0.16s 동안 시체를 때리는 것이
        // 숙련 매크로 방어의 'deadTarget' 경로로 실제로 잡혀야 하기 때문. (§F, §K)
        DebugLog.Add($"{Stats.Id} 처치");
        OnKilled();
    }

    /// <summary>개별 몬스터가 처치 시점에 할 일 (플래그 기록 등).</summary>
    protected virtual void OnKilled() { }

    private void UpdateDeath(float dt)
    {
        Velocity = Vector2.Zero;
        MoveAndSlide();

        _deathTimer -= dt;
        if (_deathTimer > 0f || _burstSpawned)
            return;

        _burstSpawned = true;
        CombatFeedback.Instance?.DeathBurstAt(GlobalPosition, Stats.Color);
        Died?.Invoke(this);
        QueueFree();
    }
}
