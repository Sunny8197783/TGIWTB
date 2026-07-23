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
    private const string FlagSeek = "--capture-seek";
    private const string ArgStart = "--capture-start=";
    private const string ArgOnLog = "--capture-on-log=";

    /// <summary>로그 트리거가 걸린 뒤 몇 프레임 있다 찍을지. 히트스톱이 끝나는 시점을 노린다.</summary>
    private static readonly int OnLogDelayFrames = 14;

    /// <summary>추적 모드에서 이 거리 안이면 멈추고 때린다.</summary>
    private static readonly float SeekStrikeRange = 22f;

    /// <summary>공격 재입력 간격(프레임).</summary>
    private static readonly int SeekAttackInterval = 22;

    private int _totalFrames = 180;
    private int _every = 20;
    private string _outDir = "user://capture";
    private readonly List<string> _hold = new();

    /// <summary>frame → action. 그 프레임에 한 번만 눌렀다 뗀다.</summary>
    private readonly Dictionary<int, string> _taps = new();

    private int _frame;
    private int _shot;
    private bool _seek;
    private Vector2? _start;
    private Vector2 _lastSeekPosition;
    private int _stuckFrames;

    private static readonly int SeekStuckFrames = 12;
    private static readonly int SeekDetourFrames = 30;

    private string _logNeedle;
    private readonly List<int> _scheduledShots = new();

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
            else if (arg == FlagSeek)
                _seek = true;
            else if (arg.StartsWith(ArgStart, StringComparison.Ordinal))
                ParseStart(arg.Substring(ArgStart.Length));
            else if (arg.StartsWith(ArgOnLog, StringComparison.Ordinal))
                WatchLog(arg.Substring(ArgOnLog.Length));
        }
    }

    /// <summary>로그에 특정 문자열이 뜨면 잠시 뒤 스크린샷. 처치 연출을 놓치지 않으려는 것.</summary>
    private void WatchLog(string needle)
    {
        _logNeedle = needle;
        DebugLog.Changed += OnLogChanged;
    }

    private void OnLogChanged()
    {
        foreach (string line in DebugLog.Recent(1))
        {
            if (line.Contains(_logNeedle, StringComparison.Ordinal))
                _scheduledShots.Add(_frame + OnLogDelayFrames);
        }
    }

    public override void _ExitTree()
    {
        if (_logNeedle != null)
            DebugLog.Changed -= OnLogChanged;
    }

    /// <summary>"960,544" — 플레이어를 그 좌표에 놓고 시작한다. 추적 모드에 길찾기가 없어서 필요.</summary>
    private void ParseStart(string spec)
    {
        string[] parts = spec.Split(',');
        if (parts.Length != 2)
            return;

        _start = new Vector2(parts[0].ToFloat(), parts[1].ToFloat());
        if (GetTree().GetFirstNodeInGroup(Combat.PlayerCharacter.Group) is Node2D player)
            player.GlobalPosition = _start.Value;
    }

    /// <summary>
    /// 가장 가까운 몬스터로 걸어가서 때린다. 전투 손맛을 실제 화면으로 확인하기 위한 것.
    /// </summary>
    private void SeekAndStrike()
    {
        var player = GetTree().GetFirstNodeInGroup(Combat.PlayerCharacter.Group) as Node2D;
        if (player == null)
            return;

        Node2D nearest = null;
        float best = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(Combat.MonsterBase.Group))
        {
            if (node is not Node2D monster)
                continue;
            float distance = player.GlobalPosition.DistanceTo(monster.GlobalPosition);
            if (distance < best)
            {
                best = distance;
                nearest = monster;
            }
        }

        ReleaseMovement();
        if (nearest == null)
            return;

        Vector2 delta = nearest.GlobalPosition - player.GlobalPosition;

        if (best > SeekStrikeRange)
        {
            // 길찾기가 없어서 기둥에 끼면 못 빠져나온다. 제자리면 잠깐 옆으로 우회한다.
            if (player.GlobalPosition.DistanceTo(_lastSeekPosition) < 0.5f)
                _stuckFrames++;
            else
                _stuckFrames = 0;
            _lastSeekPosition = player.GlobalPosition;

            if (_stuckFrames > SeekStuckFrames)
            {
                if (_stuckFrames > SeekStuckFrames + SeekDetourFrames)
                    _stuckFrames = 0;
                Input.ActionPress(delta.Y > 0f ? InputSetup.MoveLeft : InputSetup.MoveRight);
                Input.ActionPress(delta.X > 0f ? InputSetup.MoveDown : InputSetup.MoveUp);
                return;
            }

            if (delta.X > 4f) Input.ActionPress(InputSetup.MoveRight);
            else if (delta.X < -4f) Input.ActionPress(InputSetup.MoveLeft);
            if (delta.Y > 4f) Input.ActionPress(InputSetup.MoveDown);
            else if (delta.Y < -4f) Input.ActionPress(InputSetup.MoveUp);
            return;
        }

        _stuckFrames = 0;

        if (_frame % SeekAttackInterval == 0)
            Input.ActionPress(InputSetup.Attack);
        else
            Input.ActionRelease(InputSetup.Attack);
    }

    private static void ReleaseMovement()
    {
        Input.ActionRelease(InputSetup.MoveUp);
        Input.ActionRelease(InputSetup.MoveDown);
        Input.ActionRelease(InputSetup.MoveLeft);
        Input.ActionRelease(InputSetup.MoveRight);
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

        if (_seek)
            SeekAndStrike();

        if (_taps.TryGetValue(_frame, out string tap) && InputMap.HasAction(tap))
            Input.ActionPress(tap);
        if (_taps.TryGetValue(_frame - 1, out string prevTap) && InputMap.HasAction(prevTap))
            Input.ActionRelease(prevTap);

        bool scheduled = _scheduledShots.Remove(_frame);
        if (scheduled || (_frame > 0 && _frame % _every == 0))
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
