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
/// 공격 흐름: 쫓기 → 공격 고르기(사거리·대기시간·무게) → 예고(깜빡임·바닥 원/띠) → 판정 → 회복(빈틈).
/// 공격 종류: melee(돌진 부채꼴) · charge(일직선 돌진) · shoot(투사체) · slam(원, leap 이면 뛰어든다) · spin(제자리 둘레).
/// </summary>
public partial class Enemy : CharacterBody3D
{
    public static readonly List<Enemy> All = new();
    /// <summary>지금 싸우는 우두머리 — HUD 가 화면 위에 큰 체력바를 그린다</summary>
    public static Enemy EngagedBoss { get; private set; }

    private enum State { Idle, Chase, Windup, Active, Recover, Hurt, Stagger, Return, Dead }

    public MonsterDef Def { get; }
    public bool Alive => _state != State.Dead;
    public float Radius => Def.Radius;
    public int Level => Def.Level;
    public bool Staggered => _state == State.Stagger;
    /// <summary>지금 공격 중이거나 예고 중 — 막기를 누르면 이쪽으로 돈다.</summary>
    public bool Threatening => _state is State.Windup or State.Active;
    /// <summary>주인공을 쫓거나 싸우는 중 (이름표·우두머리 막대)</summary>
    public bool Engaged => _state is not (State.Idle or State.Return or State.Dead);
    public float HpRatio => Mathf.Clamp(_hp / Def.Hp, 0f, 1f);
    /// <summary>주인공에게서 멀면 잔다 (AI·물리·애니메이션 모두 멈춤).</summary>
    private bool Asleep => Hero.Instance != null
        && GlobalPosition.DistanceSquaredTo(Hero.Instance.GlobalPosition) > T.MonsterSleepRange * T.MonsterSleepRange;
    /// <summary>마지막으로 맞은 시각 (실제 초). HUD 가 맞은 적만 체력바를 띄운다.</summary>
    public double LastHitAt { get; private set; } = -99;
    /// <summary>공격 예고가 끝나기까지 남은 시간 (예고 중이 아니면 무한대). 개발 캡처가 반응 타이밍을 잡을 때 쓴다.</summary>
    public float WindupLeft => _state == State.Windup ? _windup - _stateTime : float.PositiveInfinity;

    private readonly Vector3 _home;
    private CharacterSprite _sprite;
    private State _state = State.Idle;
    private float _stateTime;
    private float _hp;
    private float _poiseDamage;
    private Vector3 _vel;
    private Vector3 _facing = Vector3.Back;
    private bool _attackResolved;
    private Vector3 _wanderTarget;
    private float _flash;
    private Color _flashColor = Colors.White;
    private MeshInstance3D _warn;
    private ShaderMaterial _warnMat;

    private MonsterDef.AttackDef _atk;
    private readonly Dictionary<MonsterDef.AttackDef, float> _cooldowns = new();
    private int _hitsLeft;
    private float _windup;
    private Vector3 _target;       // slam 이 떨어질 자리
    private Vector3 _leapFrom;
    private int _phase;
    private float _speedMul = 1f, _tempo = 1f;
    private float _strafe = 1f;
    private float _minReach = float.MaxValue, _maxReach;

    private const string PlaceholderArt = "res://art/characters/forest_goblin";
    private static readonly Color WarnOrange = new(1f, 0.55f, 0.15f);
    private static readonly Color WarnPurple = new(0.85f, 0.3f, 1f);

    public Enemy(MonsterDef def, Vector3 home)
    {
        Def = def;
        _home = home;
    }

    public Enemy() { }

    public override void _Ready()
    {
        Name = Def.Id;
        _hp = Def.Hp;
        // 적끼리·주인공과는 몸으로 부딪히지 않는다 (돌진으로 빠져나가야 하니까). 땅·건물만.
        CollisionLayer = 1 << 2;
        CollisionMask = 1;
        MotionMode = MotionModeEnum.Floating;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Def.Radius, Height = Mathf.Max(Def.Radius * 2f, 1f) },
            Position = new Vector3(0f, Mathf.Max(Def.Radius, 0.5f), 0f),
        });
        // 그림을 아직 안 받은 몬스터는 고블린 그림으로 (동작 확인용)
        string art = ResourceLoader.Exists(Def.Art + "/rot.png") ? Def.Art : PlaceholderArt;
        _sprite = new CharacterSprite(art, Recolor.For(Def.Recolor), Def.Id)
            { ShadowHeight = Def.Radius * 1.8f, ShadowRadius = Def.Radius * 0.7f };
        AddChild(_sprite);
        _wanderTarget = _home;
        foreach (var a in Def.Attacks)
        {
            _minReach = Mathf.Min(_minReach, a.Reach);
            _maxReach = Mathf.Max(_maxReach, a.Reach);
        }
        All.Add(this);
    }

    public override void _ExitTree()
    {
        All.Remove(this);
        if (EngagedBoss == this)
            EngagedBoss = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.WorldScale;
        if (dt <= 0f || Asleep)
            return;
        _stateTime += dt;
        foreach (var a in Def.Attacks)
            if (_cooldowns.TryGetValue(a, out float c) && c > 0f)
                _cooldowns[a] = c - dt;
        var hero = Hero.Instance;
        Vector3 toHero = hero != null ? Flat(hero.GlobalPosition - GlobalPosition) : Vector3.Zero;
        float dist = toHero.Length();
        float speed = Def.Speed * _speedMul;

        switch (_state)
        {
            case State.Idle:
                if (hero != null && hero.IsAlive && dist < Def.Sight)
                {
                    Aggro();
                    break;
                }
                // 집 근처를 어슬렁거린다
                if (Flat(_wanderTarget - GlobalPosition).Length() < 0.4f || _stateTime > 4f)
                {
                    float ang = GD.Randf() * Mathf.Tau;
                    _wanderTarget = _home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (float)GD.RandRange(0.5, 3.0);
                    _stateTime = 0f;
                }
                Steer(Flat(_wanderTarget - GlobalPosition), speed * 0.4f, dt);
                break;

            case State.Chase:
                if (Flat(GlobalPosition - _home).Length() > Def.Leash)
                {
                    Enter(State.Return);
                    break;
                }
                if (hero == null || !hero.IsAlive || dist > Def.Sight * 1.6f)
                {
                    Enter(Flat(GlobalPosition - _home).Length() > 4f ? State.Return : State.Idle);
                    break;
                }
                if (PickAttack(dist) is { } pick)
                {
                    _atk = pick;
                    _hitsLeft = pick.Hits;
                    _facing = dist > 0.01f ? toHero / dist : _facing;
                    Enter(State.Windup);
                    break;
                }
                ChaseMove(toHero, dist, speed, dt);
                break;

            case State.Windup:
                Windup(hero, toHero, dist, dt);
                break;

            case State.Active:
                Active(hero, dt);
                break;

            case State.Recover:
                _vel = _vel.MoveToward(Vector3.Zero, 18f * dt);
                if (_stateTime >= _atk.Recover * _tempo)
                {
                    if (--_hitsLeft > 0)
                    {
                        if (dist > 0.01f)
                            _facing = toHero / dist;
                        Enter(State.Windup);
                        break;
                    }
                    _cooldowns[_atk] = _atk.Cooldown * _tempo;
                    if (_atk.Blink > 0f && hero != null)
                        Blink(hero);
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

            case State.Return:
                // 끌려 나왔다 — 체력을 채우며 집으로 (그동안은 맞지 않는다)
                _hp = Mathf.MoveToward(_hp, Def.Hp, Def.Hp * T.MonsterReturnHeal * dt);
                Vector3 home = Flat(_home - GlobalPosition);
                if (home.Length() < 1f)
                {
                    _hp = Def.Hp;
                    Enter(State.Idle);
                    break;
                }
                Steer(home, speed * T.MonsterReturnSpeed, dt);
                break;

            case State.Dead:
                _vel = _vel.MoveToward(Vector3.Zero, 10f * dt);
                break;
        }

        Hero.StepOnGround(this, ref _vel, dt, GameRoot.Instance.WorldScale);
    }

    // ── 쫓기·고르기 ─────────────────────────────────────

    private void Aggro()
    {
        if (_state != State.Idle)
            return;
        Enter(State.Chase);
        if (Def.Boss)
        {
            EngagedBoss = this;
            Sfx.Play(Def.Sounds.Windup, 0f, 0.6f);
        }
        // 같은 무리가 같이 덤빈다
        foreach (var e in All)
            if (e != this && e._state == State.Idle && e.GlobalPosition.DistanceTo(GlobalPosition) < T.MonsterPackAlert)
                e.Aggro();
    }

    /// <summary>쓸 수 있는 공격 중 무게로 하나 (사거리 안 · 대기시간 끝 · 단계가 열린 것)</summary>
    private MonsterDef.AttackDef PickAttack(float dist)
    {
        float total = 0f;
        foreach (var a in Def.Attacks)
            if (Usable(a, dist))
                total += a.Weight;
        if (total <= 0f)
            return null;
        float r = GD.Randf() * total;
        foreach (var a in Def.Attacks)
        {
            if (!Usable(a, dist))
                continue;
            r -= a.Weight;
            if (r <= 0f)
                return a;
        }
        return null;
    }

    private bool Usable(MonsterDef.AttackDef a, float dist) =>
        a.Phase <= _phase && dist <= a.Reach * 0.95f && dist >= a.MinRange && (!_cooldowns.TryGetValue(a, out float c) || c <= 0f);

    /// <summary>근접은 가장 짧은 사거리 앞까지 붙고, 원거리는 keep_away 와 사거리 사이에서 옆걸음질한다</summary>
    private void ChaseMove(Vector3 toHero, float dist, float speed, float dt)
    {
        if (Def.KeepAway > 0f)
        {
            Vector3 side = new Vector3(-toHero.Z, 0f, toHero.X).Normalized() * _strafe;
            if (dist < Def.KeepAway)
                Steer(-toHero.Normalized() + side * 0.4f, speed, dt);
            else if (dist > _maxReach * 0.85f)
                Steer(toHero, speed, dt);
            else
            {
                if (GD.Randf() < dt * 0.4f)
                    _strafe = -_strafe;
                Steer(side, speed * 0.45f, dt);
            }
            if (dist > 0.01f)
                _facing = toHero / dist;
            return;
        }
        Steer(dist > _minReach * 0.8f ? toHero : Vector3.Zero, speed, dt);
        if (dist > 0.01f)
            _facing = toHero / dist;
    }

    /// <summary>여우불: 공격 뒤 주인공 둘레 다른 자리로 사라졌다 나타난다</summary>
    private void Blink(Hero hero)
    {
        var world = GameRoot.Instance.World;
        var pal = CombatFx.PaletteOf(Def.Palette);
        for (int i = 0; i < 6; i++)
        {
            float ang = GD.Randf() * Mathf.Tau;
            Vector3 to = hero.GlobalPosition + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * _atk.Blink;
            if (Flat(to - _home).Length() > Def.Leash * 0.8f || world.WaterAt(to.X, to.Z) > world.HeightAt(to.X, to.Z))
                continue;
            CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.6f, 14, 0.6f, pal);
            GlobalPosition = new Vector3(to.X, world.WalkHeightAt(to.X, to.Z) + 0.05f, to.Z);
            CombatFx.SparkleBurst(GlobalPosition + Vector3.Up * 0.6f, 14, 0.6f, pal);
            Sfx.Play("select", -8f, 1.6f, 0.1f);
            return;
        }
    }

    // ── 예고·판정 ───────────────────────────────────────

    private void Windup(Hero hero, Vector3 toHero, float dist, float dt)
    {
        var a = _atk;
        float k = _stateTime / _windup;
        // 앞부분엔 주인공을 따라 돌고, 뒷부분엔 방향이 고정된다 — 끝에 피하면 피해진다
        float track = a.Kind switch { "shoot" => 0.85f, "charge" => 0.7f, _ => 0.6f };
        if (k < track && dist > 0.01f)
        {
            _facing = toHero / dist;
            if (a.Kind == "slam" && a.Leap && hero != null)
                _target = Flat(hero.GlobalPosition);
        }
        _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
        // 깜빡임이 점점 빨라진다 (못 막는 공격은 자줏빛)
        SetFlash(Mathf.Sin(k * k * 30f) > 0f ? 0.55f * k : 0f, a.Unblockable ? WarnPurple : WarnOrange);
        if (_warn != null && _warn.Visible)
        {
            PlaceWarning();
            _warnMat.SetShaderParameter(Uniform.Fill, Mathf.Clamp(k, 0f, 1f));
        }
        if (_stateTime >= _windup)
            Enter(State.Active);
    }

    private void Active(Hero hero, float dt)
    {
        var a = _atk;
        float active = a.Active * _tempo;
        switch (a.Kind)
        {
            case "charge":
            {
                _vel = _facing * a.Lunge;
                float d = hero != null ? Flat(hero.GlobalPosition - GlobalPosition).Length() : float.MaxValue;
                if (!_attackResolved && d < (Def.Radius + T.ChargeHitPad) * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
                    _attackResolved = true;
                if (!_attackResolved && d < Def.Radius + T.ChargeHitPad)
                    HitHero(hero, a.Damage, _facing, true);
                break;
            }
            case "slam" when a.Leap:
            {
                // 포물선으로 뛰어 찍어 둔 자리에 떨어진다 (몸은 땅에 붙어 가고 그림만 뜬다)
                float t = Mathf.Clamp(_stateTime / active, 0f, 1f);
                Vector3 p = _leapFrom.Lerp(_target, t);
                _vel = (Flat(p - GlobalPosition)) / Mathf.Max(dt, 0.001f);
                break;
            }
            case "shoot" or "spin" or "slam":
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                break;
            default:
                _vel = _facing * a.Lunge * (1f - _stateTime / active * 0.5f);
                if (!_attackResolved && hero != null)
                    TryMelee(hero);
                break;
        }
        if (_stateTime >= active)
        {
            if (a.Kind == "slam")
                Impact(a.Leap ? _target : FrontCenter(), a.Radius);
            Enter(State.Recover);
        }
    }

    private void TryMelee(Hero hero)
    {
        Vector3 to = Flat(hero.GlobalPosition - GlobalPosition);
        float d = to.Length();
        var a = _atk;
        // 공격이 거의 닿을 뻔한 거리에서 막 돌진했다 → 완벽 회피
        if (d <= a.Reach * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
        {
            _attackResolved = true;
            return;
        }
        if (d > a.Reach || (d > 0.3f && _facing.Dot(to / d) < 0.2f))
            return;
        HitHero(hero, a.Damage, _facing, false);
    }

    /// <summary>원 안을 친다 (내려찍기·회전). 연출은 맞든 안 맞든.</summary>
    private void Impact(Vector3 center, float radius)
    {
        var hero = Hero.Instance;
        var pal = CombatFx.PaletteOf(Def.Palette);
        center.Y = GameRoot.Instance.World.WalkHeightAt(center.X, center.Z);
        CombatFx.Shockwave(center, radius * 1.1f, 0.45f, pal);
        Dust.Puff(center, 14, Vector3.Zero);
        CombatFx.Shake(Def.Boss ? 0.45f : 0.25f);
        Sfx.Play("impactPlate_heavy", -3f, Def.Boss ? 0.6f : 0.8f, 0.05f);
        if (hero == null || _attackResolved)
            return;
        Vector3 to = Flat(hero.GlobalPosition - center);
        if (to.Length() > radius + 0.3f)
        {
            // 원 끝에서 막 돌진해 빠져나갔다 → 완벽 회피
            if (to.Length() <= radius * T.PerfectDodgeReach + 0.5f)
                hero.TryPerfectDodge(this);
            return;
        }
        if (hero.TryPerfectDodge(this))
            return;
        HitHero(hero, _atk.Damage, to.LengthSquared() > 0.01f ? to.Normalized() : _facing, false, center);
    }

    private void HitHero(Hero hero, float damage, Vector3 dir, bool charge, Vector3? from = null)
    {
        _attackResolved = true;
        var result = hero.ReceiveAttack(from ?? GlobalPosition, damage, dir, _atk.Unblockable);
        switch (result)
        {
            case HitResult.Parried:
                _hitsLeft = 0;
                Stagger(-_facing);
                break;
            case HitResult.Blocked:
                _hitsLeft = 0;
                Recoil(-_facing);
                if (charge || _atk.Kind == "melee")
                    Enter(State.Recover);
                break;
        }
    }

    /// <summary>투사체: 주인공 쪽으로 (여럿이면 부채꼴)</summary>
    private void Shoot(Hero hero)
    {
        var a = _atk;
        Vector3 dir = hero != null ? Flat(hero.GlobalPosition - GlobalPosition).Normalized() : _facing;
        if (dir.LengthSquared() < 0.01f)
            dir = _facing;
        Projectile.FireHostile(this, GlobalPosition, dir, a.Projectile, a.Damage, a.Unblockable, CombatFx.PaletteOf(Def.Palette));
        Sfx.Play(Def.Sounds.Shoot, -5f, 1.2f, 0.08f);
    }

    // ── 맞기 ────────────────────────────────────────────

    /// <summary>주인공의 칼에 맞았다. 죽었으면 true.</summary>
    public bool TakeHit(float damage, Vector3 dir, bool heavy)
    {
        if (!Alive)
            return false;
        if (_state == State.Return)
        {
            Hud.Text(GlobalPosition + Vector3.Up * Def.Radius * 2.2f, "회피");
            return false;
        }
        if (_state == State.Stagger)
            damage *= T.StaggerDamage;
        // 등껍질: 앞에서 오는 가벼운 공격은 막는다 (강타·경직·공격 중엔 못 막는다)
        bool guarded = Def.GuardArcDeg > 0f && !heavy && (_state is State.Idle or State.Chase or State.Hurt)
                       && Mathf.RadToDeg(_facing.AngleTo(-Flat(dir).Normalized())) <= Def.GuardArcDeg;
        if (guarded)
        {
            damage *= Def.GuardTake;
            Vector3 at = GlobalPosition + Vector3.Up * Def.Radius * 1.4f - Flat(dir).Normalized() * Def.Radius;
            CombatFx.Sparks(at, -Flat(dir).Normalized(), CombatFx.Spark.Block);
            Sfx.Play("impactMetal_light", -4f, 0.7f, 0.08f);
        }
        _hp -= damage;
        LastHitAt = Time.GetTicksMsec() / 1000.0;
        Hud.Damage(GlobalPosition + Vector3.Up * Def.Radius * 2.2f, damage, heavy);
        _flash = 1f;
        _flashColor = guarded ? new Color(0.6f, 0.8f, 1f) : Colors.White;
        if (!guarded)
            _vel = Flat(dir).Normalized() * (heavy ? 7f : 3.5f) * Def.Knockback;
        Sfx.Play(Def.Sounds.Hit, -3f, heavy ? 0.8f : 1f);
        if (_hp <= 0f)
        {
            Die();
            return true;
        }
        if (_state == State.Idle)
            Aggro();
        CheckPhase();
        if (guarded)
            return false;
        _poiseDamage += damage;
        // 버팀(poise)을 넘거나 강타면 움찔 — 공격 중이어도 끊긴다 (우두머리는 강타로는 안 끊긴다). 경직 중이면 경직 유지.
        if (_state != State.Stagger && ((heavy && !Def.Boss) || _poiseDamage >= Def.Poise))
        {
            _poiseDamage = 0f;
            _hitsLeft = 0;
            if (_atk != null && (_state is State.Windup or State.Active))
                _cooldowns[_atk] = _atk.Cooldown * 0.5f;
            Enter(State.Hurt);
        }
        return false;
    }

    /// <summary>우두머리: 체력이 문턱을 넘으면 다음 단계 — 빨라지고 새 공격이 열린다</summary>
    private void CheckPhase()
    {
        while (_phase < Def.Phases.Count && HpRatio <= Def.Phases[_phase].HpBelow)
        {
            var p = Def.Phases[_phase++];
            _speedMul = p.Speed;
            _tempo = p.Tempo;
            if (!string.IsNullOrEmpty(p.Line))
                Hud.Announce(Def.Name, p.Line, new Color(1f, 0.5f, 0.35f));
            CombatFx.Shockwave(GlobalPosition, 3f, 0.6f, CombatFx.PaletteOf(Def.Palette));
            CombatFx.Shake(0.4f);
            Sfx.Play(Def.Sounds.Windup, 2f, 0.5f);
        }
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

    private void Die()
    {
        Enter(State.Dead);
        if (Hero.Instance is IPlayerContext player)
            player.GrantExp(Mathf.RoundToInt(Def.Exp * LevelDef.Instance.ExpFactor(Def.Level, player.Level)));
        if (Def.Boss)
            Hud.Announce($"{Def.Name} 처치", null, new Color(1f, 0.85f, 0.4f));
        if (EngagedBoss == this)
            EngagedBoss = null;
        Sfx.Play(Def.Sounds.Death, -2f, 0.7f);
        Dust.Puff(GlobalPosition, 10, Vector3.Zero);
        CollisionLayer = 0;
        _sprite.PlayOnce(_sprite.Has("death") ? "death" : "hurt", T.DeathAnimTime);
        // 쓰러진 채 잠깐 남았다가 깜빡이며 사라진다 (옛 액션 게임식)
        var tw = CreateTween();
        tw.TweenInterval(T.DeathAnimTime + T.CorpseTime);
        for (int i = 0; i < Mathf.RoundToInt(T.CorpseBlinkTime / T.CorpseBlinkStep); i++)
        {
            tw.TweenCallback(Callable.From(() => _sprite.Visible = !_sprite.Visible));
            tw.TweenInterval(T.CorpseBlinkStep);
        }
        tw.TweenCallback(Callable.From(QueueFree));
        EmitSignal(SignalName.Died);
    }

    [Signal]
    public delegate void DiedEventHandler();

    // ── 그리기 ──────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (Asleep)
            return;
        float dt = (float)delta * GameRoot.Instance.WorldScale;
        var world = GameRoot.Instance.World;
        switch (_state)
        {
            case State.Idle or State.Chase or State.Return:
                float speed = _vel.Length();
                if (speed > 0.3f && _sprite.Has("move"))
                    _sprite.Play("move", 8f * Mathf.Clamp(speed / Mathf.Max(Def.Speed, 0.1f), 0.6f, 1.6f), true);
                else
                    _sprite.Play(_sprite.Has("idle") ? "idle" : "rot", 6f, true);
                break;
        }
        _sprite.Dir = DirIndex(_facing);
        if (_state is not State.Windup and not State.Stagger)
        {
            _flash = Mathf.MoveToward(_flash, 0f, (float)delta * 8f);
            _sprite.SetFlash(_flash, _flashColor);
        }
        _sprite.Advance(dt);
        Vector3 feet = GlobalPosition;
        float ground = world.WalkHeightAt(feet.X, feet.Z);
        // 도약 중엔 그림만 포물선으로 뜬다
        float lift = _state == State.Active && _atk is { Kind: "slam", Leap: true }
            ? Mathf.Sin(Mathf.Clamp(_stateTime / (_atk.Active * _tempo), 0f, 1f) * Mathf.Pi) * T.LeapHeight : 0f;
        _sprite.PlaceAt(feet + Vector3.Up * lift, ground);
    }

    private void Enter(State s)
    {
        _state = s;
        _stateTime = 0f;
        var a = _atk;
        switch (s)
        {
            case State.Windup:
                _attackResolved = false;
                _windup = (_hitsLeft < a.Hits ? a.Gap : a.Windup) * _tempo;
                _target = a.Leap ? Flat(Hero.Instance?.GlobalPosition ?? GlobalPosition) : GlobalPosition;
                string anim = _sprite.Has(a.Anim) ? a.Anim : _sprite.Has("attack") ? "attack" : "rot";
                _sprite.PlayOnce(anim, _windup + (a.Active + a.Recover) * _tempo);
                if (_hitsLeft == a.Hits || a.Kind != "melee")
                    Sfx.Play(Def.Sounds.Windup, -6f, 1.4f);
                ShowWarning(a.Telegraph || a.Kind is "charge" or "slam" or "spin");
                break;
            case State.Active:
                ShowWarning(false);
                _leapFrom = GlobalPosition;
                if (a.Kind == "shoot")
                {
                    _attackResolved = true;
                    Shoot(Hero.Instance);
                }
                else if (a.Kind == "spin")
                    Impact(GlobalPosition, a.Radius);
                else if (a.Kind == "charge")
                    Sfx.Play("dash", -4f, 0.8f);
                break;
            case State.Hurt:
                ShowWarning(false);
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.28f);
                break;
            case State.Stagger:
                ShowWarning(false);
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.4f);
                break;
            case State.Return:
                ShowWarning(false);
                SetFlash(0f, Colors.White);
                if (EngagedBoss == this)
                    EngagedBoss = null;
                break;
            case State.Recover or State.Chase or State.Idle:
                ShowWarning(false);
                SetFlash(0f, Colors.White);
                if (s == State.Idle && EngagedBoss == this)
                    EngagedBoss = null;
                break;
            case State.Dead:
                ShowWarning(false);
                break;
        }
    }

    /// <summary>바닥 예고: 원(근접 앞·내려찍기·회전) 또는 띠(돌진). 안쪽이 예고 시간 동안 차오른다.</summary>
    private void ShowWarning(bool on)
    {
        if (_warn == null)
        {
            if (!on)
                return;
            _warnMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_warn.gdshader") };
            _warn = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = Vector2.One },
                MaterialOverride = _warnMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TopLevel = true,
            };
            AddChild(_warn);
        }
        _warn.Visible = on;
        if (!on)
            return;
        var a = _atk;
        _warnMat.SetShaderParameter(Uniform.Fill, 0f);
        _warnMat.SetShaderParameter("color", a.Unblockable ? new Color(0.85f, 0.3f, 1f) : new Color(1f, 0.25f, 0.2f));
        PlaceWarning();
    }

    private void PlaceWarning()
    {
        var a = _atk;
        float sinPitch = Mathf.Sin(Mathf.DegToRad(Px.PitchDeg));
        Vector3 c;
        Vector2 size;
        bool band = a.Kind == "charge";
        if (band)
        {
            // 돌진 띠: 몸 너비 × 돌진 거리, 앞쪽으로
            float len = a.Lunge * a.Active * _tempo;
            float w = (Def.Radius + T.ChargeHitPad) * 2f;
            c = GlobalPosition + _facing * len * 0.5f;
            size = new Vector2(w, len);
            _warn.Basis = Basis.LookingAt(_facing, Vector3.Up).Scaled(new Vector3(size.X, 1f, size.Y));
            _warnMat.SetShaderParameter("shape", 1);
            _warnMat.SetShaderParameter(Uniform.Px, new Vector2(size.X, size.Y) * Px.PerMeter);
        }
        else
        {
            float r = a.Kind switch { "slam" or "spin" => a.Radius, _ => a.Reach * 0.8f };
            c = a.Kind switch
            {
                "slam" when a.Leap => _target,
                "spin" => GlobalPosition,
                "slam" => FrontCenter(),
                _ => GlobalPosition + _facing * a.Reach * 0.55f,
            };
            // 내려다보면 세로가 sin(피치)만큼 줄어든다 — 화면에서 동그랗게 보이도록 늘린다
            size = new Vector2(r * 2f, r * 2f / sinPitch);
            _warn.Basis = Basis.Identity.Scaled(new Vector3(size.X, 1f, size.Y));
            _warnMat.SetShaderParameter("shape", 0);
            _warnMat.SetShaderParameter(Uniform.Px, new Vector2(r * 2f, r * 2f) * Px.PerMeter);
        }
        c.Y = GameRoot.Instance.World.WalkHeightAt(c.X, c.Z) + 0.06f;
        _warn.GlobalPosition = c;
    }

    private Vector3 FrontCenter() => GlobalPosition + _facing * Mathf.Max(_atk.Radius * 0.8f, Def.Radius + 0.5f);

    private void SetFlash(float amount, Color c)
    {
        _flash = amount;
        _flashColor = c;
        _sprite.SetFlash(amount, c);
    }

    private void Steer(Vector3 dir, float speed, float dt)
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
        if (dir.LengthSquared() > 0.01f && _state != State.Chase)
            _facing = dir.Normalized();
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    public static int DirIndex(Vector3 f) =>
        Mathf.PosMod(Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z)) / 45f), 8);
}

public enum HitResult { Hit, Blocked, Parried, Dodged, PerfectDodge }
