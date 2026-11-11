using System.Collections.Generic;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 몬스터 하나. 정의는 data/monsters/*.json. 세계 시간(GameRoot.WorldScale)으로 움직인다 —
/// 완벽 회피 슬로우 때 느려지고, 히트스톱 때 멈춘다.
///
/// 살아 있는 것처럼 보이게:
///  - 나타날 때 흙먼지와 함께 서서히 드러난다 (Appear). 집 둘레를 걷다 멈춰 서서 두리번거린다 (Idle).
///  - 주인공을 보면 멈칫하며 '!' (Alert) → 종마다 다르게 다가온다: 붙기(chase)·거리 두기(kite)·둘레 돌기(circle).
///  - 몸은 한 번에 돌지 않고 초당 일정 각도로 돈다. 너무 멀리 끌려오면 집으로 돌아가며 기운을 되찾는다 (Return).
/// 공격 흐름: 예고(주황 깜빡임·바닥 표시, 읽을 시간) → 판정 → 회복(빈틈). 모양은 MonsterDef.AttackDef.Shape.
/// </summary>
public partial class Enemy : CharacterBody3D
{
    public static readonly List<Enemy> All = new();

    private enum State { Appear, Idle, Alert, Chase, Windup, Active, Recover, Hurt, Stagger, Guard, Return, Dead }

    public MonsterDef Def { get; }
    public int Level { get; }
    /// <summary>이름을 잃은 것: 잿빛, 더 질기고 세다. 쓰러뜨리면 이름 조각을 떨군다</summary>
    public bool Hushed { get; }
    /// <summary>스포너가 주인공 둘레에 채운 것 (멀어지면 치운다)</summary>
    public bool Ambient { get; set; }
    /// <summary>연출 중: 생각도 움직임도 멈춘다</summary>
    public bool Frozen { get; set; }
    public bool Invulnerable { get; set; }
    public string DisplayName => Hushed ? "무명 " + Def.Name : Def.Name;
    public bool Alive => _state != State.Dead;
    public float Radius => Def.Radius;
    public bool Staggered => _state == State.Stagger;
    /// <summary>지금 공격 중이거나 예고 중 — 막기를 누르면 이쪽으로 돈다.</summary>
    public bool Threatening => _state is State.Windup or State.Active;
    /// <summary>주인공과 싸우는 중 (이름표를 띄운다, 스포너가 치우지 않는다)</summary>
    public bool Engaged => _state is State.Alert or State.Chase or State.Windup or State.Active or State.Recover
        or State.Hurt or State.Stagger or State.Guard;
    public float MaxHp { get; private set; }
    public float HpRatio => Mathf.Clamp(_hp / MaxHp, 0f, 1f);
    public int ExpValue { get; private set; }
    /// <summary>주인공에게서 멀면 잔다 (AI·물리·애니메이션 모두 멈춤). 보스는 자지 않는다</summary>
    private bool Asleep => !Def.Boss && Hero.Instance != null
        && GlobalPosition.DistanceSquaredTo(Hero.Instance.GlobalPosition) > T.MonsterSleepRange * T.MonsterSleepRange;
    /// <summary>마지막으로 맞은 시각 (실제 초). HUD 가 맞은 적만 체력바를 띄운다.</summary>
    public double LastHitAt { get; private set; } = -99;
    /// <summary>공격 예고가 끝나기까지 남은 시간 (예고 중이 아니면 무한대). 개발 캡처가 반응 타이밍을 잡을 때 쓴다.</summary>
    public float WindupLeft => _state == State.Windup ? _attack.Windup - _stateTime : float.PositiveInfinity;

    [Signal]
    public delegate void DiedEventHandler();

    private readonly Vector3 _home;
    private CharacterSprite _sprite;
    private State _state = State.Appear;
    private float _stateTime;
    private float _hp;
    private float _damageScale = 1f;
    private float _poiseDamage;
    private float _cooldown;
    private float _guardCooldown;
    private Vector3 _vel;
    private Vector3 _facing = Vector3.Back;
    private bool _attackResolved;
    private Vector3 _wanderTarget;
    private float _pause;               // 서서 쉬는 시간 (어슬렁 사이)
    private float _strafeSign = 1f;
    private float _strafeLeft;
    private float _circleLeft;          // 둘레를 도는 남은 시간 (circle)
    private Vector3 _aim;               // circle 공격이 떨어질 자리 (예고 중간에 굳는다)
    private bool _alerted;
    private float _flash;
    private Color _flashColor = Colors.White;
    private MonsterDef.AttackDef _attack;
    private MeshInstance3D _warn;
    private ShaderMaterial _warnMat;
    private static readonly RandomNumberGenerator Rng = new();

    public Enemy(MonsterDef def, Vector3 home, int level = 0, bool hushed = false)
    {
        Def = def;
        _home = home;
        Level = level > 0 ? level : def.Level;
        Hushed = hushed;
        _attack = def.Attack;
    }

    public Enemy() { }

    public override void _Ready()
    {
        Name = Def.Id;
        int up = Mathf.Max(0, Level - Def.Level);
        MaxHp = Def.Hp * (1f + T.MonsterHpPerLevel * up) * (Hushed ? T.HushedHp : 1f);
        _damageScale = (1f + T.MonsterDamagePerLevel * up) * (Hushed ? T.HushedDamage : 1f);
        ExpValue = Mathf.RoundToInt(Def.Exp * (1f + T.MonsterExpPerLevel * up) * (Hushed ? T.HushedExp : 1f));
        _hp = MaxHp;
        // 적끼리·주인공과는 몸으로 부딪히지 않는다 (돌진으로 빠져나가야 하니까). 땅·건물만.
        CollisionLayer = 1 << 2;
        CollisionMask = 1;
        MotionMode = MotionModeEnum.Floating;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Def.Radius, Height = Mathf.Max(Def.Radius * 2f, 1f) },
            Position = new Vector3(0f, Mathf.Max(Def.Radius, 0.5f), 0f),
        });
        _sprite = new CharacterSprite(Def.Art, null, Def.Cell) { ShadowHeight = Def.Radius * 1.8f, ShadowRadius = Def.Radius * 0.7f };
        AddChild(_sprite);
        if (Hushed)
            _sprite.SetHushed(1f);
        _wanderTarget = _home;
        _pause = Rng.RandfRange(0.2f, 1.5f);
        float a = Rng.Randf() * Mathf.Tau;
        _facing = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        All.Add(this);
        // 나타남: 하얗게 드러났다가 제 색으로
        SetFlash(1f, Hushed ? new Color(0.85f, 0.88f, 0.95f) : Colors.White);
    }

    public override void _ExitTree() => All.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.WorldScale;
        if (dt <= 0f || Asleep || Frozen)
            return;
        _stateTime += dt;
        _cooldown -= dt;
        _guardCooldown -= dt;
        var hero = Hero.Instance;
        bool heroUp = hero != null && hero.IsAlive && !hero.Hidden;
        Vector3 toHero = hero != null ? Flat(hero.GlobalPosition - GlobalPosition) : Vector3.Zero;
        float dist = toHero.Length();
        Vector3 heroDir = dist > 0.01f ? toHero / dist : _facing;
        var a = _attack;

        switch (_state)
        {
            case State.Appear:
                _vel = Vector3.Zero;
                if (_stateTime >= T.MonsterAppearTime)
                    Enter(State.Idle);
                break;

            case State.Idle:
                if (heroUp && dist < Def.Sight)
                {
                    Enter(_alerted ? State.Chase : State.Alert);
                    break;
                }
                Wander(dt);
                break;

            case State.Alert:
                // 멈칫: 주인공을 보고 굳었다가 달려든다
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                TurnToward(heroDir, dt, 720f);
                if (_stateTime >= T.MonsterAlertTime)
                    Enter(State.Chase);
                break;

            case State.Chase:
                if (!heroUp || dist > Def.Sight * T.MonsterLoseSight)
                {
                    Enter(State.Return);
                    break;
                }
                if (!Def.Boss && Flat(GlobalPosition - _home).Length() > Def.Sight * T.MonsterLeash)
                {
                    Enter(State.Return);
                    break;
                }
                Pursue(heroDir, dist, dt);
                if (_cooldown <= 0f && (Def.Move != "circle" || _circleLeft <= 0f))
                {
                    var pick = PickAttack(dist);
                    if (pick != null)
                    {
                        _attack = pick;
                        Enter(State.Windup);
                    }
                }
                break;

            case State.Windup:
                // 예고 앞부분엔 주인공을 따라 돌고, 뒷부분엔 방향이 고정된다 — 끝에 피하면 피해진다
                if (_stateTime < a.Windup * T.MonsterAimLock)
                {
                    TurnToward(heroDir, dt, 420f);
                    if (hero != null)
                        _aim = AimPoint(hero, a);
                }
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                // 주황 깜빡임이 점점 빨라진다
                float k = _stateTime / a.Windup;
                SetFlash(Mathf.Sin(k * k * 30f) > 0f ? 0.55f * k : 0f, new Color(1f, 0.55f, 0.15f));
                UpdateWarning(a, Mathf.Clamp(k, 0f, 1f));
                if (_stateTime >= a.Windup)
                    Enter(State.Active);
                break;

            case State.Active:
                switch (a.Shape)
                {
                    case "charge":
                        _vel = _facing * a.Lunge;
                        if (!_attackResolved && hero != null)
                            TryRam(hero, dist);
                        break;
                    case "circle":
                    case "nova":
                    case "shot":
                        _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                        break;
                    default:
                        _vel = _facing * a.Lunge * (1f - _stateTime / a.Active * 0.5f);
                        if (!_attackResolved && hero != null)
                            TryHitHero(hero);
                        break;
                }
                if (_stateTime >= a.Active)
                    Enter(State.Recover);
                break;

            case State.Recover:
                _vel = _vel.MoveToward(Vector3.Zero, 18f * dt);
                if (_stateTime >= a.Recover)
                {
                    _cooldown = a.Cooldown * Rng.RandfRange(0.85f, 1.2f);
                    if (Def.Blink && heroUp)
                        BlinkAway(hero);
                    _circleLeft = Rng.RandfRange(1.0f, 2.2f);
                    Enter(State.Chase);
                }
                break;

            case State.Hurt:
                _vel = _vel.MoveToward(Vector3.Zero, 14f * dt);
                if (_stateTime >= 0.28f)
                    Enter(State.Chase);
                break;

            case State.Stagger:
                _vel = _vel.MoveToward(Vector3.Zero, 10f * dt);
                SetFlash(Mathf.Sin(_stateTime * 18f) > 0.6f ? 0.35f : 0f, new Color(1f, 0.95f, 0.5f));
                if (_stateTime >= T.StaggerTime)
                    Enter(State.Chase);
                break;

            case State.Guard:
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                if (_stateTime >= Def.Guard.Time)
                {
                    _guardCooldown = T.MonsterGuardCooldown;
                    Enter(State.Chase);
                }
                break;

            case State.Return:
                // 집으로: 기운을 되찾으며 걸어 돌아간다. 가까이서 건드리면 다시 덤빈다
                Vector3 toHome = Flat(_home - GlobalPosition);
                _hp = Mathf.Min(MaxHp, _hp + MaxHp * T.MonsterReturnRegen * dt);
                Steer(toHome, Def.Speed * 1.2f, dt);
                if (toHome.Length() < 1.2f)
                {
                    _alerted = false;
                    Enter(State.Idle);
                }
                else if (heroUp && dist < Def.Sight * 0.6f && Flat(GlobalPosition - _home).Length() < Def.Sight)
                    Enter(State.Chase);
                break;

            case State.Dead:
                _vel = _vel.MoveToward(Vector3.Zero, 10f * dt);
                break;
        }

        Hero.StepOnGround(this, ref _vel, dt, GameRoot.Instance.WorldScale);
    }

    public override void _Process(double delta)
    {
        if (Asleep)
            return;
        float dt = Frozen ? 0f : (float)delta * GameRoot.Instance.WorldScale;
        var world = GameRoot.Instance.World;
        float speed = _vel.Length();
        switch (_state)
        {
            case State.Idle or State.Chase or State.Return or State.Alert:
                if (speed > 0.3f && _sprite.Has("move"))
                    // 걸음이 느리면 발도 느리게 — 미끄러지지 않게
                    _sprite.Play("move", Mathf.Clamp(8f * speed / Mathf.Max(Def.Speed, 0.1f), 4f, 14f), true);
                else
                    _sprite.Play(_sprite.Has("idle") ? "idle" : "rot", 5f, true);
                break;
            case State.Active when _attack.Shape == "charge" && _sprite.Has("move"):
                _sprite.Play("move", 16f, true);
                break;
        }
        _sprite.Dir = DirIndex(_facing);
        if (_state == State.Appear)
            SetFlash(Mathf.Clamp(1f - _stateTime / T.MonsterAppearTime, 0f, 1f), _flashColor);
        else if (_state is not State.Windup and not State.Stagger)
        {
            _flash = Mathf.MoveToward(_flash, 0f, (float)delta * 8f);
            _sprite.SetFlash(_flash, _flashColor);
        }
        _sprite.Advance(dt);
        Vector3 feet = GlobalPosition;
        _sprite.PlaceAt(feet, world.WalkHeightAt(feet.X, feet.Z));
    }

    // ── 움직임 ──────────────────────────────────────────

    /// <summary>집 둘레를 어슬렁거린다: 걷다가 멈춰 서서 두리번거리고, 다시 걷는다</summary>
    private void Wander(float dt)
    {
        if (_pause > 0f)
        {
            _pause -= dt;
            _vel = _vel.MoveToward(Vector3.Zero, 12f * dt);
            // 가끔 고개를 돌린다
            if (Rng.Randf() < dt * 0.5f)
            {
                float ang = Rng.Randf() * Mathf.Tau;
                _wanderLook = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            }
            TurnToward(_wanderLook, dt, 160f);
            if (_pause <= 0f)
            {
                float ang = Rng.Randf() * Mathf.Tau;
                _wanderTarget = _home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Rng.RandfRange(1f, 4.5f);
                _stateTime = 0f;
            }
            return;
        }
        Vector3 to = Flat(_wanderTarget - GlobalPosition);
        if (to.Length() < 0.4f || _stateTime > 5f)
        {
            _pause = Rng.RandfRange(1.2f, 4f);
            _wanderLook = _facing;
            return;
        }
        Steer(to, Def.Speed * 0.38f, dt);
    }

    private Vector3 _wanderLook = Vector3.Back;

    private void Pursue(Vector3 heroDir, float dist, float dt)
    {
        switch (Def.Move)
        {
            case "kite":
            {
                // 거리를 지킨다: 가까우면 물러나고, 멀면 다가가고, 알맞으면 옆으로 흐른다
                TickStrafe(dt);
                Vector3 side = Vector3.Up.Cross(heroDir) * _strafeSign;
                Vector3 dir = dist < Def.PreferRange * 0.65f ? -heroDir + side * 0.4f
                    : dist > Def.PreferRange * 1.25f ? heroDir
                    : side;
                Steer(dir, Def.Speed * (dist < Def.PreferRange * 0.65f ? 1f : 0.7f), dt, face: false);
                TurnToward(heroDir, dt, 420f);
                break;
            }
            case "circle":
            {
                // 둘레를 돌며 틈을 본다. 도는 시간이 끝나면 공격을 고른다
                TickStrafe(dt);
                _circleLeft -= dt;
                Vector3 side = Vector3.Up.Cross(heroDir) * _strafeSign;
                float pull = Mathf.Clamp((dist - Def.PreferRange) / 2f, -1f, 1f);
                Steer(side + heroDir * pull, Def.Speed, dt, face: false);
                TurnToward(dist > Def.PreferRange * 1.4f ? heroDir : (side + heroDir * 0.6f), dt, 420f);
                break;
            }
            default:
                // 사거리 바로 앞까지만 붙는다
                float reach = Def.Attacks.Count > 0 ? Def.Attacks[0].PickMax : 1.5f;
                Steer(dist > reach * 0.8f ? heroDir : Vector3.Zero, Def.Speed, dt, face: false);
                TurnToward(heroDir, dt, 540f);
                break;
        }
    }

    private void TickStrafe(float dt)
    {
        _strafeLeft -= dt;
        if (_strafeLeft > 0f)
            return;
        _strafeLeft = Rng.RandfRange(1.4f, 3.2f);
        _strafeSign = Rng.Randf() < 0.5f ? -1f : 1f;
    }

    /// <summary>지금 거리에서 쓸 수 있는 공격을 무게대로 고른다 (없으면 null)</summary>
    private MonsterDef.AttackDef PickAttack(float dist)
    {
        float total = 0f;
        foreach (var a in Def.Attacks)
            if (Usable(a, dist))
                total += a.Weight;
        if (total <= 0f)
            return null;
        float roll = Rng.Randf() * total;
        foreach (var a in Def.Attacks)
        {
            if (!Usable(a, dist))
                continue;
            roll -= a.Weight;
            if (roll <= 0f)
                return a;
        }
        return null;
    }

    private bool Usable(MonsterDef.AttackDef a, float dist) =>
        dist >= a.MinRange && dist <= a.PickMax * 0.95f && HpRatio <= a.BelowHp;

    /// <summary>자리 공격(circle)이 떨어질 곳: 주인공 발밑 (Reach 보다 멀면 그 거리까지만). nova 는 제자리</summary>
    private Vector3 AimPoint(Hero hero, MonsterDef.AttackDef a)
    {
        if (a.Shape != "circle")
            return GlobalPosition;
        Vector3 to = Flat(hero.GlobalPosition - GlobalPosition);
        Vector3 p = GlobalPosition + to.LimitLength(Mathf.Max(a.Reach, 0.1f));
        p.Y = GameRoot.Instance.World.HeightAt(p.X, p.Z);
        return p;
    }

    private void BlinkAway(Hero hero)
    {
        var world = GameRoot.Instance.World;
        var pal = CombatFx.PaletteOf("ember");
        for (int tries = 0; tries < 8; tries++)
        {
            float ang = Rng.Randf() * Mathf.Tau;
            Vector3 p = hero.GlobalPosition + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Def.PreferRange;
            if (world.WaterAt(p.X, p.Z) - world.HeightAt(p.X, p.Z) > T.WadeDepth)
                continue;
            if (Mathf.Abs(world.HeightAt(p.X, p.Z) - GlobalPosition.Y) > 4f)
                continue;
            CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.6f, 14, 0.7f, pal);
            Dust.Puff(GlobalPosition, 6, Vector3.Zero);
            p.Y = world.WalkHeightAt(p.X, p.Z);
            GlobalPosition = p;
            _vel = Vector3.Zero;
            CombatFx.SparkleBurst(p + Vector3.Up * 0.6f, 14, 0.7f, pal);
            Sfx.Play("dash", -8f, 1.5f);
            SetFlash(1f, pal.Bright);
            return;
        }
    }

    // ── 맞기 ────────────────────────────────────────────

    /// <summary>주인공의 칼에 맞았다. 죽었으면 true.</summary>
    public bool TakeHit(float damage, Vector3 dir, bool heavy)
    {
        if (!Alive || Invulnerable)
            return false;
        Vector3 head = GlobalPosition + Vector3.Up * Def.HeadHeight * 0.7f;
        if (_state == State.Guard)
        {
            if (heavy)
            {
                // 강타에 껍데기가 깨진다 → 무방비
                Hud.Float(head, "깨뜨림!", new Color(1f, 0.9f, 0.45f));
                Stagger(dir);
            }
            else
            {
                damage *= Def.Guard.Reduce;
                CombatFx.Sparks(GlobalPosition + Vector3.Up * 0.5f - dir * Def.Radius * 0.5f, -dir, CombatFx.Spark.Block);
                Sfx.Play("impactMetal_light", -4f, 0.8f);
            }
        }
        if (_state == State.Stagger)
            damage *= T.StaggerDamage;
        _hp -= damage;
        _poiseDamage += damage;
        LastHitAt = Time.GetTicksMsec() / 1000.0;
        Hud.Damage(head, damage, heavy);
        _flash = 1f;
        _flashColor = Colors.White;
        _vel = Flat(dir).Normalized() * (heavy ? 7f : 3.5f) * Def.Knockback;
        Sfx.Play(Def.Sounds.Hit, -3f, heavy ? 0.8f : 1f);
        if (_hp <= 0f)
        {
            Die(dir);
            return true;
        }
        _alerted = true;
        if (_state is State.Idle or State.Return or State.Appear or State.Alert)
            Enter(State.Chase);   // 멀리서 맞아도 덤벼든다
        if (_state is State.Stagger or State.Guard)
            return false;
        // 가끔 껍데기에 숨는다 (바위 게)
        if (Def.Guard != null && _guardCooldown <= 0f && _state is State.Chase or State.Hurt && Rng.Randf() < Def.Guard.Chance)
        {
            Enter(State.Guard);
            return false;
        }
        // 강타이거나 버팀(poise)을 넘으면 움찔 — 공격 중이어도 끊긴다. 단단한 몸은 버팀을 넘어야만
        bool flinch = Def.SuperArmor ? _poiseDamage >= Def.Poise : heavy || _poiseDamage >= Def.Poise;
        if (flinch)
        {
            _poiseDamage = 0f;
            Enter(State.Hurt);
        }
        return false;
    }

    /// <summary>패링당했다 — 무방비로 굳는다.</summary>
    public void Stagger(Vector3 pushDir)
    {
        if (!Alive)
            return;
        _vel = Flat(pushDir).Normalized() * 5f * Def.Knockback;
        Enter(State.Stagger);
    }

    /// <summary>막혔다 — 살짝 튕겨 나간다.</summary>
    public void Recoil(Vector3 pushDir)
    {
        _vel = Flat(pushDir).Normalized() * 3f * Def.Knockback;
    }

    private float Damage => _attack.Damage * _damageScale;

    /// <summary>부채꼴 공격: 사거리·정면 안에 있으면 맞는다</summary>
    private void TryHitHero(Hero hero)
    {
        Vector3 to = Flat(hero.GlobalPosition - GlobalPosition);
        float d = to.Length();
        var a = _attack;
        // 공격이 거의 닿을 뻔한 거리에서 막 돌진했다 → 완벽 회피
        if (d <= a.Reach * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
        {
            _attackResolved = true;
            return;
        }
        if (d > a.Reach || (d > 0.3f && _facing.Dot(to / d) < 0.2f))
            return;
        _attackResolved = true;
        Resolve(hero.ReceiveHit(GlobalPosition, Damage, _facing));
    }

    /// <summary>돌진: 달리는 길에 몸이 닿으면 맞는다</summary>
    private void TryRam(Hero hero, float dist)
    {
        float hit = Def.Radius + T.MonsterRamReach + _attack.Reach;
        if (dist <= hit * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
        {
            _attackResolved = true;
            return;
        }
        if (dist > hit)
            return;
        _attackResolved = true;
        Resolve(hero.ReceiveHit(GlobalPosition, Damage, _facing));
    }

    /// <summary>자리 공격: 예고한 원 안에 있으면 맞는다. 맞든 안 맞든 땅은 울린다</summary>
    private void Slam(Hero hero)
    {
        var a = _attack;
        Vector3 c = _aim;
        var pal = CombatFx.PaletteOf(Hushed || Def.Boss ? "hush" : "ember");
        CombatFx.Shockwave(c, a.Radius * 1.15f, 0.45f, pal);
        Dust.Puff(c, 14, Vector3.Zero);
        Sfx.Play("impactPlate_heavy", -3f, 0.6f);
        if (hero == null)
            return;
        Vector3 to = Flat(hero.GlobalPosition - c);
        float d = to.Length();
        CombatFx.Shake(Mathf.Clamp(0.5f - d * 0.04f, 0.08f, 0.45f));
        if (d <= a.Radius * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
            return;
        if (d > a.Radius)
            return;
        Resolve(hero.ReceiveHit(c, Damage, d > 0.1f ? to / d : _facing));
    }

    private void Resolve(HitResult result)
    {
        switch (result)
        {
            case HitResult.Parried:
                Stagger(-_facing);
                break;
            case HitResult.Blocked:
                Recoil(-_facing);
                if (_attack.Shape is "arc" or "charge")
                    Enter(State.Recover);
                break;
        }
    }

    private void Die(Vector3 dir)
    {
        Enter(State.Dead);
        Hero.Instance?.OnKill(this);
        Sfx.Play(Def.Sounds.Death, -2f, 0.7f);
        Dust.Puff(GlobalPosition, 10, Vector3.Zero);
        if (Hushed)
            // 이름을 잃은 것은 재가 되어 흩어진다
            CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.6f, 22, 1.1f, CombatFx.PaletteOf("hush"));
        CollisionLayer = 0;
        _sprite.PlayOnce(_sprite.Has("death") ? "death" : "hurt", T.DeathAnimTime);
        // 쓰러진 채 잠깐 남았다가 깜빡이며 사라진다 (옛 액션 게임식)
        var tw = CreateTween();
        tw.TweenInterval(T.DeathAnimTime + (Def.Boss ? T.CorpseTime * 3f : T.CorpseTime));
        for (int i = 0; i < Mathf.RoundToInt(T.CorpseBlinkTime / T.CorpseBlinkStep); i++)
        {
            tw.TweenCallback(Callable.From(() => _sprite.Visible = !_sprite.Visible));
            tw.TweenInterval(T.CorpseBlinkStep);
        }
        tw.TweenCallback(Callable.From(QueueFree));
        EmitSignal(SignalName.Died);
    }

    /// <summary>소리 없이 치운다 (스포너: 멀어진 것)</summary>
    public void Despawn()
    {
        _state = State.Dead;
        QueueFree();
    }

    private void Enter(State s)
    {
        var prev = _state;
        _state = s;
        _stateTime = 0f;
        var a = _attack;
        ShowWarning(s == State.Windup && a.Telegraph);
        switch (s)
        {
            case State.Alert:
                _alerted = true;
                Hud.Float(GlobalPosition + Vector3.Up * (Def.HeadHeight + 0.3f), "!", new Color(1f, 0.85f, 0.3f));
                if (Def.Sounds.Alert != null)
                    Sfx.Play(Def.Sounds.Alert, -8f, 1.2f);
                break;
            case State.Chase:
                _alerted = true;
                if (prev is State.Alert or State.Idle or State.Return)
                    _circleLeft = Rng.RandfRange(0.6f, 1.6f);
                SetFlash(0f, Colors.White);
                break;
            case State.Windup:
                _attackResolved = false;
                _aim = GlobalPosition;
                string anim = _sprite.Has(a.Anim) ? a.Anim : "rot";
                float total = a.Windup + a.Active + a.Recover;
                if (a.Shape == "charge")
                    _sprite.PlayOnce(anim, a.Windup);
                else if (a.HitFrame > 0)
                    _sprite.PlayKeyed(anim, total, 0, a.HitFrame, a.Windup);   // 닿는 칸을 판정 순간에
                else
                    _sprite.PlayOnce(anim, total);
                Sfx.Play(a.Sound ?? Def.Sounds.Windup, -6f, 1.4f);
                break;
            case State.Active:
                SetFlash(0f, Colors.White);
                switch (a.Shape)
                {
                    case "circle" or "nova":
                        Slam(Hero.Instance);
                        _attackResolved = true;
                        break;
                    case "shot":
                        Vector3 origin = GlobalPosition + _facing * (Def.Radius + 0.3f);
                        EnemyShot.Fire(this, origin, _facing, a.Shot, Damage);
                        Sfx.Play("dash", -6f, 1.3f);
                        _attackResolved = true;
                        break;
                    case "charge":
                        Dust.Puff(GlobalPosition, 10, -_facing * 2f);
                        Sfx.Play("dash", -4f, 0.7f);
                        break;
                }
                break;
            case State.Hurt:
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.28f);
                break;
            case State.Stagger:
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.4f);
                break;
            case State.Guard:
                _sprite.PlayOnce(_sprite.Has("guard") ? "guard" : "rot", 0.25f);
                Sfx.Play("impactMetal_medium", -6f, 0.7f);
                break;
            case State.Recover or State.Idle or State.Return:
                SetFlash(0f, Colors.White);
                break;
        }
    }

    // ── 예고 표시 ───────────────────────────────────────

    private float _warnDiameter;
    private float _warnLength;

    /// <summary>강한 공격 예고: 부채꼴·자리 공격은 원, 돌진은 달릴 길을 따라 긴 띠.</summary>
    private void ShowWarning(bool on)
    {
        if (_warn == null)
        {
            if (!on)
                return;
            _warnMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_warn.gdshader") };
            _warn = new MeshInstance3D
            {
                MaterialOverride = _warnMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TopLevel = true,
            };
            AddChild(_warn);
        }
        _warn.Visible = on;
        if (!on)
            return;
        var a = _attack;
        float sinPitch = Mathf.Sin(Mathf.DegToRad(Px.PitchDeg));
        _warnLength = 0f;
        switch (a.Shape)
        {
            case "circle" or "nova":
                _warnDiameter = a.Radius * 2f;
                break;
            case "charge":
                _warnDiameter = (Def.Radius + T.MonsterRamReach + a.Reach) * 2f;
                _warnLength = a.Lunge * a.Active + _warnDiameter;
                break;
            default:
                _warnDiameter = a.Reach * 1.6f;
                break;
        }
        // 내려다보면 세로가 sin(피치)만큼 줄어든다 — 화면에서 동그랗게 보이도록 늘린다 (띠는 달리는 쪽으로 돌려 놓는다)
        _warn.Mesh = _warnLength > 0f
            ? new PlaneMesh { Size = new Vector2(_warnDiameter, _warnLength) }
            : new PlaneMesh { Size = new Vector2(_warnDiameter, _warnDiameter / sinPitch) };
        _warnMat.SetShaderParameter(Uniform.Px, Mathf.Max(_warnDiameter, _warnLength) * Px.PerMeter);
        UpdateWarning(a, 0f);
    }

    private void UpdateWarning(MonsterDef.AttackDef a, float fill)
    {
        if (_warn == null || !_warn.Visible)
            return;
        var world = GameRoot.Instance.World;
        Vector3 c;
        var basis = Basis.Identity;
        switch (a.Shape)
        {
            case "circle" or "nova":
                c = _aim;
                break;
            case "charge":
                c = GlobalPosition + _facing * (_warnLength * 0.5f - _warnDiameter * 0.5f);
                basis = Basis.LookingAt(-_facing, Vector3.Up);
                break;
            default:
                c = GlobalPosition + _facing * a.Reach * 0.55f;
                break;
        }
        c.Y = world.HeightAt(c.X, c.Z) + 0.06f;
        _warn.GlobalTransform = new Transform3D(basis, c);
        _warnMat.SetShaderParameter(Uniform.Fill, fill);
    }

    private void SetFlash(float amount, Color c)
    {
        _flash = amount;
        _flashColor = c;
        _sprite.SetFlash(amount, c);
    }

    private void Steer(Vector3 dir, float speed, float dt, bool face = true)
    {
        Vector3 target = dir.LengthSquared() > 0.01f ? dir.Normalized() * speed : Vector3.Zero;
        // 동료와 겹치지 않게 살짝 밀어낸다
        foreach (var other in All)
        {
            if (other == this || !other.Alive)
                continue;
            Vector3 away = Flat(GlobalPosition - other.GlobalPosition);
            float d = away.Length();
            float min = Def.Radius + other.Radius + 0.2f;
            if (d > 0.01f && d < min)
                target += away / d * (min - d) * 6f;
        }
        _vel = _vel.MoveToward(target, 16f * dt);
        if (face && dir.LengthSquared() > 0.01f)
            TurnToward(dir, dt, 420f);
    }

    /// <summary>몸을 한 번에 돌리지 않는다 — 초당 rate 도씩 돈다 (8방향 그림이 차례로 넘어간다)</summary>
    private void TurnToward(Vector3 dir, float dt, float rateDeg)
    {
        if (dir.LengthSquared() < 0.0001f)
            return;
        float cur = Mathf.Atan2(_facing.X, _facing.Z);
        float want = Mathf.Atan2(dir.X, dir.Z);
        float max = Mathf.DegToRad(rateDeg) * dt;
        cur += Mathf.Clamp(Mathf.Wrap(want - cur, -Mathf.Pi, Mathf.Pi), -max, max);
        _facing = new Vector3(Mathf.Sin(cur), 0f, Mathf.Cos(cur));
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    public static int DirIndex(Vector3 f) =>
        Mathf.PosMod(Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z)) / 45f), 8);
}

public enum HitResult { Hit, Blocked, Parried, Dodged, PerfectDodge }
