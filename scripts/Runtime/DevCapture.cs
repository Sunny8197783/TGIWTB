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
    private const string ArgSeekKeys = "--capture-seek-keys=";
    private const string ArgPulse = "--capture-pulse=";
    private const string ArgSeekRate = "--capture-seek-rate=";
    private const string ArgZoom = "--capture-zoom=";   // 카메라 줌 배율(작을수록 넓게)
    private const string ArgTune = "--capture-tune=";

    /// <summary>로그 트리거가 걸린 뒤 몇 프레임 있다 찍을지. 히트스톱이 끝나는 시점을 노린다.</summary>
    private static readonly int OnLogDelayFrames = 14;

    /// <summary>추적 모드에서 이 거리 안이면 멈추고 때린다.</summary>
    private static readonly float SeekStrikeRange = 22f;

    /// <summary>공격 재입력 간격(프레임). --capture-seek-rate 로 바꾼다.</summary>
    private int _seekAttackInterval = 22;

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
    private static readonly int SeekTeleportFrames = 240;
    private readonly RandomNumberGenerator _rng = new();

    private string _logNeedle;
    private readonly List<int> _scheduledShots = new();
    private readonly List<string> _seekKeys = new() { InputSetup.Attack };
    private string _pulseAction;
    private int _pulseOnFrames;
    private int _pulsePeriod = 1;
    private float _zoom;

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
            else if (arg.StartsWith(ArgPulse, StringComparison.Ordinal))
                ParsePulse(arg.Substring(ArgPulse.Length));
            else if (arg.StartsWith(ArgSeekRate, StringComparison.Ordinal))
                _seekAttackInterval = Mathf.Max(1, arg.Substring(ArgSeekRate.Length).ToInt());
            else if (arg.StartsWith(ArgZoom, StringComparison.Ordinal))
                _zoom = arg.Substring(ArgZoom.Length).ToFloat();
            else if (arg.StartsWith(ArgTune, StringComparison.Ordinal))
                ApplyTune(arg.Substring(ArgTune.Length));
            else if (arg.StartsWith(ArgSeekKeys, StringComparison.Ordinal))
            {
                _seekKeys.Clear();
                _seekKeys.AddRange(arg.Substring(ArgSeekKeys.Length)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    /// <summary>
    /// "dash.cooldown=0.12" — CombatTuning 항목을 커맨드라인에서 덮어쓴다.
    /// F3 슬라이더와 같은 경로(TuningEntry.Value)를 쓰므로, 이게 먹히면 슬라이더도 먹힌다.
    /// </summary>
    private static void ApplyTune(string spec)
    {
        string[] parts = spec.Split('=');
        if (parts.Length != 2)
            return;

        foreach (var entry in Combat.CombatTuning.All)
        {
            if (entry.Key != parts[0])
                continue;

            entry.Value = parts[1].ToFloat();
            GD.Print($"[Capture] tune {entry.Key} = {entry.Value}");
            return;
        }

        GD.PushWarning($"[Capture] 없는 튜닝 키: {parts[0]}");
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
            if (!line.Contains(_logNeedle, StringComparison.Ordinal))
                continue;

            // 프레임 번호를 같이 남긴다 — 공격 간격 같은 타이밍을 숫자로 검증하려면 필요하다.
            GD.Print($"[Capture] f{_frame:D4} << {line}");
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

            // 우회로도 안 되면 초원 임의 지점으로 옮긴다.
            // 장시간 소크 테스트가 기둥에 낀 채로 끝나지 않게 하기 위한 것.
            if (_stuckFrames > SeekTeleportFrames)
            {
                _stuckFrames = 0;
                player.GlobalPosition = RandomMeadowPoint();
                return;
            }

            if (_stuckFrames > SeekStuckFrames)
            {
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

        // 키를 비워 두면 접근만 하고 때리지 않는다 (가드 검증용).
        if (_seekKeys.Count == 0)
            return;

        // 여러 키를 지정하면 번갈아 눌러 콤보를 섞는다.
        int cycle = _frame / _seekAttackInterval;
        string key = _seekKeys[cycle % _seekKeys.Count];

        foreach (string other in _seekKeys)
        {
            if (other != key)
                Input.ActionRelease(other);
        }

        if (_frame % _seekAttackInterval == 0)
            Input.ActionPress(key);
        else
            Input.ActionRelease(key);
    }

    /// <summary>
    /// Input.ActionPress 는 폴링 상태만 바꾸고 _UnhandledInput 까지 가지 않는다.
    /// F1~F3·F5·F9 처럼 이벤트로 받는 키를 누르려면 실제 이벤트를 흘려 넣어야 한다.
    /// </summary>
    private static void SendAction(string action, bool pressed)
    {
        if (pressed)
            Input.ActionPress(action);
        else
            Input.ActionRelease(action);

        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
    }

    private Vector2 RandomMeadowPoint()
    {
        Rect2 rect = WorldLayout.Meadow.WorldRect;
        return new Vector2(
            _rng.RandfRange(rect.Position.X + WorldLayout.TileSize * 3, rect.End.X - WorldLayout.TileSize * 3),
            _rng.RandfRange(rect.Position.Y + WorldLayout.TileSize * 3, rect.End.Y - WorldLayout.TileSize * 3));
    }

    private static void ReleaseMovement()
    {
        Input.ActionRelease(InputSetup.MoveUp);
        Input.ActionRelease(InputSetup.MoveDown);
        Input.ActionRelease(InputSetup.MoveLeft);
        Input.ActionRelease(InputSetup.MoveRight);
    }

    /// <summary>
    /// "guard:8:24" — 24프레임 주기로 8프레임씩 누른다.
    /// 홀드로는 나오지 않는 퍼펙트 가드(누른 직후 0.12s)를 반복해서 만들기 위한 것.
    /// </summary>
    private void ParsePulse(string spec)
    {
        string[] parts = spec.Split(':');
        if (parts.Length != 3 || !InputMap.HasAction(parts[0]))
            return;

        _pulseAction = parts[0];
        _pulseOnFrames = Mathf.Max(1, parts[1].ToInt());
        _pulsePeriod = Mathf.Max(_pulseOnFrames + 1, parts[2].ToInt());
    }

    private void UpdatePulse()
    {
        if (_pulseAction == null)
            return;

        if (_frame % _pulsePeriod < _pulseOnFrames)
            Input.ActionPress(_pulseAction);
        else
            Input.ActionRelease(_pulseAction);
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
        // 맵 전경 캡처용 줌아웃. 매 프레임 덮어써서 카메라의 기본 줌을 이긴다.
        if (_zoom > 0f && GetParent() is GameWorld gw && gw.Camera != null)
            gw.Camera.Zoom = new Vector2(_zoom, _zoom);

        foreach (string action in _hold)
        {
            if (InputMap.HasAction(action))
                Input.ActionPress(action);
        }

        if (_seek)
            SeekAndStrike();

        UpdatePulse();

        if (_taps.TryGetValue(_frame, out string tap) && InputMap.HasAction(tap))
            SendAction(tap, pressed: true);
        if (_taps.TryGetValue(_frame - 1, out string prevTap) && InputMap.HasAction(prevTap))
            SendAction(prevTap, pressed: false);

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
