using System;
using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// 개발용 스크린샷 하니스. 커맨드라인에 --capture 가 있을 때만 동작한다.
/// 손맛은 눈으로 확인해야 하므로 마일스톤마다 프레임을 찍어 본다. 게임 로직에는 관여하지 않는다.
///
///   godot --path . -- --capture --capture-frames=180 --capture-every=20 \
///         --capture-hold=move_right,attack --capture-out=user://capture
/// </summary>
public partial class DevCapture : Node
{
    private const string FlagEnable = "--capture";
    private const string ArgFrames = "--capture-frames=";
    private const string ArgEvery = "--capture-every=";
    private const string ArgHold = "--capture-hold=";
    private const string ArgOut = "--capture-out=";
    private const string ArgTapAt = "--capture-tap=";

    private int _totalFrames = 180;
    private int _every = 20;
    private string _outDir = "user://capture";
    private readonly List<string> _hold = new();

    /// <summary>frame → action. 그 프레임에 한 번만 눌렀다 뗀다.</summary>
    private readonly Dictionary<int, string> _taps = new();

    private int _frame;
    private int _shot;

    public static bool IsRequested()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == FlagEnable)
                return true;
        }
        return false;
    }

    public override void _Ready()
    {
        ParseArgs();
        DirAccess.MakeDirRecursiveAbsolute(_outDir);
        GD.Print($"[Capture] frames={_totalFrames} every={_every} hold=[{string.Join(",", _hold)}] out={_outDir}");
    }

    private void ParseArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(ArgFrames, StringComparison.Ordinal))
                _totalFrames = arg.Substring(ArgFrames.Length).ToInt();
            else if (arg.StartsWith(ArgEvery, StringComparison.Ordinal))
                _every = Mathf.Max(1, arg.Substring(ArgEvery.Length).ToInt());
            else if (arg.StartsWith(ArgOut, StringComparison.Ordinal))
                _outDir = arg.Substring(ArgOut.Length);
            else if (arg.StartsWith(ArgHold, StringComparison.Ordinal))
                _hold.AddRange(arg.Substring(ArgHold.Length).Split(',', StringSplitOptions.RemoveEmptyEntries));
            else if (arg.StartsWith(ArgTapAt, StringComparison.Ordinal))
                ParseTaps(arg.Substring(ArgTapAt.Length));
        }
    }

    /// <summary>"30:attack,70:dash" 형식.</summary>
    private void ParseTaps(string spec)
    {
        foreach (string pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split(':');
            if (parts.Length == 2)
                _taps[parts[0].ToInt()] = parts[1];
        }
    }

    public override void _Process(double delta)
    {
        foreach (string action in _hold)
        {
            if (InputMap.HasAction(action))
                Input.ActionPress(action);
        }

        if (_taps.TryGetValue(_frame, out string tap) && InputMap.HasAction(tap))
            Input.ActionPress(tap);
        if (_taps.TryGetValue(_frame - 1, out string prevTap) && InputMap.HasAction(prevTap))
            Input.ActionRelease(prevTap);

        if (_frame > 0 && _frame % _every == 0)
            CallDeferred(nameof(Shoot));

        _frame++;
        if (_frame > _totalFrames)
        {
            GD.Print($"[Capture] done — {_shot} shots");
            GetTree().Quit();
        }
    }

    private void Shoot()
    {
        Image image = GetViewport().GetTexture()?.GetImage();
        if (image == null)
            return;

        string path = $"{_outDir}/f{_frame:D4}.png";
        image.SavePng(path);
        _shot++;

        if (GetTree().GetFirstNodeInGroup(Combat.PlayerCharacter.Group) is Node2D player)
            GD.Print($"[Capture] f{_frame:D4} player={player.GlobalPosition}");
    }
}
