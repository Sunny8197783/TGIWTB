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
    /// <summary>가로로 긴 육각형. 네모보다 짐승 몸통에 가깝고 꼭짓점이 여섯뿐이다.</summary>
    private static Vector2[] Hex(Vector2 at, float w, float h) => new[]
    {
        at + new Vector2(-w, 0f),
        at + new Vector2(-w * 0.55f, -h),
        at + new Vector2(w * 0.55f, -h),
        at + new Vector2(w, 0f),
        at + new Vector2(w * 0.55f, h),
        at + new Vector2(-w * 0.55f, h),
    };

    protected override void DrawShape(Color color)
    {
        float r = Stats.Radius;
        Color body = _enraged ? color.Lightened(0.2f) : color;

        // 사각형이었다. 도형이어도 상관없지만(규칙 5) '빨간 네모'는 몬스터가 아니라
        // 디버그 표시로 읽힌다 — 새 그림 없이 실루엣만 짐승으로 만든다.
        // 몸통(가로로 긴 육각) + 앞다리 + 머리 + 턱. 광원은 좌상단.
        float bx = r * 0.95f, by = r * 0.62f;

        DrawColoredPolygon(Hex(new Vector2(0f, r * 0.05f), bx + OutlineWidth, by + OutlineWidth),
            OutlineColor);
        DrawColoredPolygon(Hex(new Vector2(0f, r * 0.05f), bx, by), body);

        // 등의 밝은 면과 배의 그늘.
        DrawColoredPolygon(Hex(new Vector2(0f, -r * 0.16f), bx * 0.82f, by * 0.42f), Lit(body));
        DrawColoredPolygon(Hex(new Vector2(0f, r * 0.44f), bx * 0.74f, by * 0.30f), Dark(body));

        // 네 다리 — 몸통 아래로 짧게. 방향과 무관하게 자세가 잡혀 보이면 충분하다.
        var paw = Dark(body);
        for (int i = 0; i < 4; i++)
        {
            float px = (i < 2 ? -1f : 1f) * bx * (i % 2 == 0 ? 0.62f : 0.30f);
            DrawRect(new Rect2(px - r * 0.13f, by * 0.55f, r * 0.26f, r * 0.42f), paw);
        }

        // 머리는 보는 방향으로 붙는다. 어느 쪽을 노리는지가 실루엣으로 읽혀야 한다.
        Vector2 head = Facing * r * 0.82f;
        Vector2 side = new(-Facing.Y, Facing.X);

        DrawCircle(head, r * 0.46f + OutlineWidth, OutlineColor);
        DrawCircle(head, r * 0.46f, body);
        DrawCircle(head - Facing * r * 0.10f - side * r * 0.10f, r * 0.22f, Lit(body));

        // 귀 두 짝. 실루엣에 뾰족한 데가 있어야 공 하나로 안 보인다.
        // 머리 옆에서 뒤쪽(몸통 쪽)으로 눕힌 삼각형 — 짐승 귀의 각도다.
        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 baseAt = head + side * r * 0.30f * i;
            DrawColoredPolygon(new[]
            {
                baseAt - Facing * r * 0.16f,
                baseAt + Facing * r * 0.08f,
                baseAt + side * r * 0.30f * i - Facing * r * 0.20f,
            }, Dark(body));
        }

        // 철턱 — 이름이 턱이다. 아래턱을 하나 물리고 이빨을 박는다.
        Vector2 jaw = head + Facing * r * 0.26f;
        DrawCircle(jaw, r * 0.26f, Dark(body));

        var fang = new Color(0.95f, 0.93f, 0.88f);
        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 root = jaw + side * r * 0.15f * i;
            DrawColoredPolygon(new[]
            {
                root - side * r * 0.06f * i,
                root + side * r * 0.06f * i,
                root + Facing * r * 0.20f,
            }, fang);
        }

        // 눈 — 이것 하나로 '앞'이 확실해진다.
        var eye = new Color(1f, 0.86f, 0.42f);
        for (int i = -1; i <= 1; i += 2)
            DrawCircle(head + side * r * 0.18f * i - Facing * r * 0.06f, r * 0.07f, eye);

        DrawAttackOverlay();

        float width = r * 2f;
        float y = r + 4f;
        DrawRect(new Rect2(-r, y, width, 2f), new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(-r, y, width * HpRatio, 2f), new Color(0.95f, 0.3f, 0.3f));
    }

    protected override void DrawAttackOverlay()
    {
        float r = Stats.Radius;
        // 선딜을 크게 알린다. 미니보스는 읽을 수 있어야 한다.
        if (State == MonsterState.Windup)
        {
            float range = _pattern == Pattern.Charge
                ? MonsterTuning.IronjawAttack.ChargeSpeed * MonsterTuning.IronjawAttack.ChargeDuration
                : MonsterTuning.IronjawAttack.BiteRange;

            DrawLine(Vector2.Zero, Facing * range, new Color(1f, 0.5f, 0.3f, 0.55f), 2f);
            DrawArc(Vector2.Zero, r + 4f, 0f, Mathf.Tau, 24, new Color(1f, 0.7f, 0.4f, 0.9f), 1.5f);
        }

    }

}
