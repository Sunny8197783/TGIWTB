using Godot;
using PixelMmo.Combat;
using PixelMmo.Balance;

namespace PixelMmo.Runtime;

/// <summary>
/// 월드 루트. 씬 트리를 코드로 조립한다 — 아트가 전부 도형이라 .tscn 에 담을 것이
/// 사실상 없고, 수치는 전부 Tuning 클래스에 있다. (§A 아트 방침)
/// </summary>
public partial class GameWorld : Node2D
{
    private static readonly float AnnounceSeconds = 2.0f;

    private TileWorld _tiles;
    private PlayerCharacter _player;
    private GameCamera _camera;
    private CombatFeedback _feedback;
    private MonsterSpawner _spawner;
    private DebugOverlay _overlay;
    private TuningPanel _tuning;
    private AnimationStateMachine _anim;
    private Label _announceLabel;
    private float _announceTimer;

    private string _lastZoneId = "";

    public PlayerCharacter Player => _player;
    public GameCamera Camera => _camera;
    public TileWorld Tiles => _tiles;
    public MonsterSpawner Spawner => _spawner;

    public override void _Ready()
    {
        Name = "World";

        // 이 노드의 자식들을 Y 로 정렬한다. 건물·나무·플레이어·몬스터가 한 줄에
        // 서서, 화면상 아래에 있는 것이 위에 있는 것을 덮는다 —
        // 집 뒤로 걸어 들어가면 집이 플레이어를 가린다.
        YSortEnabled = true;

        _tiles = new TileWorld();
        AddChild(_tiles);

        // 건물 스프라이트는 타일맵이 아니라 여기 직접 붙인다(같은 정렬 묶음).
        if (_tiles.Props != null)
            AddChild(_tiles.Props);

        _player = new PlayerCharacter();
        AddChild(_player);
        _player.Announced += ShowAnnounce;

        // 스킬 사용 1건 = 로그 1줄. 유효/무효와 사유가 전부 남는다. (§I)
        _player.SkillUsed += result => DebugLog.Add(result.ToLogLine());

        _camera = new GameCamera();
        AddChild(_camera);
        _camera.Follow(_player);

        _feedback = new CombatFeedback();
        AddChild(_feedback);
        _feedback.Bind(_camera);

        _spawner = new MonsterSpawner();
        AddChild(_spawner);
        _spawner.Configure(_tiles, _player);
        PopulateMonsters();

        BuildUi();

        // 애니메이션 상태머신 — 플레이어 상태를 읽어 이름 붙은 상태로 분류하고 시그널을 쏜다.
        // 전투 로직은 이걸 모르므로, 이 노드를 지워도 게임은 그대로 돌아간다. (관찰자)
        _anim = new AnimationStateMachine();
        AddChild(_anim);
        _anim.Bind(_player);
        _anim.StateEntered += id => { if (DebugFlags.ShowAnimStates) DebugLog.Add($"[anim] → {id}"); };

        _overlay = new DebugOverlay();
        AddChild(_overlay);
        _overlay.Bind(_player, _player);

        _tuning = new TuningPanel();
        AddChild(_tuning);

        // 세이브가 있으면 이어서, 없거나 깨졌으면 새 게임. 어느 쪽도 크래시하지 않는다. (§H)
        if (SaveSystem.Instance != null && !SaveSystem.Instance.LoadInto(_player))
            _player.ResetToNewGame();

        RescueIfStuck();

        // 하니스가 시작 좌표를 옮길 수 있으므로 구역 판정보다 먼저 붙인다.
        if (DevCapture.IsRequested())
            AddChild(new DevCapture());

        _camera.Follow(_player);
        _lastZoneId = WorldLayout.ZoneAt(_player.GlobalPosition)?.Id ?? "";

        GD.Print($"[World] ready — spawn={_player.GlobalPosition} zone={_lastZoneId}");
    }

    /// <summary>§G 구역별 몬스터 배치.</summary>
    private void PopulateMonsters()
    {
        _spawner.AddGroup(() => new Slime(), WorldLayout.Meadow,
            MonsterTuning.MeadowSlimeCount, MonsterTuning.MeadowRespawnSeconds);

        _spawner.AddGroup(() => new GoblinArcher(), WorldLayout.Meadow,
            MonsterTuning.MeadowGoblinCount, MonsterTuning.MeadowRespawnSeconds);

        _spawner.AddGroup(() => new Ironjaw(), WorldLayout.IronjawDen,
            MonsterTuning.DenIronjawCount, MonsterTuning.DenRespawnSeconds);

        // 마을 훈련용 허수아비. 죽지 않으므로 스포너를 거치지 않고 직접 놓는다.
        // 광장에서 큰길을 따라 남동쪽 훈련장 안에 있다.
        var dummy = new TrainingDummy { GlobalPosition = _tiles.TrainingDummySpot };
        AddChild(dummy);
    }

    /// <summary>
    /// 불러온 좌표가 지금 맵에서 못 서는 자리면 부활 지점으로 되돌린다.
    ///
    /// 맵 형태를 고치면 예전 세이브의 좌표가 벽 속이나 딴 구역이 될 수 있다.
    /// 그때 플레이어가 지형에 갇히는 대신 마을 광장에서 다시 시작하게 한다.
    /// </summary>
    private void RescueIfStuck()
    {
        if (_player == null || _tiles == null)
            return;

        Vector2 at = _player.GlobalPosition;
        if (WorldLayout.WorldBounds.HasPoint(at) && _tiles.IsWalkable(at))
            return;

        _player.GlobalPosition = WorldLayout.SpawnPoint;
        _player.Velocity = Vector2.Zero;
        DebugLog.Add($"세이브 좌표 {at.X:0}/{at.Y:0} 가 지금 맵에서 막힌 자리라 마을로 돌려보냈다.");
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);

        _announceLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _announceLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _announceLabel.OffsetTop = 96;
        _announceLabel.OffsetBottom = 120;
        _announceLabel.AddThemeFontSizeOverride("font_size", 14);
        layer.AddChild(_announceLabel);
    }

    /// <summary>화면 중앙에 한 줄. 조건 설명은 하지 않는다. (CLAUDE.md 규칙 4)</summary>
    public void ShowAnnounce(string message)
    {
        _announceLabel.Text = message;
        _announceTimer = AnnounceSeconds;
    }

    public override void _Process(double delta)
    {
        UpdateAnnounce((float)delta);
        UpdateZone();
        SaveSystem.Instance?.Tick(delta, _player);
    }

    private void UpdateAnnounce(float delta)
    {
        if (_announceTimer <= 0f)
            return;

        _announceTimer -= delta;
        float alpha = Mathf.Clamp(_announceTimer / AnnounceSeconds, 0f, 1f);
        _announceLabel.Modulate = new Color(1f, 1f, 1f, Mathf.Min(1f, alpha * 2f));
    }

    /// <summary>마을(안전지대)에 처음 들어온 순간 자동 저장. (§H)</summary>
    private void UpdateZone()
    {
        var zone = WorldLayout.ZoneAt(_player.GlobalPosition);
        string id = zone?.Id ?? "";
        if (id == _lastZoneId)
            return;

        _lastZoneId = id;
        if (zone != null && zone.Safe)
            SaveSystem.Instance?.Save(_player, $"zone/{zone.Id}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputSetup.QuickSave))
        {
            SaveSystem.Instance?.Save(_player, "F5");
            ShowAnnounce("저장했다.");
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.QuickLoad))
        {
            bool ok = SaveSystem.Instance?.LoadInto(_player) ?? false;
            RescueIfStuck();
            ShowAnnounce(ok ? "불러왔다." : "불러올 것이 없다.");
            _camera.Follow(_player);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugOverlay))
        {
            _overlay.Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugHitbox))
        {
            DebugFlags.ShowHitbox = !DebugFlags.ShowHitbox;
            ShowAnnounce($"히트박스 {(DebugFlags.ShowHitbox ? "ON" : "OFF")}");

            // 껐을 때 마지막에 그린 도형이 남지 않도록 한 번 더 그린다.
            _player.QueueRedraw();
            foreach (Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
            {
                if (node is CanvasItem item)
                    item.QueueRedraw();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugAnim))
        {
            DebugFlags.ShowAnimStates = !DebugFlags.ShowAnimStates;
            ShowAnnounce($"애니 상태 로그 {(DebugFlags.ShowAnimStates ? "ON" : "OFF")}");
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugTuning))
        {
            _tuning.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
}
