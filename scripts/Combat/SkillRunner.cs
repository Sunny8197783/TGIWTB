using System.Linq;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 스킬 해석기: SkillDef 의 이벤트를 시간 순서대로 실행한다. 주인공이 하나 들고 쓴다.
/// 자리는 모두 "주인공 발밑 + 바라보는 쪽으로 Forward m" 기준 — 발동 순간이 아니라 이벤트 순간의 자리라
/// 돌진하며 베는 스킬도 이벤트만 나열하면 된다.
/// </summary>
public sealed class SkillRunner
{
    private readonly Hero _hero;
    private SkillDef _def;
    private SkillDef.SkillEvent[] _events;
    private int _next;
    private float _t;
    private float _dashLeft;
    private CombatFx.Palette _pal;

    public SkillRunner(Hero hero) => _hero = hero;

    public SkillDef Def => _def;
    public bool Done => _def == null || _t >= _def.Duration;
    public bool Cancelable => _def != null && _t >= _def.CancelAt;
    /// <summary>스킬이 몸을 미는 속도 (dash 이벤트). 0 이면 주인공이 알아서 멈춘다.</summary>
    public Vector3 DashVelocity { get; private set; }
    /// <summary>돌진 중에는 맞지 않는다.</summary>
    public bool Invulnerable => _dashLeft > 0f;

    public void Start(SkillDef def)
    {
        _def = def;
        _events = def.Events.OrderBy(e => e.T).ToArray();
        _next = 0;
        _t = 0f;
        _dashLeft = 0f;
        DashVelocity = Vector3.Zero;
        _pal = CombatFx.PaletteOf(def.Palette);
    }

    public void Update(float dt)
    {
        if (_def == null)
            return;
        _t += dt;
        while (_next < _events.Length && _events[_next].T <= _t)
            Fire(_events[_next++]);
        if (_dashLeft > 0f)
        {
            _dashLeft -= dt;
            if (_dashLeft <= 0f)
                DashVelocity = Vector3.Zero;
        }
    }

    private void Fire(SkillDef.SkillEvent e)
    {
        Vector3 f = _hero.Forward;
        Vector3 left = Vector3.Up.Cross(f);
        Vector3 feet = _hero.GlobalPosition;
        Vector3 at = feet + f * e.Forward;
        Vector3 chest = at + Vector3.Up * 0.85f;
        switch (e.Fx)
        {
            case "sfx":
                Sfx.Play(e.Sound, e.Db, e.Pitch, 0.05f, "Hero");
                break;
            case "slash":
                switch (e.Shape)
                {
                    case "reverse":
                        CombatFx.Slash(chest, f, (left - Vector3.Up * 0.3f).Normalized(), e.Radius, e.Arc, e.Life, true, e.Thickness, _pal);
                        break;
                    case "vertical":
                        CombatFx.Slash(at + Vector3.Up * 0.5f, (f + Vector3.Down * 0.2f).Normalized(), Vector3.Up, e.Radius, e.Arc, e.Life, true, e.Thickness, _pal);
                        break;
                    case "spin":
                        CombatFx.Slash(feet + Vector3.Up * 0.7f, f, left, e.Radius, 360f, e.Life, false, e.Thickness, _pal);
                        break;
                    default:
                        CombatFx.Slash(chest, f, (left + Vector3.Up * 0.35f).Normalized(), e.Radius, e.Arc, e.Life, false, e.Thickness, _pal);
                        break;
                }
                break;
            case "ring":
                CombatFx.Shockwave(at, e.Radius, e.Life, _pal);
                break;
            case "sparkle":
                CombatFx.SparkleBurst(at + Vector3.Up * 0.8f, e.Count, e.Radius, _pal);
                break;
            case "petals":
                CombatFx.Petals(at + Vector3.Up * 0.6f, f, e.Count, e.Radius);
                break;
            case "lightning":
                CombatFx.Lightning(at, e.Life, _pal);
                break;
            case "flash":
                CombatFx.Flash(_pal.Bright, e.Amount);
                break;
            case "shake":
                CombatFx.Shake(e.Amount);
                break;
            case "slow":
                GameRoot.Instance.SlowWorld(e.Amount, e.Time, tint: false); // 스킬의 짧은 멈칫 — 완벽 회피 색은 켜지 않는다
                break;
            case "dash":
                DashVelocity = f * (e.Distance / Mathf.Max(e.Time, 0.01f));
                _dashLeft = e.Time;
                break;
            case "hit":
                Hit(e, f, at);
                break;
            case "spin":
                _hero.Spin(e.Anim, e.Frame, e.Time);
                break;
            default:
                GD.PushWarning($"[Skill] {_def.Id}: 모르는 이벤트 {e.Fx}");
                break;
        }
    }

    private void Hit(SkillDef.SkillEvent e, Vector3 f, Vector3 at)
    {
        bool heavy = e.Impact == "heavy";
        float damage = e.Damage * (GameRoot.Instance.WorldSlowed ? T.WitchDamage : 1f);
        int hits = 0;
        foreach (var enemy in Enemy.All.ToArray())
        {
            if (!enemy.Alive)
                continue;
            Vector3 to = Flat(enemy.GlobalPosition - (e.Shape == "circle" ? at : _hero.GlobalPosition));
            float d = to.Length();
            bool inside = e.Shape == "circle"
                ? d - enemy.Radius <= e.Radius
                : d - enemy.Radius <= e.Radius && (d < 0.4f || Mathf.RadToDeg(f.AngleTo(to)) <= e.Arc * 0.5f);
            if (!inside)
                continue;
            hits++;
            Vector3 dir = Flat(enemy.GlobalPosition - _hero.GlobalPosition);
            _hero.Strike(enemy, damage, dir.LengthSquared() > 0.01f ? dir.Normalized() : f, heavy);
        }
        if (hits > 0)
            _hero.ApplyImpact(heavy ? T.HitHeavy : T.HitNormal, f);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
