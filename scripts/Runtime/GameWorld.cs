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

        _tiles = new TileWorld();
        AddChild(_tiles);

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

        // 세이브가 있으면 이어서, 없거나 깨졌으면 새 게임. 어느 쪽도 크래시하지 않는다. (§H)
        if (SaveSystem.Instance != null && !SaveSystem.Instance.LoadInto(_player))
            _player.ResetToNewGame();

        // 하니스가 시작 좌표를 옮길 수 있으므로 구역 판정보다 먼저 붙인다.
        if (DevCapture.IsRequested())
            AddChild(new DevCapture());

        _camera.Follow(_player);
        _lastZoneId = WorldLayout.ZoneAt(_player.GlobalPosition)?.Id ?? "";

        GD.Print($"[World] ready — spawn={_player.GlobalPosition} zone={_lastZoneId}");
    }

    /// <summary>§G 구역별 몬스터. 고블린·철턱은 M5 에서 붙는다.</summary>
    private void PopulateMonsters()
    {
        _spawner.AddGroup(() => new Slime(), WorldLayout.Meadow,
            MonsterTuning.MeadowSlimeCount, MonsterTuning.MeadowRespawnSeconds);
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
            ShowAnnounce(ok ? "불러왔다." : "불러올 것이 없다.");
            _camera.Follow(_player);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugHitbox))
        {
            DebugFlags.ShowHitbox = !DebugFlags.ShowHitbox;
            ShowAnnounce($"히트박스 {(DebugFlags.ShowHitbox ? "ON" : "OFF")}");
            GetViewport().SetInputAsHandled();
        }
    }
}
