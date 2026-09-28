using Godot;
using PixelMmo.Combat;
using PixelMmo.Balance;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>
/// 월드 루트. 씬 트리를 코드로 조립한다 — 아트가 전부 도형이라 .tscn 에 담을 것이
/// 사실상 없고, 수치는 전부 Tuning 클래스에 있다. (§A 아트 방침)
/// </summary>
public partial class GameWorld : Node2D
{
    private static readonly float AnnounceSeconds = 2.0f;

    /// <summary>NPC 를 세울 자리를 못 찾을 때 몇 칸까지 넓혀 볼지.</summary>
    private const int NpcSearchRings = 8;

    /// <summary>F8 테스트장 고블린이 죽고 나서 다시 서기까지.</summary>
    private const float TestArenaRespawnSeconds = 10f;

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
    private bool _testArenaGoblinSpawned;
    private bool _inTestArena;
    private Vector2 _preTestArenaPosition;

    private PanelContainer _dialogueBox;
    private Label _dialogueLabel;
    private Label _dialogueName;
    private Npc _talking;

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
        PlaceNpcs();

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
        if (ReferenceWorld3D.CheckRequested || (HuntCheck.Requested && !HuntCheck.Resume) || (DevCapture.IsRequested() && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--capture-fresh") >= 0) || (SaveSystem.Instance != null && !SaveSystem.Instance.LoadInto(_player)))
            _player.ResetToNewGame();

        RescueIfStuck();

        // 하니스가 시작 좌표를 옮길 수 있으므로 구역 판정보다 먼저 붙인다.
        if (DevCapture.IsRequested())
            AddChild(new DevCapture());

        _camera.Follow(_player);
        _lastZoneId = WorldLayout.ZoneAt(_player.GlobalPosition)?.Id ?? "";

        GD.Print($"[World] ready — spawn={_player.GlobalPosition} zone={_lastZoneId}");
        if (ReferenceWorld3D.Enabled)
            AddChild(new ReferenceWorld3D { World = this });
        if (HuntCheck.Requested) AddChild(new HuntCheck());
        if (!ReferenceWorld3D.CheckRequested && (!DevCapture.IsRequested() || System.Array.IndexOf(OS.GetCmdlineUserArgs(),"--creator-preview")>=0)
            && (!((IPlayerContext)_player).Appearance.Confirmed || System.Array.IndexOf(OS.GetCmdlineUserArgs(),"--creator-preview")>=0))
            Callable.From(()=>CharacterCreator.Open(this,_player)).CallDeferred();
    }

    /// <summary>§G 구역별 몬스터 배치.</summary>
    private void PopulateMonsters()
    {
        // 초원과 굴은 손으로 맞춰 둔 편성을 그대로 둔다 — 튜토리얼 구간과
        // 보스방이라 자동 편성이 덮어쓰면 곤란하다.
        AddChild(new MeadowEncounters());

        _spawner.AddGroup(() => new Ironjaw(), WorldLayout.IronjawDen,
            MonsterTuning.DenIronjawCount, MonsterTuning.DenRespawnSeconds);

        // Encounter clearings are authored; no random monsters inside the village.
        GD.Print($"[World] 몬스터 자리 {_spawner.SlotCount}개");

        // 마을 훈련용 허수아비. 죽지 않으므로 스포너를 거치지 않고 직접 놓는다.
        // 광장에서 큰길을 따라 남동쪽 훈련장 안에 있다.
        var dummy = new TrainingDummy { GlobalPosition = _tiles.TrainingDummySpot };
        AddChild(dummy);
    }

    /// <summary>
    /// 나머지 구역은 위험도와 넓이로 자동 편성한다.
    ///
    /// 구역이 열세 개다. 하나씩 손으로 적으면 구역을 추가할 때마다 여기를
    /// 같이 고쳐야 하고, 빠뜨린 구역은 조용히 텅 빈 채로 남는다.
    /// </summary>
    private void PopulateByDanger()
    {
        foreach (var zone in WorldLayout.Zones)
        {
            if (zone.Safe || zone.Danger <= 0)
                continue;
            if (zone == WorldLayout.Meadow || zone == WorldLayout.IronjawDen)
                continue;

            int area = zone.Tiles.Size.X * zone.Tiles.Size.Y;
            int total = Mathf.Clamp(area / MonsterTuning.TilesPerMonster,
                1, MonsterTuning.ZoneMonsterCap);

            // 위험도가 3 이상인 곳에만 철턱이 나온다. 나머지는 고블린과 슬라임.
            int ironjaw = zone.Danger >= 3 ? Mathf.Min(zone.Danger - 2, total) : 0;
            int goblin = Mathf.Min((total - ironjaw) * zone.Danger / 4 + 1, total - ironjaw);
            int slime = total - ironjaw - goblin;

            if (slime > 0)
                _spawner.AddGroup(() => new Slime(), zone, slime, MonsterTuning.MeadowRespawnSeconds);
            if (goblin > 0)
                _spawner.AddGroup(() => new GoblinArcher(), zone, goblin, MonsterTuning.MeadowRespawnSeconds);
            if (ironjaw > 0)
                _spawner.AddGroup(() => new Ironjaw(), zone, ironjaw, MonsterTuning.DenRespawnSeconds);
        }
    }

    /// <summary>data/npcs 의 NPC 를 좌표대로 세운다. 막힌 칸이면 근처를 뒤진다.</summary>
    private void PlaceNpcs()
    {
        var db = GameDatabase.Instance;
        if (db == null)
            return;

        int placed = 0;
        float drift = 0f;

        // 두 사람이 같은 시설 앞에 배정되면 겹쳐 선다. 이미 누가 선 칸은 피한다.
        var occupied = new System.Collections.Generic.HashSet<Vector2I>();

        foreach (var def in db.Npcs.Values)
        {
            var zone = WorldLayout.ZoneById(def.Zone);
            if (zone == null) continue;
            Vector2 at = AnchorSpot(def);
            Vector2 wanted = at;

            if (zone != null && !zone.Contains(at))
            {
                GD.PushWarning($"[World] NPC {def.Id} 좌표가 구역 {def.Zone} 밖이다 — 건너뛴다.");
                continue;
            }

            if (!FindStandingSpot(ref at, occupied))
            {
                GD.PushWarning($"[World] NPC {def.Id} 를 세울 자리를 못 찾았다 — 건너뛴다.");
                continue;
            }

            occupied.Add((Vector2I)(at / WorldLayout.TileSize).Floor());
            drift = Mathf.Max(drift, at.DistanceTo(wanted) / WorldLayout.TileSize);

            var npc = new Npc { GlobalPosition = at };
            npc.Bind(def);
            AddChild(npc);
            placed++;
        }

        // 이탈이 크면 그 NPC 는 자기 건물 앞에 서 있지 않다는 뜻이다.
        GD.Print($"[World] NPC {placed}/{db.Npcs.Count}명 배치 (앵커 최대 이탈 {drift:0.0}칸)");
    }

    /// <summary>
    /// anchor 가 있으면 그 시설 정면(아래쪽) 한 칸 앞. 없으면 json 의 타일 좌표.
    /// 건물은 길을 향해 문을 내므로 아래가 곧 그 집 앞이다. (TownGenerator 주석)
    /// </summary>
    private Vector2 AnchorSpot(NpcDefinition def)
    {
        if (_tiles.TryGetLandmark(def.Anchor, out var lm))
        {
            return WorldLayout.TileCenter(
                lm.Position.X + lm.Size.X / 2, lm.Position.Y + lm.Size.Y);
        }

        if (!string.IsNullOrEmpty(def.Anchor))
            GD.PushWarning($"[World] NPC {def.Id} 의 anchor '{def.Anchor}' 를 못 찾았다 — 좌표로 세운다.");

        return WorldLayout.TileCenter(def.TileX, def.TileY);
    }

    /// <summary>
    /// 정확한 칸이 막혀 있으면 나선으로 넓혀 가며 설 수 있는 칸을 찾는다.
    /// 마을 건물은 절차적으로 앉으므로 손으로 적은 좌표가 집 안에 떨어질 수 있다.
    /// </summary>
    internal bool FindStandingSpot(ref Vector2 at, System.Collections.Generic.HashSet<Vector2I> occupied)
    {
        if (_tiles.IsWalkable(at) && !occupied.Contains((Vector2I)(at / WorldLayout.TileSize).Floor()))
            return true;

        for (int ring = 1; ring <= NpcSearchRings; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    // 테두리만 본다 — 안쪽은 이전 링에서 이미 봤다.
                    if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring)
                        continue;

                    Vector2 probe = at + new Vector2(dx, dy) * WorldLayout.TileSize;
                    if (_tiles.IsWalkable(probe)
                        && !occupied.Contains((Vector2I)(probe / WorldLayout.TileSize).Floor()))
                    {
                        at = probe;
                        return true;
                    }
                }
            }
        }

        return false;
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
        if (WorldLayout.ZoneAt(at) != null && _tiles.IsWalkable(at))
            return;

        // 광장 한가운데는 분수가 차지하고 있다. 부활 지점 자체가 막힌 칸이므로
        // 좌표를 그냥 되돌리면 분수 안에 선다 — 근처의 설 수 있는 칸을 찾는다.
        Vector2 spawn = WorldLayout.SpawnPoint;
        FindStandingSpot(ref spawn, new System.Collections.Generic.HashSet<Vector2I>());

        _player.GlobalPosition = spawn;
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
        _announceLabel.OffsetTop = 32;
        _announceLabel.OffsetBottom = 68;
        _announceLabel.AddThemeFontSizeOverride("font_size", 21);
        _announceLabel.AddThemeColorOverride("font_color",RpgUi.Paper);
        _announceLabel.AddThemeColorOverride("font_shadow_color",RpgUi.Ink);
        _announceLabel.AddThemeConstantOverride("shadow_offset_x",2);
        _announceLabel.AddThemeConstantOverride("shadow_offset_y",2);
        layer.AddChild(_announceLabel);

        BuildDialogueBox(layer);

        var hud = new PlayerHud();
        layer.AddChild(hud);
        hud.Bind(_player, _tiles);
    }

    /// <summary>화면 아래 대사창. 말을 걸 때만 보인다.</summary>
    private void BuildDialogueBox(CanvasLayer layer)
    {
        _dialogueBox = new PanelContainer { Name="Dialogue", Visible=false };
        _dialogueBox.AddThemeStyleboxOverride("panel",RpgUi.Frame());
        _dialogueBox.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _dialogueBox.OffsetTop=-302; _dialogueBox.OffsetBottom=-122;
        _dialogueBox.OffsetLeft=40; _dialogueBox.OffsetRight=-40;
        var column=new VBoxContainer(); column.AddThemeConstantOverride("separation",10); _dialogueBox.AddChild(column);
        _dialogueName=RpgUi.Text("",21,RpgUi.Gold); column.AddChild(_dialogueName);
        _dialogueLabel=RpgUi.Text("",18); _dialogueLabel.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _dialogueLabel.SizeFlagsVertical=Control.SizeFlags.ExpandFill; column.AddChild(_dialogueLabel);
        var buttons=new HBoxContainer(); column.AddChild(buttons);
        var hint=RpgUi.Text("대화",12,RpgUi.Muted); hint.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; buttons.AddChild(hint);
        var next=RpgUi.Button("계속 듣기  F"); next.Pressed+=Interact; buttons.AddChild(next);
        var close=RpgUi.Button("대화 마치기"); close.Pressed+=CancelTalk; buttons.AddChild(close);
        layer.AddChild(_dialogueBox);
    }

    private void CancelTalk()
    {
        _talking=null; _dialogueBox.Visible=false;
        ((IPlayerContext)_player).InputBlocked=PlayerHud.Instance?.MenuOpen ?? false;
    }

    /// <summary>
    /// F 한 번 = 대사 한 줄. 대화 중이 아니면 가까운 NPC 를 찾아 시작한다.
    /// 대사가 끝나면 창이 닫히고, 전직 NPC 라면 그때 전직을 시도한다.
    /// </summary>
    private void Interact()
    {
        if (_talking == null)
        {
            _talking = Npc.Nearest(this, _player.GlobalPosition);
            if (_talking == null)
                return;
            _talking.Rewind();
        }

        string line = _talking.NextLine();
        if (line != null)
        {
            _dialogueName.Text = "◆  " + _talking.Def.Name;
            _dialogueLabel.Text = line;
            ((IPlayerContext)_player).InputBlocked = true;
            _dialogueBox.Visible = true;
            return;
        }

        FinishTalk();
    }

    /// <summary>
    /// 걸어서 멀어지면 대사창을 닫는다. 전직 판정은 하지 않는다 —
    /// 끝까지 듣지 않고 떠난 것이므로 F 를 끝까지 누른 것과 같게 취급하면 안 된다.
    /// </summary>
    private void UpdateTalk()
    {
        if (_talking == null)
            return;

        if (Npc.Nearest(this, _player.GlobalPosition) == _talking)
            return;

        _talking = null;
        _dialogueBox.Visible = false;
    }

    /// <summary>대화 종료. 전직 NPC 면 여기서 한 번만 판정한다.</summary>
    private void FinishTalk()
    {
        var npc = _talking;
        _talking = null;
        _dialogueBox.Visible = false;

        if (npc?.Def?.Restores == true)
        {
            _player.RestoreFull();
            ShowAnnounce("숨을 돌렸다.");
        }

        string jobId = npc?.Def?.TeachesJob;
        if (string.IsNullOrEmpty(jobId))
            return;

        var db = GameDatabase.Instance;
        var def = db?.GetJob(jobId);
        if (def == null)
            return;

        if (!_player.Jobs.CanStart(def, _player))
        {
            // 왜 안 되는지는 말하지 않는다. (CLAUDE.md 규칙 4)
            if (!string.IsNullOrEmpty(npc.Def.RefuseLine))
                ShowAnnounce(npc.Def.RefuseLine);
            return;
        }

        _player.Jobs.StartJob(jobId, db);
        ShowAnnounce(string.IsNullOrEmpty(npc.Def.AcceptLine)
            ? $"{def.Name}(이)가 되었다."
            : npc.Def.AcceptLine);
        SaveSystem.Instance?.Save(_player, $"job/{jobId}");
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
        UpdateTalk();
        var hovered=GetViewport().GuiGetHoveredControl();
        ((IPlayerContext)_player).InputBlocked=CharacterCreator.IsOpen || (_dialogueBox?.Visible ?? false) || (PlayerHud.Instance?.MenuOpen ?? false) || hovered is Button;
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
        if (zone == null)
            return;

        // 구역 이름을 띄운다. 성문을 나서는 순간이 안전지대의 끝이라는 것을
        // 몸으로 알려 주는 신호가 지금까지 아무 것도 없었다.
        ShowAnnounce(zone.Safe ? zone.DisplayName : $"{zone.DisplayName} — 위험도 {zone.Danger}");

        if (zone.Safe)
            SaveSystem.Instance?.Save(_player, $"zone/{zone.Id}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (CharacterCreator.IsOpen) return;
        if (@event.IsActionPressed(InputSetup.Customize) && !@event.IsEcho() && !_dialogueBox.Visible)
        { CharacterCreator.Open(this,_player); GetViewport().SetInputAsHandled(); return; }
        if (PlayerHud.Instance?.MenuOpen == true) return;
        if (_dialogueBox.Visible && @event is InputEventKey key && key.Pressed && key.PhysicalKeycode==Key.Escape)
        { CancelTalk(); GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed(InputSetup.Interact))
        {
            Interact();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.QuickSave))
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
        else if (@event.IsActionPressed(InputSetup.DebugTestArena))
        {
            EnterTestArena();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.DebugTestArenaExit))
        {
            ExitTestArena();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// F8 — 손맛 테스트용. 지도 밖 별도 방(WorldLayout.TestArenaTiles)으로 순간이동 + 무적,
    /// 근접 몬스터 한 마리가 계속 그 자리를 지킨다(처치 10초 후 같은 자리에 재소환). 실제 구역
    /// 스폰과는 별개의 고정 슬롯이라 한 번만 만들고, 다시 눌러도 그 자리로 되돌아갈 뿐 슬롯이 늘지 않는다.
    /// </summary>
    private void EnterTestArena()
    {
        if (_player == null || _tiles == null)
            return;

        // 처음 들어갈 때만(또는 이미 나가 있는 상태에서만) 원래 있던 자리를 기억해 둔다 —
        // 테스트장 안에서 F8 을 다시 눌러도 복귀 지점이 테스트장 자체로 덮어써지지 않게.
        if (!_inTestArena)
            _preTestArenaPosition = _player.GlobalPosition;

        var empty = new System.Collections.Generic.HashSet<Vector2I>();

        Vector2 arenaPoint = WorldLayout.TestArenaCenter;
        FindStandingSpot(ref arenaPoint, empty);
        _player.GlobalPosition = arenaPoint;
        _player.Velocity = Vector2.Zero;
        _player.DebugInvulnerable = true;
        _inTestArena = true;
        _camera.ClampToWorld = false;
        _camera.Follow(_player);

        if (!_testArenaGoblinSpawned)
        {
            Vector2 monsterPoint = arenaPoint + new Vector2(2 * WorldLayout.TileSize, 0f);
            FindStandingSpot(ref monsterPoint, empty);
            _spawner.AddFixedSlot(() => new Slime(), monsterPoint, TestArenaRespawnSeconds);
            _testArenaGoblinSpawned = true;
        }

        ShowAnnounce("테스트장 — 무적 ON, 몬스터 무한 리스폰(10초).");
    }

    /// <summary>F7 — 테스트장에서 나간다. 무적을 끄고 들어오기 전 자리로 되돌린다.</summary>
    private void ExitTestArena()
    {
        if (_player == null || !_inTestArena)
            return;

        _player.GlobalPosition = _preTestArenaPosition;
        _player.Velocity = Vector2.Zero;
        _player.DebugInvulnerable = false;
        _inTestArena = false;
        _camera.ClampToWorld = true;
        _camera.Follow(_player);

        ShowAnnounce("테스트장 나감 — 무적 OFF.");
    }
}
