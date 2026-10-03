using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 전망 지점과 지역 이름 (M4). 지도 meta 의 viewpoints·zones 를 읽는다.
///  - 전망 지점 반지름 안에 서면 카메라가 고개를 들고(피치↓) 물러선다 — 수평선·노을이 화면 위로 들어온다.
///    나가면 원래대로 (CameraRig 가 부드럽게 옮긴다). 들어설 때 이름 한 줄.
///  - 새 지역에 들어서면 지역 이름 한 줄 (같은 이름은 잠시 안 띄운다 — 경계를 오갈 때 시끄럽지 않게).
/// </summary>
public partial class Scenery : Node
{
    private readonly record struct Viewpoint(string Name, Vector2 At, float Radius, float Pitch, float Distance, Vector3 Look);
    private readonly record struct Zone(string Name, Rect2 Area);

    /// <summary>전망 지점을 벗어났다고 볼 여유 (m) — 반지름 언저리에서 카메라가 들썩이지 않게</summary>
    private const float ExitMargin = 2f;
    /// <summary>같은 지역 이름을 다시 띄우기까지 (초)</summary>
    private const double ZoneRepeat = 30.0;
    private static readonly Color ViewColor = new(1f, 0.86f, 0.55f);
    private static readonly Color ZoneColor = new(0.95f, 0.95f, 0.88f);

    private readonly List<Viewpoint> _views = new();
    private readonly List<Zone> _zones = new();
    private int _view = -1;
    private string _zone;
    private readonly Dictionary<string, double> _zoneShownAt = new();

    public Scenery(WorldData world)
    {
        Name = "Scenery";
        foreach (var v in world.Meta.GetProperty("viewpoints").EnumerateArray())
            _views.Add(new Viewpoint(v.GetProperty("name").GetString(),
                new Vector2(v.GetProperty("x").GetSingle(), v.GetProperty("z").GetSingle()),
                v.GetProperty("radius").GetSingle(),
                Opt(v, "pitch", Px.PitchDeg), Opt(v, "distance", Px.FocusDistance),
                new Vector3(0f, Opt(v, "look_up", 0f), -Opt(v, "look_ahead", 0f))));
        foreach (var z in world.Meta.GetProperty("zones").EnumerateArray())
        {
            float x0 = z.GetProperty("x0").GetSingle(), z0 = z.GetProperty("z0").GetSingle();
            _zones.Add(new Zone(z.GetProperty("name").GetString(),
                new Rect2(x0, z0, z.GetProperty("x1").GetSingle() - x0, z.GetProperty("z1").GetSingle() - z0)));
        }
    }

    private static float Opt(JsonElement e, string key, float fallback) =>
        e.TryGetProperty(key, out var v) ? v.GetSingle() : fallback;

    public override void _Process(double delta)
    {
        var hero = Hero.Instance;
        if (hero == null || GameRoot.Instance.MenuOpen)
            return; // 모습 고르기 중엔 이름을 띄우지 않는다 — 닫으면 그때 띄운다
        var at = new Vector2(hero.GlobalPosition.X, hero.GlobalPosition.Z);
        UpdateView(at);
        UpdateZone(at);
    }

    private void UpdateView(Vector2 at)
    {
        int now = -1;
        for (int i = 0; i < _views.Count; i++)
        {
            float r = _views[i].Radius + (i == _view ? ExitMargin : 0f);
            if (at.DistanceTo(_views[i].At) <= r)
            {
                now = i;
                break;
            }
        }
        if (now == _view)
            return;
        var rig = GameRoot.Instance.Rig;
        if (_view >= 0)
        {
            // 캡처처럼 다른 누가 카메라를 바꿔 놓았으면 건드리지 않는다
            var old = _views[_view];
            if (Mathf.IsEqualApprox(rig.Pitch, old.Pitch)) rig.Pitch = Px.PitchDeg;
            if (Mathf.IsEqualApprox(rig.Distance, old.Distance)) rig.Distance = Px.FocusDistance;
            if (rig.Offset.IsEqualApprox(old.Look)) rig.Offset = Vector3.Zero;
        }
        _view = now;
        if (now >= 0)
        {
            rig.Pitch = _views[now].Pitch;
            rig.Distance = _views[now].Distance;
            rig.Offset = _views[now].Look;
            Hud.Announce(_views[now].Name, null, ViewColor);
        }
    }

    private void UpdateZone(Vector2 at)
    {
        string now = null;
        foreach (var z in _zones)
        {
            if (z.Area.HasPoint(at))
            {
                now = z.Name;
                break;
            }
        }
        if (now == _zone)
            return;
        _zone = now;
        if (now == null || _view >= 0)
            return; // 전망 지점 이름이 먼저
        double t = Time.GetTicksMsec() / 1000.0;
        if (_zoneShownAt.TryGetValue(now, out double last) && t - last < ZoneRepeat)
            return;
        _zoneShownAt[now] = t;
        Hud.Announce(now, null, ZoneColor);
    }
}
