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
/// 공격 흐름: 쫓기 → 예고(주황 깜빡임, 플레이어가 읽을 시간) → 판정(돌진) → 회복(빈틈).
/// </summary>
public partial class Enemy : CharacterBody3D
{
    public static readonly List<Enemy> All = new();

    private enum State { Idle, Chase, Windup, Active, Recover, Hurt, Stagger, Dead }

    public MonsterDef Def { get; }
    public bool Alive => _state != State.Dead;
    public float Radius => Def.Radius;
    public bool Staggered => _state == State.Stagger;
    /// <summary>지금 공격 중이거나 예고 중 — 막기를 누르면 이쪽으로 돈다.</summary>
    public bool Threatening => _state is State.Windup or State.Active;
    public float HpRatio => Mathf.Clamp(_hp / Def.Hp, 0f, 1f);
    /// <summary>주인공에게서 멀면 잔다 (AI·물리·애니메이션 모두 멈춤).</summary>
    private bool Asleep => Hero.Instance != null
        && GlobalPosition.DistanceSquaredTo(Hero.Instance.GlobalPosition) > T.MonsterSleepRange * T.MonsterSleepRange;
    /// <summary>마지막으로 맞은 시각 (실제 초). HUD 가 맞은 적만 체력바를 띄운다.</summary>
    public double LastHitAt { get; private set; } = -99;
    /// <summary>공격 예고가 끝나기까지 남은 시간 (예고 중이 아니면 무한대). 개발 캡처가 반응 타이밍을 잡을 때 쓴다.</summary>
    public float WindupLeft => _state == State.Windup ? Def.Attack.Windup - _stateTime : float.PositiveInfinity;

    private readonly Vector3 _home;
    private CharacterSprite _sprite;
    private State _state = State.Idle;
    private float _stateTime;
    private float _hp;
    private float _poiseDamage;
    private float _cooldown;
    private Vector3 _vel;
    private Vector3 _facing = Vector3.Back;
    private bool _attackResolved;
    private Vector3 _wanderTarget;
    private float _flash;
    private Color _flashColor = Colors.White;

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
        _sprite = new CharacterSprite(Def.Art) { ShadowHeight = Def.Radius * 1.8f, ShadowRadius = Def.Radius * 0.7f };
        AddChild(_sprite);
        _wanderTarget = _home;
        All.Add(this);
    }

    public override void _ExitTree() => All.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta * GameRoot.Instance.WorldScale;
        if (dt <= 0f || Asleep)
            return;
        _stateTime += dt;
        _cooldown -= dt;
        var hero = Hero.Instance;
        Vector3 toHero = hero != null ? Flat(hero.GlobalPosition - GlobalPosition) : Vector3.Zero;
        float dist = toHero.Length();
        var a = Def.Attack;

        switch (_state)
        {
            case State.Idle:
                if (hero != null && hero.IsAlive && dist < Def.Sight)
                {
                    Enter(State.Chase);
                    break;
                }
                // 집 근처를 어슬렁거린다
                if (Flat(_wanderTarget - GlobalPosition).Length() < 0.4f || _stateTime > 4f)
                {
                    float ang = GD.Randf() * Mathf.Tau;
                    _wanderTarget = _home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (float)GD.RandRange(0.5, 3.0);
                    _stateTime = 0f;
                }
                Steer(Flat(_wanderTarget - GlobalPosition), Def.Speed * 0.4f, dt);
                break;

            case State.Chase:
                if (hero == null || !hero.IsAlive || dist > Def.Sight * 1.6f)
                {
                    Enter(State.Idle);
                    break;
                }
                if (dist <= a.Reach * 0.95f && _cooldown <= 0f)
                {
                    Enter(State.Windup);
                    break;
                }
                // 사거리 바로 앞까지만 붙는다
                Steer(dist > a.Reach * 0.8f ? toHero : Vector3.Zero, Def.Speed, dt);
                break;

            case State.Windup:
                // 예고 앞부분엔 주인공을 따라 돌고, 뒷부분엔 방향이 고정된다 — 끝에 피하면 피해진다
                if (_stateTime < a.Windup * 0.6f && dist > 0.01f)
                    _facing = toHero / dist;
                _vel = _vel.MoveToward(Vector3.Zero, 20f * dt);
                // 주황 깜빡임이 점점 빨라진다
                float k = _stateTime / a.Windup;
                SetFlash(Mathf.Sin(k * k * 30f) > 0f ? 0.55f * k : 0f, new Color(1f, 0.55f, 0.15f));
                if (_stateTime >= a.Windup)
                    Enter(State.Active);
                break;

            case State.Active:
                _vel = _facing * a.Lunge * (1f - _stateTime / a.Active * 0.5f);
                if (!_attackResolved && hero != null)
                    TryHitHero(hero);
                if (_stateTime >= a.Active)
                    Enter(State.Recover);
                break;

            case State.Recover:
                _vel = _vel.MoveToward(Vector3.Zero, 18f * dt);
                if (_stateTime >= a.Recover)
                {
                    _cooldown = a.Cooldown;
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
        float dt = (float)delta * GameRoot.Instance.WorldScale;
        var world = GameRoot.Instance.World;
        switch (_state)
        {
            case State.Idle or State.Chase:
                float speed = _vel.Length();
                if (speed > 0.3f && _sprite.Has("move"))
                    _sprite.Play("move", 8f, true);
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
        _sprite.PlaceAt(feet, world.HeightAt(feet.X, feet.Z));
    }

    /// <summary>주인공의 칼에 맞았다. 죽었으면 true.</summary>
    public bool TakeHit(float damage, Vector3 dir, bool heavy)
    {
        if (!Alive)
            return false;
        if (_state == State.Stagger)
            damage *= T.StaggerDamage;
        _hp -= damage;
        _poiseDamage += damage;
        LastHitAt = Time.GetTicksMsec() / 1000.0;
        Hud.Damage(GlobalPosition + Vector3.Up * Def.Radius * 2.2f, damage, heavy);
        _flash = 1f;
        _flashColor = Colors.White;
        _vel = Flat(dir).Normalized() * (heavy ? 7f : 3.5f) * Def.Knockback;
        Sfx.Play(Def.Sounds.Hit, -3f, heavy ? 0.8f : 1f);
        if (_hp <= 0f)
        {
            Die();
            return true;
        }
        // 강타이거나 버팀(poise)을 넘으면 움찔 — 공격 중이어도 끊긴다. 경직 중이면 경직 유지.
        if (_state != State.Stagger && (heavy || _poiseDamage >= Def.Poise))
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

    private void TryHitHero(Hero hero)
    {
        Vector3 to = Flat(hero.GlobalPosition - GlobalPosition);
        float d = to.Length();
        var a = Def.Attack;
        // 공격이 거의 닿을 뻔한 거리에서 막 돌진했다 → 완벽 회피
        if (d <= a.Reach * T.PerfectDodgeReach && hero.TryPerfectDodge(this))
        {
            _attackResolved = true;
            return;
        }
        if (d > a.Reach || (d > 0.3f && _facing.Dot(to / d) < 0.2f))
            return;
        _attackResolved = true;
        var result = hero.ReceiveAttack(this, a.Damage, _facing);
        switch (result)
        {
            case HitResult.Parried:
                Stagger(-_facing);
                break;
            case HitResult.Blocked:
                Recoil(-_facing);
                Enter(State.Recover);
                break;
        }
    }

    private void Die()
    {
        Enter(State.Dead);
        Sfx.Play(Def.Sounds.Death, -2f, 0.7f);
        Dust.Puff(GlobalPosition, 10, Vector3.Zero);
        CollisionLayer = 0;
        _sprite.PlayOnce(_sprite.Has("death") ? "death" : "hurt", 0.6f);
        var tw = CreateTween();
        tw.TweenInterval(0.9f);
        tw.TweenCallback(Callable.From(QueueFree));
        EmitSignal(SignalName.Died);
    }

    [Signal]
    public delegate void DiedEventHandler();

    private void Enter(State s)
    {
        _state = s;
        _stateTime = 0f;
        var a = Def.Attack;
        switch (s)
        {
            case State.Windup:
                _attackResolved = false;
                _sprite.PlayOnce(_sprite.Has("attack") ? "attack" : "rot", a.Windup + a.Active + a.Recover);
                Sfx.Play(Def.Sounds.Windup, -6f, 1.4f);
                break;
            case State.Hurt:
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.28f);
                break;
            case State.Stagger:
                _sprite.PlayOnce(_sprite.Has("hurt") ? "hurt" : "rot", 0.4f);
                break;
            case State.Recover or State.Chase or State.Idle:
                SetFlash(0f, Colors.White);
                break;
        }
    }

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
        if (dir.LengthSquared() > 0.01f)
            _facing = dir.Normalized();
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    public static int DirIndex(Vector3 f) =>
        Mathf.PosMod(Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z)) / 45f), 8);
}

public enum HitResult { Hit, Blocked, Parried, Dodged, PerfectDodge }
