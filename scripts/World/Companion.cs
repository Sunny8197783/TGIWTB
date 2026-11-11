using System.Linq;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.World;

/// <summary>
/// 나래 — 처음부터 함께 걷고 함께 싸우다가, 주인공이 이름을 받는 날 떠나고, 가장 위험한 순간에 돌아오는 동행.
/// 세이브 값 narae: 0 아직 만나기 전(나루에서 기다린다) · 1 함께 · 2 떠남 · 3 돌아와 함께.
///  - 따라오기: 주인공 뒤 옆에 선다. 너무 멀어지면 곁으로 건너온다.
///  - 싸우기: 주인공과 싸우는 적에게 붙어 등불 지팡이를 휘두른다. 맞지 않는다 (적은 주인공만 노린다).
///  - 무명지에서도 제 색 — 이름이 반쯤 남아 있기 때문.
/// 말 걸기(처음 만날 때)는 Story.TalkTo("narae").
/// </summary>
public partial class Companion : Node3D
{
    public static Companion Instance { get; private set; }

    private enum Mode { Absent, Waiting, Follow, Leaving }

    private const string Art = "res://art/characters/narae";
    private const string Placeholder = "res://art/characters/hero_ranger";
    private const float Speed = 5.8f;            // 주인공(5.2)보다 조금 빨라야 따라붙는다
    private const float FollowBack = 1.9f, FollowSide = 1.3f;
    private const float TeleportBeyond = 24f;
    private const float Sight = 9f;              // 이 거리 안에서 주인공과 싸우는 적을 돕는다
    private const float Reach = 1.9f;
    private const float SwingTime = 0.5f, SwingHit = 0.24f, SwingCooldown = 1.5f;   // 30f · 14f · 90f
    private const float BaseDamage = 8f, DamagePerLevel = 0.07f, ReturnedPower = 2.2f;
    private const float TalkReach = 2.8f;
    private const float LeaveTime = 4.5f;

    private readonly Hero _hero;
    private CharacterSprite _sprite;
    private Mode _mode = Mode.Absent;
    private Vector3 _vel;
    private Vector3 _facing = Vector3.Back;
    private float _cool, _swing = -1f;
    private bool _swingHit;
    private Enemy _target;
    private float _modeTime;
    private float _bark = 8f;
    private bool _returned;
    private static readonly RandomNumberGenerator Rng = new();

    /// <summary>화면에 있나, 그리고 말을 걸 수 있나 (HUD 가 이름표와 'F 대화'를 그린다)</summary>
    public bool Present => _mode != Mode.Absent;
    public bool CanTalk => _mode == Mode.Waiting && Flat(_hero.GlobalPosition - GlobalPosition).Length() < TalkReach;
    public string DisplayName => NpcDef.All.FirstOrDefault(n => n.Id == "narae")?.Name ?? "나래";

    public Companion(Hero hero)
    {
        Name = "Companion";
        _hero = hero;
        Instance = this;
    }

    public Companion() { }

    public override void _Ready()
    {
        TopLevel = true;
        _sprite = new CharacterSprite(ResourceLoader.Exists(Art + "/rot.png") ? Art : Placeholder);
        AddChild(_sprite);
        _sprite.ExemptFromHush();
        var world = GameRoot.Instance.World;
        switch (_hero.Var("narae"))
        {
            case 0:
                // 나루에서 기다린다 — 주인공이 깨어난 자리 바로 곁
                Vector2 spawn = world.Vec2("spawn");
                Place(new Vector3(spawn.X + 2.6f, 0f, spawn.Y - 1.8f));
                _mode = Mode.Waiting;
                break;
            case 1:
            case 3:
                _returned = _hero.Var("narae") == 3;
                Place(_hero.GlobalPosition + new Vector3(1.5f, 0f, 1.5f));
                _mode = Mode.Follow;
                break;
        }
        _sprite.Visible = _mode != Mode.Absent;
    }

    private void Place(Vector3 p)
    {
        var world = GameRoot.Instance.World;
        p.Y = world.WalkHeightAt(p.X, p.Z);
        GlobalPosition = p;
        _vel = Vector3.Zero;
    }

    /// <summary>이야기가 부른다: join(함께) · leave(떠난다) · stay(돌아온 채 계속 함께)</summary>
    public void Set(string mode)
    {
        switch (mode)
        {
            case "join":
                _hero.SetVar("narae", 1);
                _mode = Mode.Follow;
                _sprite.Visible = true;
                Cheer();
                break;
            case "leave":
                _hero.SetVar("narae", 2);
                _mode = Mode.Leaving;
                _modeTime = 0f;
                break;
            case "stay":
                _hero.SetVar("narae", 3);
                _returned = true;
                if (_mode == Mode.Absent)
                {
                    Place(_hero.GlobalPosition + new Vector3(1.5f, 0f, 1.5f));
                    _sprite.Visible = true;
                }
                _mode = Mode.Follow;
                break;
        }
    }

    /// <summary>돌아온다: 등불을 치켜들고 그 자리에 나타나 곧장 함께 싸운다 (전보다 훨씬 세다)</summary>
    public void Return(Vector3 at)
    {
        _hero.SetVar("narae", 3);
        _returned = true;
        Place(at);
        _sprite.Visible = true;
        _mode = Mode.Follow;
        _cool = 0.6f;
        var pal = CombatFx.PaletteOf("lantern");
        CombatFx.Shockwave(GlobalPosition, 4f, 0.7f, pal);
        CombatFx.SparkleBurst(GlobalPosition + Vector3.Up, 50, 2f, pal);
        Cheer();
    }

    private void Cheer()
    {
        if (_sprite.Has("cheer"))
        {
            _sprite.PlayOnce("cheer", 0.9f);
            _swing = -1f;
            _cool = Mathf.Max(_cool, 0.9f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode == Mode.Absent)
            return;
        float dt = (float)delta * GameRoot.Instance.HeroScale;
        if (dt <= 0f)
            return;
        _modeTime += dt;
        _cool -= dt;
        var world = GameRoot.Instance.World;
        Vector3 toHero = Flat(_hero.GlobalPosition - GlobalPosition);

        switch (_mode)
        {
            case Mode.Waiting:
                _vel = Vector3.Zero;
                if (toHero.Length() < 8f)
                    Face(toHero);
                if (CanTalk && _hero.IsAlive && !GameRoot.Instance.InputLocked && Input.IsActionJustPressed(Controls.Interact))
                    Story.Instance?.TalkTo("narae");
                break;

            case Mode.Leaving:
                // 몇 걸음 물러나 돌아보고, 손을 흔들고, 등불빛 속으로 사라진다
                if (_modeTime < 1.2f)
                    Steer(-toHero + new Vector3(0.4f, 0f, 0f), Speed * 0.45f, dt);
                else if (_modeTime < 2.4f)
                {
                    _vel = _vel.MoveToward(Vector3.Zero, 30f * dt);
                    Face(toHero);
                    if (_modeTime - dt < 1.2f)
                        Cheer();
                }
                else
                    Steer(-toHero, Speed * 0.8f, dt);
                if (_modeTime >= LeaveTime)
                {
                    CombatFx.SparkleBurst(GlobalPosition + Vector3.Up, 40, 1.6f, CombatFx.PaletteOf("lantern"));
                    Sfx.Play("impactBell_heavy", -10f, 1.8f, 0f, "UI");
                    _sprite.Visible = false;
                    _mode = Mode.Absent;
                    return;
                }
                break;

            case Mode.Follow:
                if (toHero.Length() > TeleportBeyond || GameRoot.Instance.Cinematic && toHero.Length() > 12f)
                {
                    Place(_hero.GlobalPosition - _hero.Forward * FollowBack);
                    break;
                }
                TickFight(dt, toHero);
                break;
        }

        // 지형: 깊은 물·절벽은 피하고 발을 땅에 붙인다 (주인공과 같은 규칙)
        _vel = world.ConstrainMove(GlobalPosition, _vel, dt, T.WadeDepth, T.MaxSlope);
        Vector3 p = GlobalPosition + _vel * dt;
        p.Y = world.WalkHeightAt(p.X, p.Z);
        GlobalPosition = p;
    }

    private void TickFight(float dt, Vector3 toHero)
    {
        // 휘두르는 중
        if (_swing >= 0f)
        {
            _swing += dt;
            _vel = _vel.MoveToward(Vector3.Zero, 30f * dt);
            if (!_swingHit && _swing >= SwingHit)
            {
                _swingHit = true;
                Hit();
            }
            if (_swing >= SwingTime)
                _swing = -1f;
            return;
        }
        if (_target == null || !IsInstanceValid(_target) || !_target.Alive || !_target.Engaged
            || Flat(_target.GlobalPosition - _hero.GlobalPosition).Length() > Sight * 1.4f)
            _target = FindTarget();
        if (_target != null && _hero.IsAlive && !GameRoot.Instance.Cinematic)
        {
            Vector3 to = Flat(_target.GlobalPosition - GlobalPosition);
            float d = to.Length() - _target.Radius;
            if (d > Reach * 0.8f)
                Steer(to, Speed, dt);
            else
            {
                _vel = _vel.MoveToward(Vector3.Zero, 30f * dt);
                Face(to);
                if (_cool <= 0f)
                {
                    _swing = 0f;
                    _swingHit = false;
                    _cool = SwingCooldown * (_returned ? 0.7f : 1f);
                    _sprite.PlayOnce(_sprite.Has("attack") ? "attack" : "rot", SwingTime);
                    Sfx.Play("swing", -9f, 1.25f, 0.08f);
                }
            }
            return;
        }
        // 따라가기: 주인공 뒤 옆자리
        Vector3 f = _hero.Forward;
        Vector3 spot = _hero.GlobalPosition - f * FollowBack + Vector3.Up.Cross(f) * FollowSide;
        Vector3 go = Flat(spot - GlobalPosition);
        float dist = go.Length();
        if (dist > 0.7f)
            Steer(go, Mathf.Min(Speed, dist * 3.2f), dt);
        else
        {
            _vel = _vel.MoveToward(Vector3.Zero, 30f * dt);
            if (toHero.Length() > 0.5f && _vel.LengthSquared() < 0.05f)
                Face(f);
        }
        Bark(dt);
    }

    private Enemy FindTarget()
    {
        Enemy best = null;
        float bestD = Sight;
        foreach (var e in Enemy.All)
        {
            if (!e.Alive || !e.Engaged)
                continue;
            float d = Flat(e.GlobalPosition - _hero.GlobalPosition).Length();
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }
        return best;
    }

    private void Hit()
    {
        if (_target == null || !IsInstanceValid(_target) || !_target.Alive)
            return;
        Vector3 to = Flat(_target.GlobalPosition - GlobalPosition);
        if (to.Length() - _target.Radius > Reach + 0.6f)
            return;
        Vector3 dir = to.LengthSquared() > 0.01f ? to.Normalized() : _facing;
        float damage = BaseDamage * (1f + DamagePerLevel * (_hero.Level - 1)) * (_returned ? ReturnedPower : 1f);
        var pal = CombatFx.PaletteOf("lantern");
        Vector3 at = _target.GlobalPosition + Vector3.Up * 0.5f - dir * _target.Radius * 0.5f;
        _target.TakeHit(damage, dir, false);
        CombatFx.Sparks(at, dir, CombatFx.Spark.Hit);
        CombatFx.Star(at, 0.5f, pal.Bright, 0.08f);
        if (_returned)
            CombatFx.SparkleBurst(at, 10, 0.5f, pal);
        Sfx.Play("impactPunch_medium", -8f, 1.3f, 0.08f);
    }

    /// <summary>가끔 한마디 (머리 위에 뜬다)</summary>
    private void Bark(float dt)
    {
        _bark -= dt;
        if (_bark > 0f)
            return;
        _bark = Rng.RandfRange(22f, 40f);
        string line;
        if (_hero.Hp / _hero.MaxHp < 0.35f)
            line = "이슬 마셔! H!";
        else if (Hush.Instance?.ActiveZoneAt(GlobalPosition.X, GlobalPosition.Z) != null)
            line = Pick("여긴 소리까지 잿빛이야…", "이름비가 가까워.", "내 옆에 붙어 있어.");
        else if (GameRoot.Instance.DayCycle.IsNight)
            line = Pick("밤에는 이름이 흐려져. 조심해.", "등불 가까이 있어.");
        else
            line = Pick("바람 좋다.", "세계수, 여기서도 보이네.", "배고프다…", "저 꽃 이름이 뭐였더라.");
        Hud.Float(GlobalPosition + Vector3.Up * 2.3f, line, new Color(1f, 0.93f, 0.7f));
    }

    private static string Pick(params string[] lines) => lines[Rng.RandiRange(0, lines.Length - 1)];

    public override void _Process(double delta)
    {
        if (_mode == Mode.Absent)
            return;
        float dt = (float)delta * GameRoot.Instance.HeroScale;
        var world = GameRoot.Instance.World;
        float speed = _vel.Length();
        if (_swing < 0f && _sprite.Finished || _sprite.Anim is "idle" or "run" or "rot")
        {
            if (speed > 0.4f && _sprite.Has("run"))
                _sprite.Play("run", speed / T.StrideLength * _sprite.FrameCount, true);
            else if (speed <= 0.4f)
                _sprite.Play(_sprite.Has("idle") ? "idle" : "rot", T.IdleFps, true);
        }
        _sprite.Dir = Enemy.DirIndex(_facing);
        _sprite.Advance(dt);
        Vector3 feet = GlobalPosition;
        _sprite.PlaceAt(feet, world.WalkHeightAt(feet.X, feet.Z));
    }

    private void Steer(Vector3 dir, float speed, float dt)
    {
        Vector3 target = dir.LengthSquared() > 0.01f ? dir.Normalized() * speed : Vector3.Zero;
        _vel = _vel.MoveToward(target, 40f * dt);
        Face(dir);
    }

    private void Face(Vector3 dir)
    {
        if (dir.LengthSquared() > 0.01f)
            _facing = dir.Normalized();
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
