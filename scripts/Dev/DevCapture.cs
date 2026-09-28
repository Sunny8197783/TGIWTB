using System.Collections.Generic;
using System.Globalization;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Dev;

/// <summary>
/// 눈으로 확인하는 캡처 도구. 게임 로직은 건드리지 않는다.
///   godot --path . -- --capture --shot=112,150,9 --shot=112,90,19,20,34 --out=user://shots
/// shot = x,z,시각[,피치,거리]. 찍고 나면 종료한다.
/// </summary>
public partial class DevCapture : Node
{
    private readonly List<float[]> _shots = new();
    private string _out = "user://shots";
    private int _index = -1;
    private int _wait;
    private const int SettleFrames = 24;

    private readonly List<float> _frameMs = new();
    private ulong _last;

    public static System.Collections.Generic.HashSet<string> Disabled()
    {
        var set = new System.Collections.Generic.HashSet<string>();
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--no="))
                foreach (var part in a.Substring(5).Split(','))
                    set.Add(part);
        return set;
    }

    /// <summary>--hold=초 : 각 장면에 이만큼 머물며 프레임 시간을 잰다 (기본은 안정될 때까지만).</summary>
    private float _hold;

    public static bool Requested()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--capture") return true;
        return false;
    }

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
            {
                // 60fps 상한을 풀어 실제 여유를 잰다
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            }
            else if (a.StartsWith("--hold="))
            {
                _hold = float.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--out="))
            {
                _out = a.Substring(6);
            }
        }
        DirAccess.MakeDirRecursiveAbsolute(_out);
        GD.Print($"[Capture] {_shots.Count}장 → {ProjectSettings.GlobalizePath(_out)}");
        Next();
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
        root.Rig.Target = new Vector3(s[0], root.World.HeightAt(s[0], s[1]), s[1]);
        root.Rig.Pitch = s.Length > 3 ? s[3] : Px.PitchDeg;
        root.Rig.Distance = s.Length > 4 ? s[4] : Px.FocusDistance;
        root.Rig.SnapNext();
        root.DayCycle.Hour = s[2];
        root.DayCycle.Paused = true;
        root.DayCycle.RefreshSkyNow();
        _wait = SettleFrames + (int)(_hold * 60f);
        _frameMs.Clear();
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_last > 0 && _wait < SettleFrames + (int)(_hold * 60f) - 20) _frameMs.Add((now - _last) / 1000f);
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
        var img = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/shot_{_index:D2}.png";
        img.SavePng(path);
        GD.Print($"[Capture] {path}");
        Next();
    }

    private void ReportPerf()
    {
        if (_frameMs.Count < 10) return;
        _frameMs.Sort();
        GD.Print($"[Perf] shot{_index:D2} 중앙 {_frameMs[_frameMs.Count / 2]:0.0}ms  p90 {_frameMs[(int)(_frameMs.Count * 0.9f)]:0.0}ms  최악 {_frameMs[^1]:0.0}ms");
    }
}
