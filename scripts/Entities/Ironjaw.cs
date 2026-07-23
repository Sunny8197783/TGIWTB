using Godot;
using PixelMmo.Balance;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// 철턱 — 큰 빨간 사각형. 미니보스. (§D-2)
/// 패턴 3개: 물어뜯기 / 돌진 / 포효(HP 40% 이하 1회, 이후 공격 속도 25% 증가).
/// 처치·사망 플래그는 P2 의 job_warrior R10 관문과 job_ashen_revenant 히든 조건이 그대로 읽는다.
/// </summary>
public partial class Ironjaw : MonsterBase
{
    private enum Pattern
    {
        None,
        Bite,
        Charge,
        Roar,
    }

    public override MonsterStats Stats => MonsterTuning.Ironjaw;

    private Pattern _pattern = Pattern.None;
    private float _cooldown;
    private bool _roared;
    private bool _enraged;
    private Vector2 _chargeDirection;
    private bool _chargeHitLanded;

    /// <summary>포효 이후 선딜·후딜·쿨다운이 이 배율만큼 짧아진다.</summary>
    private float SpeedScale => _enraged ? MonsterTuning.IronjawAttack.EnragedSpeedScale : 1f;

    protected override void UpdateAi(float delta)
    {
        if (_cooldown > 0f)
            _cooldown -= delta;

        switch (State)
        {
            case MonsterState.Windup:
                UpdateWindup();
                return;
            case MonsterState.Attack:
                UpdateAttack(delta);
                return;
            case MonsterState.Recover:
                Velocity = Vector2.Zero;
                if (StateTimer <= 0f)
                {
                    _pattern = Pattern.None;
                    SetState(MonsterState.Chase);
                }
                return;
        }

        if (!PlayerIsAlive)
        {
            Velocity = Vector2.Zero;
            SetState(MonsterState.Idle);
            return;
        }

        // 패턴 3 — HP 40% 이하에서 한 번만. (§D-2)
        // _roared 는 포효가 실제로 끝났을 때 세운다. 여기서 세우면 중간에 끊겼을 때
        // 한 번도 못 울고 소진된다.
        if (!_roared && HpRatio <= MonsterTuning.IronjawAttack.RoarHpRatio)
        {
            _pattern = Pattern.Roar;
            Velocity = Vector2.Zero;
            SetState(MonsterState.Windup, MonsterTuning.IronjawAttack.RoarDuration);
            return;
        }

        float distance = DistanceToPlayer();
        if (distance > Stats.AggroRange)
        {
            Velocity = Vector2.Zero;
            SetState(MonsterState.Idle);
            return;
        }

        Vector2 toPlayer = DirectionToPlayer();
        if (toPlayer != Vector2.Zero)
            Facing = toPlayer;

        SetState(MonsterState.Chase);
        Velocity = toPlayer * Stats.MoveSpeed;

        if (_cooldown > 0f)
            return;

        // 가까우면 물어뜯고, 멀면 돌진한다.
        if (distance <= MonsterTuning.IronjawAttack.BiteDecisionRange)
            BeginPattern(Pattern.Bite, MonsterTuning.IronjawAttack.BiteWindup);
        else
            BeginPattern(Pattern.Charge, MonsterTuning.IronjawAttack.ChargeWindup);
    }

    private void BeginPattern(Pattern pattern, float windup)
    {
        _pattern = pattern;
        _cooldown = MonsterTuning.IronjawAttack.PatternCooldown * SpeedScale;
        Velocity = Vector2.Zero;
        SetState(MonsterState.Windup, windup * SpeedScale);
    }

    private void UpdateWindup()
    {
        Velocity = Vector2.Zero;

        // 선딜 동안에는 방향을 계속 조정한다 — 그래서 회피가 의미 있어진다.
        if (_pattern != Pattern.Roar && PlayerIsAlive)
        {
            Vector2 toPlayer = DirectionToPlayer();
            if (toPlayer != Vector2.Zero)
                Facing = toPlayer;
        }

        if (StateTimer > 0f)
            return;

        switch (_pattern)
        {
            case Pattern.Bite:
                Bite();
                break;

            case Pattern.Charge:
                _chargeDirection = Facing;
                _chargeHitLanded = false;
                SetState(MonsterState.Attack, MonsterTuning.IronjawAttack.ChargeDuration);
                break;

            case Pattern.Roar:
                _roared = true;
                _enraged = true;
                _cooldown = 0f;
                _pattern = Pattern.None;
                SetState(MonsterState.Recover, MonsterTuning.IronjawAttack.BiteRecover);
                DebugLog.Add("철턱이 포효했다 — 공격 속도 증가");
                CombatFeedback.Instance?.Announce("굴이 울렸다.");
                break;
        }
    }

    private void UpdateAttack(float delta)
    {
        if (_pattern != Pattern.Charge)
        {
            SetState(MonsterState.Recover, MonsterTuning.IronjawAttack.BiteRecover * SpeedScale);
            return;
        }

        Velocity = _chargeDirection * MonsterTuning.IronjawAttack.ChargeSpeed;

        // 돌진 1회당 한 번만 들이받는다.
        if (!_chargeHitLanded && PlayerIsAlive
            && DistanceToPlayer() <= Stats.Radius + PlayerTuning.BodySize * 0.5f)
        {
            _chargeHitLanded = true;
            StrikeAndCount(MonsterTuning.IronjawAttack.ChargeDamage, _chargeDirection, heavy: true);
        }

        if (StateTimer <= 0f)
            SetState(MonsterState.Recover, MonsterTuning.IronjawAttack.ChargeRecover * SpeedScale);
    }

    private void Bite()
    {
        SetState(MonsterState.Recover, MonsterTuning.IronjawAttack.BiteRecover * SpeedScale);

        if (!PlayerIsAlive)
            return;

        bool inCone = Hitbox.InCone(GlobalPosition, Facing,
            MonsterTuning.IronjawAttack.BiteAngleDeg, MonsterTuning.IronjawAttack.BiteRange,
            Player.GlobalPosition, PlayerTuning.BodySize * 0.5f);

        if (inCone)
            StrikeAndCount(MonsterTuning.IronjawAttack.BiteDamage, DirectionToPlayer(), heavy: true);
    }

    /// <summary>
    /// 피해를 넣고, 그 일격으로 플레이어가 죽었으면 카운터를 올린다.
    /// 7회에 도달하면 플래그를 세운다 — job_ashen_revenant 의 히든 조건. (§D-2)
    /// </summary>
    private void StrikeAndCount(float damage, Vector2 direction, bool heavy)
    {
        if (!StrikePlayer(damage, direction, heavy))
            return;

        if (Context == null || Player.IsAlive)
            return;

        int deaths = Context.AddCounter(MonsterTuning.IronjawAttack.CounterDeaths);
        DebugLog.Add($"{MonsterTuning.IronjawAttack.CounterDeaths} = {deaths}");

        if (deaths >= MonsterTuning.IronjawAttack.DeathsForFlag)
            Context.SetFlag(MonsterTuning.IronjawAttack.FlagDeathsReached);
    }

    /// <summary>처치 시 플래그. P2 의 job_warrior R10 관문이 이걸 읽는다. (§D-2)</summary>
    protected override void OnKilled()
    {
        Context?.SetFlag(MonsterTuning.IronjawAttack.FlagDefeated);
        CombatFeedback.Instance?.Announce("굴이 조용해졌다.");
    }

    /// <summary>큰 빨간 사각형. (§A 아트 방침)</summary>
    protected override void DrawShape(Color color)
    {
        float r = Stats.Radius;
        Color body = _enraged ? color.Lightened(0.2f) : color;
        DrawRect(new Rect2(-r, -r, r * 2f, r * 2f), body);

        // 선딜을 크게 알린다. 미니보스는 읽을 수 있어야 한다.
        if (State == MonsterState.Windup)
        {
            float range = _pattern == Pattern.Charge
                ? MonsterTuning.IronjawAttack.ChargeSpeed * MonsterTuning.IronjawAttack.ChargeDuration
                : MonsterTuning.IronjawAttack.BiteRange;

            DrawLine(Vector2.Zero, Facing * range, new Color(1f, 0.5f, 0.3f, 0.55f), 2f);
            DrawArc(Vector2.Zero, r + 4f, 0f, Mathf.Tau, 24, new Color(1f, 0.7f, 0.4f, 0.9f), 1.5f);
        }

        float width = r * 2f;
        float y = r + 4f;
        DrawRect(new Rect2(-r, y, width, 2f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-r, y, width * HpRatio, 2f), new Color(0.95f, 0.3f, 0.3f));
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        QueueRedraw();
    }
}
