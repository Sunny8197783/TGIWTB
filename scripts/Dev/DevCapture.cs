using System.Collections.Generic;
using System.Globalization;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Dev;

/// <summary>
/// 눈으로 확인하는 캡처 도구. 게임 로직은 건드리지 않는다.
///   godot --path . -- --capture --shot=112,150,9 --shot=112,90,19,20,34 --out=user://shots
/// shot = x,z,시각[,피치,거리]. 주인공도 그 자리로 옮긴다. 찍고 나면 종료한다.
///   --hold=초          각 장면에 이만큼 머물며 프레임 시간을 잰다
///   --settle=N         장면을 옮긴 뒤 N 프레임 뒤에 찍는다 (기본 24, 동작 한가운데를 찍을 때 줄인다)
///   --press=a,b        장면마다 그 조작을 누른다 (move_right 처럼 누르고 있기, attack 처럼 한 번)
///   --react=guard|dodge --after=N
///                      적의 공격 예고가 끝나기 직전에 그 조작을 누르고 N 프레임 뒤에 찍는다 (패링·완벽 회피 확인)
///   --novsync          60fps 상한을 풀어 실제 여유를 잰다
/// </summary>
public partial class DevCapture : Node
{
    private readonly List<float[]> _shots = new();
    private string _out = "user://shots";
    private int _index = -1;
    private int _wait;
    private int _settle = 24;
    private float _hold;

    private readonly List<string> _press = new();
    private bool _pressPending;
    private string _react;
    private int _after = 6;
    private bool _reacted;

    private readonly List<float> _frameMs = new();
    private readonly List<float> _physMs = new();
    private ulong _last;

    public static HashSet<string> Disabled()
    {
        var set = new HashSet<string>();
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--no="))
                foreach (var part in a.Substring(5).Split(','))
                    set.Add(part);
        return set;
    }

    public static bool Requested()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--capture") return true;
        return false;
    }

    /// <summary>--mastery=sk_moon_crescent:29.5 — 진화 직전 상태로 시작해 진화를 확인한다.</summary>
    public static IEnumerable<(string id, float value)> MasterySeeds()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--mastery="))
                continue;
            foreach (var part in a.Substring(10).Split(','))
            {
                var kv = part.Split(':');
                yield return (kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture));
            }
        }
    }

    // "방금 눌렀다"는 물리 프레임 번호로 판정된다 — 주인공보다 먼저 도는 물리 프레임 안에서 눌러야 한다
    public override void _EnterTree() => ProcessPhysicsPriority = -100;

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot="))
            {
                var parts = a.Substring(7).Split(',');
                var v = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    v[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
                _shots.Add(v);
            }
            else if (a == "--novsync")
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            else if (a.StartsWith("--hold="))
                _hold = float.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--settle="))
                _settle = int.Parse(a.Substring(9));
            else if (a.StartsWith("--press="))
                _press.AddRange(a.Substring(8).Split(','));
            else if (a.StartsWith("--react="))
                _react = a.Substring(8);
            else if (a.StartsWith("--after="))
                _after = int.Parse(a.Substring(8));
            else if (a.StartsWith("--out="))
                _out = a.Substring(6);
        }
        DirAccess.MakeDirRecursiveAbsolute(_out);
        GD.Print($"[Capture] {_shots.Count}장 → {ProjectSettings.GlobalizePath(_out)}");
        // 불러온 직후 몇 프레임은 셰이더 컴파일로 길다 — 동작 타이밍을 찍으려면 먼저 가라앉힌다
        GetTree().CreateTimer(1.0).Timeout += Next;
    }

    private void Next()
    {
        _index++;
        if (_index >= _shots.Count)
        {
            GetTree().Quit();
            return;
        }
        var s = _shots[_index];
        var root = GameRoot.Instance;
        var feet = new Vector3(s[0], root.World.HeightAt(s[0], s[1]), s[1]);
        root.Rig.Target = feet;
        Combat.Hero.Instance?.Teleport(feet);
        root.Rig.Pitch = s.Length > 3 ? s[3] : Px.PitchDeg;
        root.Rig.Distance = s.Length > 4 ? s[4] : Px.FocusDistance;
        root.Rig.SnapNext();
        root.DayCycle.Hour = s[2];
        root.DayCycle.Paused = true;
        root.DayCycle.RefreshSkyNow();
        _wait = _settle + (int)(_hold * 60f);
        _frameMs.Clear();
        _physMs.Clear();
        _pressPending = _press.Count > 0;
        _reacted = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_index < 0 || _index >= _shots.Count)
            return;
        if (_react != null && !_reacted)
        {
            _wait = int.MaxValue - 1; // 반응할 때까지 기다린다
            foreach (var e in Combat.Enemy.All)
            {
                if (e.WindupLeft > 0.05f)
                    continue;
                _reacted = true;
                Input.ActionPress(_react);
                if (_react == Controls.Dodge)
                    Input.ActionPress(Controls.Left); // 옆으로 빠진다
                _wait = _after;
                break;
            }
        }
        if (_pressPending)
        {
            _pressPending = false;
            foreach (var action in _press)
                Input.ActionPress(action);
        }
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_last > 0 && _wait < _settle + (int)(_hold * 60f) - 20)
        {
            _frameMs.Add((now - _last) / 1000f);
            _physMs.Add((float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f);
        }
        _last = now;

        if (_index < 0 || _index >= _shots.Count)
            return;
        if (--_wait > 0)
            return;
        CallDeferred(nameof(Shoot));
        _wait = int.MaxValue;
    }

    private void Shoot()
    {
        ReportPerf();
        // 주인공이 찍을 자리에 없으면 알린다 (가끔 광장에 남은 채 찍힌 적이 있다)
        var s = _shots[_index];
        var hero = Combat.Hero.Instance;
        if (hero != null && new Vector2(hero.GlobalPosition.X - s[0], hero.GlobalPosition.Z - s[1]).Length() > 3f)
            GD.PushWarning($"[Capture] shot{_index:D2}: 주인공이 {hero.GlobalPosition} 에 있다 (목표 {s[0]},{s[1]})");
        var img = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/shot_{_index:D2}.png";
        img.SavePng(path);
        GD.Print($"[Capture] {path}");
        Next();
    }

    private void ReportPerf()
    {
        if (_frameMs.Count < 10) return;
        float phys = 0f;
        foreach (float v in _physMs) phys += v;
        _frameMs.Sort();
        GD.Print($"[Perf] shot{_index:D2} 중앙 {_frameMs[_frameMs.Count / 2]:0.0}ms  p90 {_frameMs[(int)(_frameMs.Count * 0.9f)]:0.0}ms  최악 {_frameMs[^1]:0.0}ms"
            + $"  | 물리 평균 {phys / _physMs.Count:0.0}ms 드로우 {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}");
    }
}
