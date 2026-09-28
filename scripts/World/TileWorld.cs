using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// 16px 타일맵을 코드로 만든다. 스프라이트를 그리지 않는다 — 타일 텍스처는
/// 단색 사각형을 런타임에 찍어서 만든 플레이스홀더다. (CLAUDE.md 규칙 5)
/// </summary>
public partial class TileWorld : TileMapLayer
{
    // 아틀라스 타일 인덱스 (x 좌표) — 구역과 무관하게 공통으로 쓰는 것들.
    private const int TileWall = 0;

    // 건물(집) — 지붕색만 다른 플레이스홀더. 전부 충돌한다(못 지나감). (규칙 5)
    private const int TileHouseA = 1;
    private const int TileHouseB = 2;
    private const int TileHouseC = 3;

    // 강(물) — 못 지나감. 다리 — 건널 수 있음.
    private const int TileWaterA = 4;
    private const int TileWaterB = 5;
    private const int TileBridge = 6;

    // 마을 길(흙)과 광장(돌), 훈련장 울타리.
    private const int TileDirtA = 7;
    private const int TileDirtB = 8;
    private const int TileStoneA = 9;
    private const int TileStoneB = 10;
    private const int TileFence = 11;

    /// <summary>
    /// 마을을 두르는 돌 성벽. 한 색으로 3칸을 칠하면 회색 띠일 뿐이라
    /// 위(볕 드는 흉벽) · 몸통 · 아래(그늘) 세 단으로 나눠 두께를 보이게 한다.
    /// </summary>
    private const int TileTownWallTop = 12;
    private const int TileTownWall = 13;
    private const int TileTownWallBase = 14;

    /// <summary>
    /// 여기서부터는 구역마다 세 장씩 — 바닥 A / 바닥 B / 그 구역의 바위.
    ///
    /// 예전에는 마을·초원·굴 여섯 장을 손으로 적어 뒀는데, 구역이 열셋이 되면서
    /// 그 방식으로는 상수만 마흔 개가 된다. 구역표를 곧 타일표로 쓰면 구역을
    /// 하나 더 넣어도 여기는 손댈 데가 없다.
    /// </summary>
    private const int ZoneTileStart = 15;
    /// <summary>
    /// 구역 한 곳이 쓰는 타일 수 — 바닥 A/B + 암반 A/B.
    ///
    /// 암반이 한 장이었다. 바위 덩어리는 2~4칸씩 붙어 깔리는데 한 장뿐이면
    /// 같은 결이 32px 마다 되풀이돼 격자가 보인다. 두 장을 좌표 해시로 섞는다.
    /// </summary>
    private const int TilesPerZone = 4;

    private static int ZoneFloorA(int index) => ZoneTileStart + index * TilesPerZone;
    private static int ZoneFloorB(int index) => ZoneFloorA(index) + 1;
    private static int ZoneSolid(int index) => ZoneFloorA(index) + 2;
    private static int ZoneSolidB(int index) => ZoneFloorA(index) + 3;

    private static readonly int TileCount
        = ZoneTileStart + WorldLayout.Zones.Length * TilesPerZone;

    /// <summary>마을은 구역표의 첫 칸이다. 마을 바닥 = 잔디.</summary>
    private static readonly int TileGrassA = ZoneFloorA(0);
    private static readonly int TileGrassB = ZoneFloorB(0);
    internal (int Source, Vector2I Atlas) GrassTile => _grassDirt != null
        ? (_grassDirt.SourceId, _grassDirt.Atlas(0)) : (SourceId, new Vector2I(TileGrassA,0));

    /// <summary>벽·건물·물·울타리·구역 바위면 막힌 칸. 다리(TileBridge)는 통행 가능.</summary>
    private static bool IsSolid(int tileX)
    {
        if (tileX >= ZoneTileStart)
            return (tileX - ZoneTileStart) % TilesPerZone >= 2;

        return tileX == TileWall
            || (tileX >= TileHouseA && tileX <= TileHouseC)
            || tileX == TileWaterA || tileX == TileWaterB
            || tileX == TileFence
            || tileX == TileTownWallTop || tileX == TileTownWall || tileX == TileTownWallBase;
    }

    private const int SourceId = 0;
    private const int PhysicsLayer = 0;

    // --- 마을 바닥의 실제 픽셀아트 (PixelLab Wang 타일셋) ---
    //
    // 도형 타일로 먼저 칠해 놓은 마을 바닥을, 같은 자리에 코너 지형으로 다시 칠한다.
    // 에셋이 없으면 이 단계를 건너뛰고 도형 그대로 둔다 — 게임은 그대로 돌아간다.
    /// <summary>바닥 지형. 숫자가 클수록 위에 얹히는 것 — 정점 판정에서 큰 쪽이 이긴다.</summary>
    private const int TerrainNone = -1;
    private const int TerrainGrass = 0;
    private const int TerrainDirt = 1;
    private const int TerrainStone = 2;
    private const int TerrainWater = 3;

    private const int GrassDirtSourceId = 1;
    private const int DirtStoneSourceId = 2;
    private const int GrassStoneSourceId = 3;
    private const int GrassWaterSourceId = 4;

    /// <summary>성벽. 바닥이 아니라 '높이가 있는 지형'이라 따로 칠하고 따로 막는다.</summary>
    private const int TownWallSourceId = 5;

    private const string TilesRoot = "res://art/tiles32";

    private WangTileset _grassDirt;
    private WangTileset _dirtStone;
    private WangTileset _grassStone;
    private WangTileset _grassWater;
    private WangTileset _townWall;
    private bool _terrainReady;

    /// <summary>집·우물 스프라이트가 있는가. 없으면 예전 도형 블록으로 간다.</summary>
    private bool _propsReady;

    private VillageProps _props;
    private TownGenerator _town;

    /// <summary>
    /// 건물·나무 스프라이트 묶음. 타일맵의 자식이 아니라 **월드의 자식**이어야 한다 —
    /// 플레이어와 같은 Y 정렬 묶음에 들어가야 건물 뒤로 지나갈 때 가려진다.
    /// GameWorld 가 _Ready 직후에 받아 간다.
    /// </summary>
    public VillageProps Props => _props;
    internal TownGenerator Town => _town;

    /// <summary>훈련장 허수아비 자리. GameWorld 가 개체를 놓을 때 쓴다.</summary>
    public Vector2 TrainingDummySpot => _town?.TrainingDummySpot ?? WorldLayout.SpawnPoint;

    /// <summary>
    /// 이름 붙은 시설이 앉은 자리(타일). NPC 를 그 앞에 세울 때 쓴다.
    /// 키는 TownGenerator.Landmarks 참고 — "smithy", "chapel", "gate_east", "plaza" 등.
    /// </summary>
    public bool TryGetLandmark(string key, out Rect2I tiles)
    {
        tiles = default;
        return _town != null && !string.IsNullOrEmpty(key)
            && _town.Landmarks.TryGetValue(key, out tiles);
    }

    /// <summary>
    /// 같은 구역 안에서 두 장을 섞어 밝기를 살짝 흔든다 — 걸을 때 움직임이 보이게.
    ///
    /// 0.06 이었다. (x+y)%2 로 딱 맞춰 깔리니 그 차이가 그대로 바둑판 무늬가 됐고,
    /// 사냥터 화면 전체가 32px 격자로 읽혔다. 배치를 좌표 해시로 흩은 뒤에는
    /// 두 장이 붙어 깔리는 경우가 생기므로 차이를 더 줄여야 이음매가 안 보인다.
    /// </summary>
    private static readonly float CheckerShade = 0.012f;

    // 초원 안의 것들은 초원 왼쪽 끝을 기준으로 잡는다.
    // 예전에는 절대 좌표라, 마을을 넓혀 초원을 오른쪽으로 밀었을 때 강과 바위가
    // 마을 한복판에 남아 있었다. 구역이 움직이면 같이 움직여야 한다.
    private static Rect2I MeadowTiles => WorldLayout.Meadow.Tiles;
    private static int MeadowX(float frac)
        => MeadowTiles.Position.X + (int)(MeadowTiles.Size.X * frac);
    private static int MeadowY(float frac)
        => MeadowTiles.Position.Y + (int)(MeadowTiles.Size.Y * frac);

    /// <summary>초원을 세로로 가르는 강(물). 못 지나감. 구역 높이 전체를 덮는다.</summary>
    private static Rect2I MeadowRiver
        => new(MeadowX(0.42f), MeadowTiles.Position.Y, 4, MeadowTiles.Size.Y);

    /// <summary>강을 건너는 다리 — 마을에서 나오는 통로 높이에 맞춘다.</summary>
    private static Rect2I MeadowBridge
    {
        get
        {
            int centerY = WorldLayout.Town.Tiles.Position.Y + WorldLayout.Town.Tiles.Size.Y / 2;
            return new Rect2I(MeadowX(0.42f), centerY - WorldLayout.GateHeight / 2,
                4, WorldLayout.GateHeight);
        }
    }

    public override void _Ready()
    {
        Name = "TileWorld";
        // 실제 픽셀아트 타일이 들어오므로 확대할 때 뭉개지면 안 된다.
        TextureFilter = TextureFilterEnum.Nearest;

        // 바닥은 언제나 맨 아래. Y 정렬은 z 가 같은 것끼리만 겨루므로,
        // 바닥을 따로 떼어 놓아야 건물·플레이어 정렬에 끼어들지 않는다.
        ZIndex = -10;

        // 마을을 만든다. 188x188 에 건물이 수백 채라 손으로 놓을 수 없다.
        _town = new TownGenerator(WorldLayout.Town.Tiles);
        _town.Generate();

        _propsReady = VillageProps.AssetsPresent();

        TileSet = BuildTileSet();
        Paint();

        // 여기서 만들기만 하고 트리에 붙이지는 않는다 — GameWorld 가 자기 자식으로
        // 받아 가야 플레이어와 같은 Y 정렬 묶음에 들어간다.
        if (_propsReady)
        {
            var props = new System.Collections.Generic.List<PropPlacement>(_town.Props);
            props.AddRange(ForestProps());
            if (ReferenceWorld3D.Enabled)
                props.RemoveAll(p => ReferenceTerrain3D.CoastalWater(new Vector2(
                    (p.Tiles.Position.X + p.Tiles.Size.X * .5f) * WorldLayout.TileSize,
                    (p.Tiles.Position.Y + p.Tiles.Size.Y) * WorldLayout.TileSize)));
            _props = new VillageProps(props);
            if (!ReferenceWorld3D.Enabled) _props.Ready += ReportBlockedRoads;
        }

        Minimap = BuildMinimap();
        var bounds=WorldLayout.IronjawDen.Tiles.Grow(2);
        using var interior=Image.CreateEmpty(bounds.Size.X,bounds.Size.Y,false,Image.Format.Rgba8);
        for(int y=0;y<bounds.Size.Y;y++) for(int x=0;x<bounds.Size.X;x++)
        {
            var cell=bounds.Position+new Vector2I(x,y);
            int source=GetCellSourceId(cell);
            bool wall=source==0 && IsSolid(GetCellAtlasCoords(cell).X);
            interior.SetPixel(x,y,source<0 ? new Color(.04f,.06f,.05f) : wall ? new Color(.32f,.4f,.34f) : new Color(.55f,.59f,.51f));
        }
        SanctuaryMinimap=ImageTexture.CreateFromImage(interior);
    }

    /// <summary>
    /// 미니맵 텍스처. 한 타일 = 한 픽셀이라 384x224 밖에 안 된다.
    ///
    /// 맵이 다섯 배가 되면서 "여기가 어디인지"를 알 방법이 화면에 아무 것도 없어졌다.
    /// 구역 모양과 길만 보이면 충분하다 — 소품 하나하나까지 찍을 이유는 없다.
    /// </summary>
    public Texture2D Minimap { get; private set; }
    public Texture2D SanctuaryMinimap { get; private set; }

    private Texture2D BuildMinimap()
    {
        var image = Image.CreateEmpty(WorldLayout.WidthTiles, WorldLayout.HeightTiles,
            false, Image.Format.Rgba8);

        var wall = new Color(0.07f, 0.07f, 0.09f);
        var water = new Color(0.16f, 0.28f, 0.44f);
        var road = new Color(0.52f, 0.45f, 0.36f);

        for (int y = 0; y < WorldLayout.HeightTiles; y++)
        {
            for (int x = 0; x < WorldLayout.WidthTiles; x++)
            {
                var zone = WorldLayout.ZoneAt(WorldLayout.TileCenter(x, y));
                if (zone == null)
                {
                    image.SetPixel(x, y, wall);
                    continue;
                }

                int source = GetCellSourceId(new Vector2I(x, y));
                Color c;

                Vector2 point = WorldLayout.TileCenter(x, y);
                if (GetCellSourceId(new Vector2I(x, y)) < 0) { image.SetPixel(x,y,wall); continue; }
        int tile = GetCellAtlasCoords(new Vector2I(x, y)).X;
                if ((ReferenceWorld3D.Enabled && ReferenceTerrain3D.CoastalWater(point))
                    || source == GrassWaterSourceId || (source == SourceId && (tile == TileWaterA || tile == TileWaterB)))
                    c = water;
                else if (ReferenceWorld3D.Enabled && ReferenceTerrain3D.CoastalSand(point))
                    c = new Color(.78f, .7f, .5f);
                else if (source == TownWallSourceId)
                    c = wall;
                else if (_town != null && _town.IsRoadCell(x-WorldLayout.Town.Tiles.Position.X, y-WorldLayout.Town.Tiles.Position.Y))
                    c = road;
                else if (source == SourceId && (tile == TileBridge || tile == TileDirtA || tile == TileDirtB))
                    c = road;
                else if (source == SourceId && IsSolid(GetCellAtlasCoords(new Vector2I(x, y)).X))
                    c = zone.SolidColor;
                else
                    c = WorldLayout.Meadow.FloorColor;

                // 바닥색 그대로 쓰면 미니맵이 전부 어두운 흙빛이 된다. 한 번 띄운다.
                image.SetPixel(x, y, new Color(c.R * 1.35f, c.G * 1.35f, c.B * 1.35f));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// 길 위인데 막혀 있는 칸을 센다.
    ///
    /// "건물에 닿지도 않았는데 막힌다"는 건 눈으로는 잘 안 보인다 — 걸어 보다
    /// 우연히 걸려야 안다. 건물 충돌은 그림에서 뽑으므로 캔버스가 크거나
    /// 그림이 캔버스 밖으로 삐져나오면 길을 덮을 수 있다. 그러면 여기 숫자가 뛴다.
    /// </summary>
    internal void ReportBlockedRoads()
    {
        Rect2I town = WorldLayout.Town.Tiles;
        int roads = 0, blocked = 0;
        var worst = new Vector2I(-1, -1);

        for (int y = 0; y < town.Size.Y; y++)
        {
            for (int x = 0; x < town.Size.X; x++)
            {
                if (!_town.IsRoadCell(x, y))
                    continue;
                roads++;

                if (!IsWalkable(WorldLayout.TileCenter(town.Position.X + x, town.Position.Y + y)))
                {
                    blocked++;
                    if (worst.X < 0)
                        worst = new Vector2I(town.Position.X + x, town.Position.Y + y);
                }
            }
        }

        string where = worst.X < 0 ? "" : $" (처음: {worst.X},{worst.Y})";
        GD.Print($"[TileWorld] 길 {roads}칸 중 막힌 칸 {blocked}칸{where}");
    }

    /// <summary>해당 월드 좌표가 벽이 아닌가.</summary>
    public bool IsWalkable(Vector2 worldPosition)
    {
        if (ReferenceWorld3D.Enabled && ReferenceTerrain3D.CoastalWater(worldPosition)) return false;
        if (ReferenceWorld3D.Instance is { CanCheckStanding: true } view) return view.CanStand(worldPosition);
        // 집·우물은 타일이 아니라 스프라이트 + 충돌체다. 바닥 타일만 보면
        // 집터가 '설 수 있는 자리'로 보이므로 소품에게 먼저 묻는다.
        if (_props != null && _props.IsBlocked(worldPosition))
            return false;

        Vector2I cell = LocalToMap(ToLocal(worldPosition));
        int source = GetCellSourceId(cell);

        // Wang 지형 소스에는 바닥만 들어 있다 — 충돌 폴리곤도 없다.
        // (마을 바닥을 지형으로 다시 칠하면 소스 번호가 바뀌므로 여기서 함께 본다.)
        if (source == GrassDirtSourceId || source == DirtStoneSourceId
            || source == GrassStoneSourceId)
            return true;
        if (source == GrassWaterSourceId)
            return false;      // 강은 못 건넌다 — 다리로만.
        if (source == TownWallSourceId)
            return false;      // 성벽. 실제 충돌은 코너 사분면 단위로 붙어 있다.

        return source == SourceId && !IsSolid(GetCellAtlasCoords(cell).X);
    }

    private TileSet BuildTileSet()
    {
        var tileSet = new TileSet
        {
            TileSize = new Vector2I(WorldLayout.TileSize, WorldLayout.TileSize),
        };
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(PhysicsLayer, CollisionLayers.World);
        tileSet.SetPhysicsLayerCollisionMask(PhysicsLayer, 0);

        var source = new TileSetAtlasSource
        {
            Texture = BuildAtlasTexture(),
            TextureRegionSize = new Vector2I(WorldLayout.TileSize, WorldLayout.TileSize),
        };

        // TileData 는 소속 TileSet 에서 물리 레이어 개수를 읽는다.
        // 반드시 AddSource 를 먼저 하고 나서 타일을 만들어야 충돌을 붙일 수 있다.
        tileSet.AddSource(source, SourceId);

        for (int x = 0; x < TileCount; x++)
            source.CreateTile(new Vector2I(x, 0));

        // 막힌 타일(벽 + 건물)에 충돌 폴리곤을 붙인다. 좌표는 타일 중심 기준.
        float half = WorldLayout.TileSize * 0.5f;
        var square = new[]
        {
            new Vector2(-half, -half),
            new Vector2(half, -half),
            new Vector2(half, half),
            new Vector2(-half, half),
        };
        for (int x = 0; x < TileCount; x++)
        {
            if (!IsSolid(x))
                continue;
            TileData data = source.GetTileData(new Vector2I(x, 0), 0);
            data.SetCollisionPolygonsCount(PhysicsLayer, 1);
            data.SetCollisionPolygonPoints(PhysicsLayer, 0, square);
        }

        AddVillageTerrains(tileSet);
        return tileSet;
    }

    /// <summary>
    /// 마을 바닥용 Wang 타일셋 세 벌(잔디↔흙, 흙↔돌, 잔디↔돌)을 붙인다.
    ///
    /// 세 벌이 잔디·흙·돌 그림을 공유한다. 생성할 때 base_tile_id 로 체인을
    /// 걸었으므로 세 시트의 같은 지형이 같은 그림이고, 어느 조합이 맞닿아도 이어진다.
    /// 세 쌍이 다 있어야 돌길이 잔디에 바로 닿을 수 있다 — 예전에는 광장을 흙으로
    /// 감싸야만 했다.
    ///
    /// 한 벌이라도 없으면 아예 쓰지 않는다. 절반만 실제 아트인 상태보다
    /// 전부 도형인 편이 낫고, 무엇보다 게임이 멈추지 않는다.
    /// </summary>
    private void AddVillageTerrains(TileSet tileSet)
    {
        _grassDirt = WangTileset.Load(tileSet, GrassDirtSourceId,
            $"{TilesRoot}/grass_dirt_metadata.json", $"{TilesRoot}/grass_dirt_image.png");

        _dirtStone = WangTileset.Load(tileSet, DirtStoneSourceId,
            $"{TilesRoot}/dirt_cobble_metadata.json", $"{TilesRoot}/dirt_cobble_image.png");

        _grassStone = WangTileset.Load(tileSet, GrassStoneSourceId,
            $"{TilesRoot}/grass_cobble_metadata.json", $"{TilesRoot}/grass_cobble_image.png");

        _grassWater = WangTileset.Load(tileSet, GrassWaterSourceId,
            $"{TilesRoot}/grass_water_metadata.json", $"{TilesRoot}/grass_water_image.png");

        // 성벽은 있으면 쓰고 없으면 코드로 그린 석축 무늬로 간다 — 나머지 바닥과
        // 달리 없어도 마을이 성립하므로 _terrainReady 조건에 넣지 않는다.
        _townWall = WangTileset.Load(tileSet, TownWallSourceId,
            $"{TilesRoot}/town_wall_metadata.json", $"{TilesRoot}/town_wall_image.png",
            solidQuadrants: true);

        _terrainReady = _grassDirt != null && _dirtStone != null
            && _grassStone != null && _grassWater != null;
        if (!_terrainReady)
            GD.Print("[TileWorld] 마을 타일셋이 없어 도형 바닥으로 간다.");
    }

    private static ImageTexture BuildAtlasTexture()
    {
        int size = WorldLayout.TileSize;
        var image = Image.CreateEmpty(size * TileCount, size, false, Image.Format.Rgba8);

        var wallColor = new Color(0.10f, 0.10f, 0.13f);
        Fill(image, TileWall, wallColor);

        // 구역마다 바닥 두 장(체커용)과 바위 한 장. 단색이 아니라 결을 찍는다 —
        // 열세 구역이 전부 단색 사각형이면 색만 다른 같은 땅으로 읽힌다.
        for (int i = 0; i < WorldLayout.Zones.Length; i++)
        {
            ZoneDef zone = WorldLayout.Zones[i];
            PaintZoneFloor(image, ZoneFloorA(i), zone, 0);
            PaintZoneFloor(image, ZoneFloorB(i), zone, 1);
            PaintBoulder(image, ZoneSolid(i), zone, 0);
            PaintBoulder(image, ZoneSolidB(i), zone, 1);
        }

        // 집 지붕색 3종 (참조 이미지의 다양한 지붕을 흉내낸 플레이스홀더)
        Fill(image, TileHouseA, new Color(0.58f, 0.32f, 0.22f));   // 주황 기와
        Fill(image, TileHouseB, new Color(0.32f, 0.36f, 0.56f));   // 청색 슬레이트
        Fill(image, TileHouseC, new Color(0.46f, 0.30f, 0.50f));   // 보라 지붕

        // 강(물) 2색 + 다리(널빤지)
        var water = new Color(0.20f, 0.42f, 0.62f);
        Fill(image, TileWaterA, water);
        Fill(image, TileWaterB, Shade(water));
        Fill(image, TileBridge, new Color(0.52f, 0.38f, 0.22f));

        // 마을 길(다져진 흙)과 광장(포장돌), 훈련장 울타리(나무).
        var dirt = new Color(0.44f, 0.34f, 0.22f);
        Fill(image, TileDirtA, dirt);
        Fill(image, TileDirtB, Shade(dirt));
        var stone = new Color(0.46f, 0.46f, 0.50f);
        Fill(image, TileStoneA, stone);
        Fill(image, TileStoneB, Shade(stone));
        Fill(image, TileFence, new Color(0.40f, 0.28f, 0.16f));

        // 성벽 3단 — 볕 드는 흉벽 / 몸통 / 그늘. 색만으로 두께가 읽힌다.
        // 단색으로 칠하면 회색 띠일 뿐이라 돌 쌓은 결을 직접 그려 넣는다.
        Masonry(image, TileTownWallTop, new Color(0.62f, 0.60f, 0.55f));
        Masonry(image, TileTownWall, new Color(0.47f, 0.45f, 0.42f));
        Masonry(image, TileTownWallBase, new Color(0.31f, 0.30f, 0.29f));

        return ImageTexture.CreateFromImage(image);
    }

    private static void Fill(Image image, int tileIndex, Color color)
    {
        int size = WorldLayout.TileSize;
        image.FillRect(new Rect2I(tileIndex * size, 0, size, size), color);
    }

    // ── 구역 바닥의 결 ───────────────────────────────────────────────────
    //
    // 마을 바닥만 실제 픽셀아트(Wang 타일셋)고, 나머지 열두 구역은 도형이다.
    // 도형이라고 단색일 필요는 없다 — 32px 안에 결을 찍으면 '색만 다른 같은 땅'을
    // 면할 수 있고, 아트를 새로 뽑지 않아도 된다. (CLAUDE.md 규칙 5)
    //
    // 무늬는 좌표 해시로만 정해지므로 실행할 때마다 같다. variant 0/1 은
    // 체커보드로 번갈아 깔리는 두 장이라 씨앗만 다르다.

    /// <summary>좌표를 넣으면 0~1 이 나오는 결정적 해시. 난수기를 들고 다닐 것 없다.</summary>
    private static float Hash(int x, int y, int salt)
    {
        int h = x * 374761393 + y * 668265263 + salt * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
    }

    private static Color Mix(Color a, Color b, float t)
        => new(Mathf.Lerp(a.R, b.R, t), Mathf.Lerp(a.G, b.G, t), Mathf.Lerp(a.B, b.B, t));

    private static Color Lift(Color c, float amount)
        => new(Mathf.Clamp(c.R + amount, 0f, 1f),
               Mathf.Clamp(c.G + amount, 0f, 1f),
               Mathf.Clamp(c.B + amount, 0f, 1f));

    private static void PaintZoneFloor(Image image, int tileIndex, ZoneDef zone, int variant)
    {
        int size = WorldLayout.TileSize;
        int ox = tileIndex * size;
        Color baseColor = variant == 0 ? zone.FloorColor : Shade(zone.FloorColor);
        int salt = variant * 7717 + zone.Id.Length * 131;

        image.FillRect(new Rect2I(ox, 0, size, size), baseColor);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = PatternPixel(zone.Pattern, baseColor, x, y, salt);
                if (c.A > 0f)
                    image.SetPixel(ox + x, y, c);
            }
        }
    }

    /// <summary>
    /// 이 결에서 (x,y) 픽셀을 무슨 색으로 덮을지. 알파 0 이면 바닥색 그대로 둔다.
    /// </summary>
    private static Color PatternPixel(GroundPattern pattern, Color baseColor, int x, int y, int salt)
    {
        var none = new Color(0f, 0f, 0f, 0f);
        float n = Hash(x, y, salt);

        switch (pattern)
        {
            case GroundPattern.Grass:
                // 세로로 선 풀포기 — 세 픽셀짜리 밝은 획을 드문드문.
                if (Hash(x, y / 3, salt) > 0.90f)
                    return Lift(baseColor, 0.10f - (y % 3) * 0.02f);
                return n > 0.94f ? Lift(baseColor, -0.05f) : none;

            case GroundPattern.Leaf:
                // 떨어진 잎 — 가로로 퍼진 2x1 얼룩이 갈색과 올리브로 섞인다.
                if (Hash(x / 2, y, salt) > 0.88f)
                    return Mix(baseColor, new Color(0.36f, 0.26f, 0.13f), 0.55f);
                if (Hash(x, y, salt + 91) > 0.93f)
                    return Mix(baseColor, new Color(0.28f, 0.34f, 0.18f), 0.5f);
                return none;

            case GroundPattern.Bog:
                // 고인 물웅덩이 — 넓적한 얼룩. 사인파로 결을 그렸더니 불씨 무늬와
                // 같은 이유로 칸마다 같은 물결이 반복됐다.
                if (Hash(x / 5, y / 3, salt) > 0.80f)
                    return Mix(baseColor, new Color(0.30f, 0.40f, 0.36f), 0.5f);
                return n > 0.96f ? Lift(baseColor, 0.08f) : none;

            case GroundPattern.Rubble:
                // 깨진 자갈 — 4px 격자마다 돌 하나, 밝기를 크게 흔든다.
                {
                    float pick = Hash(x / 4, y / 4, salt);
                    if (pick < 0.45f)
                        return none;
                    bool edge = x % 4 == 0 || y % 4 == 0;
                    return Lift(baseColor, edge ? -0.07f : (pick - 0.7f) * 0.35f);
                }

            case GroundPattern.Flagstone:
                // 다듬은 돌판 — 16px 슬래브에 줄눈. 켜마다 반 칸 어긋난다.
                {
                    int course = y / 16;
                    int shift = (course % 2) * 8;
                    if (y % 16 == 0 || (x + shift) % 16 == 0)
                        return Lift(baseColor, -0.09f);
                    float jitter = (Hash((x + shift) / 16, course, salt) - 0.5f) * 0.10f;
                    return Lift(baseColor, jitter);
                }

            case GroundPattern.Frost:
                // 서리 결정 — 흰 점 몇 개와 X 자로 뻗은 실금.
                if (n > 0.975f)
                    return new Color(0.92f, 0.96f, 1f);
                if ((x + y) % 11 == 0 && Hash(x / 11, y / 11, salt) > 0.7f)
                    return Lift(baseColor, 0.14f);
                return n < 0.03f ? Lift(baseColor, -0.10f) : none;

            case GroundPattern.Ember:
                // 흩어진 불씨.
                //
                // 원래는 사인파로 세로 균열을 그렸는데, 그 곡선이 y 에만 의존해서
                // 모든 칸에 똑같은 물결이 들어갔다 — 화면 전체가 주황 물결 벽지였다.
                // 32px 로 잘린 타일에서 '선'은 언제나 주기를 드러낸다. 잔디가
                // 안 들키는 이유가 점이기 때문이라, 여기도 점으로 간다.
                if (n > 0.990f)
                    return new Color(0.95f, 0.52f, 0.16f);
                if (n > 0.972f)
                    return Mix(baseColor, new Color(0.62f, 0.24f, 0.08f), 0.55f);
                return n < 0.07f ? Lift(baseColor, -0.05f) : none;

            case GroundPattern.Bone:
                // 마른 모래에 박힌 뼛조각 — 가로로 누운 3x1 흰 획.
                if (Hash(x / 3, y, salt) > 0.955f)
                    return new Color(0.82f, 0.79f, 0.70f);
                return n > 0.9f ? Lift(baseColor, 0.05f) : none;

            default:
                return none;
        }
    }

    /// <summary>
    /// 그 구역의 바위 한 덩이. 타일을 꽉 채우되 네 귀를 깎아 둥글게 만들고,
    /// 왼쪽 위를 밝게 오른쪽 아래를 어둡게 해서 덩어리로 보이게 한다.
    /// 귀퉁이는 바닥색으로 남기므로 여러 칸이 붙어도 바위 무리처럼 읽힌다.
    /// </summary>
    /// <summary>
    /// 구역의 막힌 타일 — 암반 한 칸.
    ///
    /// 칸 가운데에 돌 하나를 그렸었다. 그런데 이 타일은 낱개로 놓이는 일이 거의
    /// 없고 2~4칸 덩어리로 붙어 깔린다 — 그러면 알을 격자로 늘어놓은 것처럼 보였다.
    /// 칸을 가득 채우는 결로 그려야 이웃과 이어져 바위 덩어리로 읽힌다.
    /// </summary>
    private static void PaintBoulder(Image image, int tileIndex, ZoneDef zone, int variant)
    {
        int size = WorldLayout.TileSize;
        int ox = tileIndex * size;
        Color rock = zone.SolidColor;
        int salt = zone.Id.Length * 977 + 13 + variant * 5231;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 굵은 결(암반 덩어리)과 잔결(부스러기)을 겹친다. 두 결의 칸 크기를
                // 서로소로 두어야 32px 주기가 드러나지 않는다.
                float coarse = (Hash(x / 7, y / 5, salt + 31) - 0.5f) * 0.26f;
                float fine = (Hash(x / 2, y / 3, salt) - 0.5f) * 0.12f;
                image.SetPixel(ox + x, y, Lift(rock, coarse + fine - 0.04f));
            }
        }
    }

    /// <summary>돌 한 켜의 높이(px). 32px 타일에 네 켜가 들어간다.</summary>
    private const int MasonryCourse = 8;

    /// <summary>돌 하나의 폭(px).</summary>
    private const int MasonryBlock = 11;

    /// <summary>
    /// 성벽 타일에 돌 쌓은 결을 그린다.
    ///
    /// 성벽은 팔각이라 대각선 구간이 있어서, 스프라이트를 늘어놓는 방식으로는
    /// 이음매가 어긋난다. 타일 한 장에 무늬를 넣으면 어느 방향이든 그대로 이어진다.
    /// 켜마다 반 칸씩 어긋나게 쌓고 돌마다 밝기를 흔들어, 32px 안에서도
    /// '쌓아 올린 것'으로 읽히게 한다. 무늬는 좌표로만 정해지므로 매번 같다.
    /// </summary>
    private static void Masonry(Image image, int tileIndex, Color baseColor)
    {
        int size = WorldLayout.TileSize;
        int ox = tileIndex * size;

        var mortar = new Color(baseColor.R * 0.62f, baseColor.G * 0.62f, baseColor.B * 0.64f);
        image.FillRect(new Rect2I(ox, 0, size, size), mortar);

        for (int y = 0; y < size; y++)
        {
            int course = y / MasonryCourse;
            if (y % MasonryCourse == 0)
                continue;                                  // 가로 줄눈

            // 켜마다 반 칸 어긋나게 — 벽돌이 일자로 서면 격자무늬가 된다.
            int shift = (course % 2) * (MasonryBlock / 2);

            for (int x = 0; x < size; x++)
            {
                int block = (x + shift) / MasonryBlock;
                if ((x + shift) % MasonryBlock == 0)
                    continue;                              // 세로 줄눈

                // 돌마다 밝기를 조금씩 흔든다. 좌표 해시라 실행할 때마다 같다.
                float jitter = ((block * 7 + course * 13) % 5 - 2) * 0.035f;
                image.SetPixel(ox + x, y, new Color(
                    Mathf.Clamp(baseColor.R + jitter, 0f, 1f),
                    Mathf.Clamp(baseColor.G + jitter, 0f, 1f),
                    Mathf.Clamp(baseColor.B + jitter, 0f, 1f)));
            }
        }
    }

    private static Color Shade(Color color)
        => new(color.R + CheckerShade, color.G + CheckerShade, color.B + CheckerShade);

    /// <summary>구역 바닥 결마다 어울리는 소품. 나무·바위는 전부 기존 마을 에셋이다.</summary>
    private static string[] PropsFor(GroundPattern pattern, bool indoor)
    {
        if (indoor)
            return System.Array.Empty<string>();

        return pattern switch
        {
            GroundPattern.Grass => new[]
                { "tree_oak", "tree_oak", "orchard_tree", "boulder", "haystack", "flower_pots", "tree_stump" },
            GroundPattern.Leaf => new[]
                { "tree_conifer", "tree_conifer", "tree_oak", "tree_stump", "log_pile", "boulder" },

            // 늪 — 죽은 그루터기와 삭은 나무. 셋 다 밑동에 잔디가 없다.
            GroundPattern.Bog => new[]
                { "tree_stump", "tree_stump", "log_pile", "firewood" },

            _ => System.Array.Empty<string>(),
        };
    }


    /// <summary>
    /// 소품 한 개당 몇 칸. 사냥터가 두 가지 잔디 타일만 번갈아 깔린 체커보드라
    /// 걸어도 지나온 곳과 갈 곳이 구분되지 않았다 — 지형지물이 있어야 길이 생긴다.
    /// </summary>
    private const int TilesPerZoneProp = 55;

    /// <summary>한 구역 상한. 넓은 구역 하나가 스프라이트 예산을 다 먹지 않게.</summary>
    private const int ZonePropCap = 240;

    /// <summary>
    /// 구역마다 나무·바위를 흩뿌린다.
    ///
    /// 밀도는 그 구역에 이미 잡혀 있는 Boulders 값을 가중치로 쓴다 — 숲(40)이
    /// 초원(10)보다 네 배 빽빽해야 한다는 판단이 거기 이미 들어가 있어서
    /// 같은 숫자를 두 벌 관리할 이유가 없다.
    /// </summary>
    private System.Collections.Generic.List<PropPlacement> ScatterZoneProps()
    {
        var placed = new System.Collections.Generic.List<PropPlacement>();
        var taken = new System.Collections.Generic.HashSet<Vector2I>();
        int total = 0;

        foreach (var zone in WorldLayout.Zones)
        {
            if (zone.Safe || zone.Boulders <= 0)
                continue;

            Rect2I r = zone.Tiles;
            int area = r.Size.X * r.Size.Y;
            int want = Mathf.Min(area / TilesPerZoneProp * zone.Boulders / 10, ZonePropCap);
            if (zone == WorldLayout.Meadow) want = 280;
            if (want <= 0)
                continue;

            // 소품 없이 지형만으로 채우는 구역이 있다. 빈 목록에서 뽑으면 터진다.
            string[] kinds = PropsFor(zone.Pattern, zone.Indoor);
            if (kinds.Length == 0)
                continue;

            Color tint = ZoneTint(zone);
            var rng = new RandomNumberGenerator
            {
                Seed = (ulong)((StableZoneHash(zone.Id) ^ 0x5eed) & 0x7fffffff),
            };

            int margin = 3;
            for (int i = 0; i < want; i++)
            {
                string kind = kinds[rng.RandiRange(0, kinds.Length - 1)];
                int w = 2, h = kind.StartsWith("tree_") || kind == "orchard_tree" ? 3 : 2;

                int x = rng.RandiRange(r.Position.X + margin, r.Position.X + r.Size.X - margin - w);
                int y = rng.RandiRange(r.Position.Y + margin, r.Position.Y + r.Size.Y - margin - h);
                if (zone == WorldLayout.Meadow)
                {
                    string[] local = x < 119 ? new[] { "orchard_tree", "tree_oak", "flower_pots", "haystack" }
                        : new[] { "tree_oak", "tree_conifer", "boulder", "log_pile" };
                    kind = local[rng.RandiRange(0, local.Length - 1)];
                    h = kind.Contains("tree") ? 3 : 2;
                }
                var rect = new Rect2I(x, y, w, h);

                if ((zone == WorldLayout.Meadow && MeadowLayout.Reserved(WorldLayout.TileCenter(x + w / 2, y + h / 2)))
                    || NearCorridor(rect) || !AllOpenFloor(rect) || Overlaps(taken, rect))
                    continue;

                for (int ty = y; ty < y + h; ty++)
                {
                    for (int tx = x; tx < x + w; tx++)
                        taken.Add(new Vector2I(tx, ty));
                }

                placed.Add(new PropPlacement(rect, kind, tint));
                total++;
            }
        }

        GD.Print($"[TileWorld] 야외 소품 {total}개");
        return placed;
    }

    /// <summary>
    /// 그 구역에 놓인 소품에 곱할 색.
    ///
    /// 소품 그림은 전부 마을(초록 잔디) 기준이라 밑동에 잔디가 붙어 있고 전체적으로
    /// 밝다. 갱도·잿물 골짜기·뼈들판에 그대로 놓으면 어두운 땅 위에서 혼자 조명을
    /// 받은 것처럼 뜬다. 구역 바닥색 쪽으로 살짝 끌어당겨 그 땅에서 자란 것처럼 만든다.
    /// </summary>
    private static Color ZoneTint(ZoneDef zone)
    {
        // 초원·숲은 원래 색이 맞다. 실내는 빛이 없으므로 훨씬 더 눌러야 한다.
        float pull = zone.Indoor ? 0.85f : 0.35f;

        Color ground = zone.FloorColor;
        float lum = ground.R * 0.3f + ground.G * 0.59f + ground.B * 0.11f;

        // 바닥색을 그대로 곱하면 새까매진다. 바닥의 '색조'만 가져오고 밝기는
        // 어둡기에 비례해서만 낮춘다.
        Color hue = lum > 0.01f
            ? new Color(ground.R / lum, ground.G / lum, ground.B / lum)
            : Colors.White;

        float dim = Mathf.Lerp(1f, Mathf.Clamp(0.25f + lum, 0.22f, 1f), pull);
        return new Color(
            Mathf.Lerp(1f, hue.R, pull) * dim,
            Mathf.Lerp(1f, hue.G, pull) * dim,
            Mathf.Lerp(1f, hue.B, pull) * dim);
    }

    /// <summary>그 사각형이 전부 걸을 수 있는 바닥 타일인가(벽·바위 위에 얹지 않는다).</summary>
    private bool AllOpenFloor(Rect2I rect)
    {
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
            {
                var cell = new Vector2I(x, y);
                if (GetCellSourceId(cell) != SourceId || IsSolid(GetCellAtlasCoords(cell).X))
                    return false;
            }
        }
        return true;
    }

    private static bool Overlaps(System.Collections.Generic.HashSet<Vector2I> taken, Rect2I rect)
    {
        // 나무가 겹쳐 서면 밑동 충돌체도 겹쳐 한 칸짜리 함정이 생긴다.
        var pad = rect.Grow(1);
        for (int y = pad.Position.Y; y < pad.Position.Y + pad.Size.Y; y++)
        {
            for (int x = pad.Position.X; x < pad.Position.X + pad.Size.X; x++)
            {
                if (taken.Contains(new Vector2I(x, y)))
                    return true;
            }
        }
        return false;
    }

    private static int StableZoneHash(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value) hash = unchecked((hash ^ c) * 16777619);
        return (int)(hash & 0x7fffffff);
    }

    private System.Collections.Generic.List<PropPlacement> ForestProps()
    {
        var result = new System.Collections.Generic.List<PropPlacement>();
        var rng = new RandomNumberGenerator { Seed = 20260908 };
        for(int y=5;y<WorldLayout.HeightTiles-5;y+=3) for(int x=5;x<WorldLayout.WidthTiles-5;x+=3)
        {
            int tx=x+rng.RandiRange(-1,1), ty=y+rng.RandiRange(-1,1);
            Vector2 p=new(tx+1.5f,ty+3f); Vector2 world=p*WorldLayout.TileSize;
            bool village=WorldLayout.Town.Contains(world);
            bool nearHouse=_town.Props.Exists(prop=>prop.Solid && prop.Tiles.Grow(2).HasPoint((Vector2I)p));
            if(!WorldLayout.IsForest(p) || (village && (nearHouse || rng.Randf()<.5f))
                || Mathf.Abs(p.X-WorldLayout.RiverX(p.Y))<5f || WorldLayout.PoolDistance(p)<2f || MeadowLayout.Reserved(world)
                || p.DistanceTo(new Vector2(48,49))<7f) continue;
            float grove = .5f + .5f*Mathf.Sin(tx*.12f+Mathf.Cos(ty*.083f)*2f)*Mathf.Cos(ty*.095f);
            if(rng.Randf()<Mathf.Lerp(.12f,.70f,grove)) continue;
            bool wetland=tx>280 && ty>145;
            if(((p-new Vector2(325,85))/new Vector2(27,21)).Length()<1f) continue;
            string tree = wetland ? rng.Randf()<.42f?"tree_willow":"tree_oak"
                : Mathf.Abs(p.X-WorldLayout.RiverX(p.Y))<11f ? "tree_willow"
                : ty<95 && tx>190 ? rng.Randf()<.88f?"tree_conifer":"tree_stump"
                : rng.Randf()<.25f?"tree_conifer":"tree_oak";
            var tint=wetland?new Color(.81f,.94f,.83f):new Color(.94f,1f,.91f);
            result.Add(new(new(tx,ty,3,3),tree,tint,flipH:rng.Randf()<.5f));
            if(!village && rng.Randf()<.12f && !MeadowLayout.Reserved(world+new Vector2(0,32)))
                result.Add(new(new(tx+1,ty+3,1,1),"boulder"));
        }
        void Prop(string name,int x,int y,int w=2,int h=2,bool solid=true)
            => result.Add(new(new(x,y,w,h),name,solid));
        // Camps have distinct silhouettes and empty fighting space in front of the structures.
        Prop("goblin_hut",157,58,3,3); Prop("goblin_hut",168,60,3,3);
        Prop("goblin_hut",174,67,3,3); Prop("watchtower",152,69,3,4);
        Prop("barrels",170,74); Prop("crates",158,75); Prop("firewood",160,65);
        Prop("log_pile",66,141,3,2); Prop("hand_cart",75,144);
        Prop("kit_s_shed",64,146,3,3); Prop("boulder",43,46,3,3);
        Prop("boulder",51,46,3,3); Prop("tree_oak",45,42,3,4);
        Prop("root_entrance",46,46,4,3,false);
        Prop("statue",-85,25,2,3); Prop("statue",-66,23,2,3);
        Prop("boulder",-82,23,2,2); Prop("boulder",-69,22,2,2);
        Prop("tree_stump",-82,32,2,2); Prop("log_pile",-67,30,2,2);
        Prop("boulder",-79,36,2,2); Prop("boulder",-71,37,2,2);
        Prop("bench",80,39,2,1,false); Prop("flower_pots",86,39,1,1,false);
        var landmarks = new System.Collections.Generic.List<PropPlacement>();
        void Detail(string name,int x,int y,int w=2,int h=2,bool solid=true)
            => landmarks.Add(new(new(x,y,w,h),name,solid));
        Detail("watchtower",208,90,3,4); Detail("goblin_hut",223,90,3,3);
        Detail("barrels",226,97); Detail("firewood",209,102); Detail("crates",213,94);
        Detail("statue",240,137,2,3); Detail("statue",255,137,2,3);
        Detail("boulder",240,150,3,2); Detail("flower_pots",244,140,1,1,false);
        Detail("well",253,151); Detail("bench",239,145,2,1,false);
        Detail("kit_m_cottage",207,44,4,3); Detail("kit_s_shed",222,46,3,3);
        Detail("hand_cart",223,55); Detail("log_pile",207,58,3,2); Detail("firewood",211,48);
        Detail("lamp_post",213,49,1,2,false); Detail("barrels",223,50);
        Detail("chapel",187,180,4,4); Detail("bench",189,189,2,1,false);
        Detail("well",202,182); Detail("flower_pots",187,186,1,1,false);
        Detail("lamp_post",194,183,1,2,false); Detail("statue",201,199,2,3);
        Detail("boulder",254,91,3,3); Detail("boulder",268,96,3,3);
        Detail("boulder",265,106,3,3); Detail("hand_cart",256,105);
        Detail("crates",269,101); Detail("log_pile",254,98,3,2);
        Detail("tree_oak",253,182,4,5); Detail("statue",265,186,2,3);
        Detail("bench",252,197,2,1,false); Detail("flower_pots",265,196,1,1,false);
        Detail("kit_s_shed",169,200,3,3); Detail("hand_cart",183,216);
        Detail("log_pile",170,215,3,2); Detail("lamp_post",173,206,1,2,false);
        // Eastern settlement: dwellings around a working yard, food plots, stores and a guarded entrance.
        Detail("goblin_hut",299,72,3,3); Detail("goblin_hut",308,70,3,3);
        Detail("goblin_hut",326,65,3,3); Detail("goblin_hut",337,67,3,3);
        Detail("goblin_hut",352,94,3,3); Detail("goblin_hut",349,107,3,3);
        Detail("watchtower",293,94,3,4); Detail("watchtower",350,79,3,4);
        Detail("well",317,86); Detail("barrels",323,87); Detail("crates",327,89);
        Detail("hand_cart",313,94); Detail("log_pile",342,84,3,2); Detail("firewood",347,87);
        Detail("farm_plot",320,63,3,2,false); Detail("veg_patch",324,60,3,2,false);
        Detail("haystack",314,62); Detail("bench",302,76,2,1,false);
        Detail("tree_stump",346,112); Detail("log_pile",350,115,3,2);
        // Slime wetlands: orchard silhouettes, fallen timber and rounded stone clusters leave wide hunting glades.
        Detail("tree_willow",289,169,3,3); Detail("orchard_tree",294,165,3,3);
        Detail("tree_stump",311,184); Detail("log_pile",315,188,3,2);
        Detail("boulder",331,181,3,2); Detail("boulder",335,184,2,2);
        Detail("tree_willow",351,159,3,3); Detail("orchard_tree",355,164,3,3);
        Detail("boulder",337,215,3,3); Detail("tree_stump",342,218);
        Detail("kit_s_shed",300,208,3,3); Detail("hand_cart",304,212);
        // The old aqueduct clearing separates the wet lowlands from the conifer ridge.
        Detail("statue",284,137,2,3); Detail("statue",291,141,2,3);
        Detail("boulder",286,145,3,2); Detail("well",277,137);
        // Keep the silhouettes of authored landmarks clear of the procedural tree canopy.
        result.RemoveAll(p => landmarks.Exists(d => d.Tiles.Grow(2).Intersects(p.Tiles)));
        result.AddRange(landmarks);
        return result;
    }

    private void Paint()
    {
        Clear();
        for(int y=0;y<WorldLayout.HeightTiles;y++) for(int x=0;x<WorldLayout.WidthTiles;x++)
        {
            Vector2 p = new(x+.5f,y+.5f);
            if(!WorldLayout.IsForest(p)) continue;
            SetFloor(x,y,TileGrassA,TileGrassB);
            float hollow = Mathf.Min(((p-new Vector2(324,199))/new Vector2(11,8)).Length(),
                Mathf.Min(((p-new Vector2(300,179))/new Vector2(9,7)).Length(),((p-new Vector2(342,170))/new Vector2(10,7)).Length()));
            float yard = Mathf.Min(((p-new Vector2(332,75))/new Vector2(12,9)).Length(),
                Mathf.Min(((p-new Vector2(306,82))/new Vector2(10,9)).Length(),((p-new Vector2(344,100))/new Vector2(12,10)).Length()));
            if(hollow<.82f+.10f*Mathf.Sin(p.X*.23f)*Mathf.Cos(p.Y*.18f)
                || yard<.83f+.08f*Mathf.Sin(p.Y*.35f)) SetFloor(x,y,TileDirtA,TileDirtB);
            if(((p-new Vector2(286,140))/new Vector2(9,6)).Length()<.9f+.12f*Mathf.Sin(p.X))
                SetFloor(x,y,TileStoneA,TileStoneB);
            if(WorldLayout.Water(p) && !WorldLayout.Bridge(p)) SetCellTile(x,y,TileWaterA);
        }
        // Paths are continuous ribbons through clearings, rather than rectangular region gates.
        foreach(var route in MeadowLayout.Data.Routes)
            for(int i=1;i<route.Length;i++)
            {
                Vector2 a=new(route[i-1][0],route[i-1][1]), b=new(route[i][0],route[i][1]);
                for(int y=(int)Mathf.Min(a.Y,b.Y)-2;y<=Mathf.Max(a.Y,b.Y)+2;y++)
                for(int x=(int)Mathf.Min(a.X,b.X)-2;x<=Mathf.Max(a.X,b.X)+2;x++)
                    if(Geometry2D.GetClosestPointToSegment(new(x,y),a,b).DistanceTo(new(x,y))<.65f
                        && !WorldLayout.Water(new(x+.5f,y+.5f))) SetFloor(x,y,TileDirtA,TileDirtB);
            }
        PaintVillage();
        ApplyTerrainIn(new Rect2I(0,0,WorldLayout.WidthTiles,WorldLayout.HeightTiles), "연속 숲 지형");
        for(int y=0;y<WorldLayout.HeightTiles;y++) for(int x=0;x<WorldLayout.WidthTiles;x++)
            if(WorldLayout.Bridge(new(x+.5f,y+.5f))) SetCellTile(x,y,TileBridge);
        PaintTestArena(WorldLayout.TestArenaTiles);
        PaintSanctuary();

    }

    /// <summary>
    /// 링크 목록대로 구역 사이 벽을 뚫는다.
    ///
    /// 통로 바닥은 양쪽 중 **덜 위험한 쪽**의 것으로 깐다 — 통로를 나서기 전에
    /// 바닥이 바뀌면 "여기부터 다른 땅"이라는 신호가 한 걸음 빨리 온다.
    /// </summary>
    private void PaintSanctuary()
    {
        bool Floor(int x,int y)
        {
            Vector2 p=new(x+.5f,y+.5f);
            return ((p-new Vector2(-74,29))/new Vector2(10,8)).LengthSquared()<1f
                || ((p-new Vector2(-74,39))/new Vector2(5,4)).LengthSquared()<1f
                || ((p-new Vector2(-84,30))/new Vector2(4,4)).LengthSquared()<1f
                || ((p-new Vector2(-64,27))/new Vector2(4,4)).LengthSquared()<1f;
        }
        var bounds=WorldLayout.IronjawDen.Tiles.Grow(2);
        for(int y=bounds.Position.Y;y<bounds.End.Y;y++) for(int x=bounds.Position.X;x<bounds.End.X;x++)
        {
            if(Floor(x,y)) SetCellTile(x,y,TileStoneA);
            else if(Floor(x+1,y)||Floor(x-1,y)||Floor(x,y+1)||Floor(x,y-1)) SetCellTile(x,y,TileWall);
        }
        ApplyTerrainIn(bounds,"성소 석재 바닥");
    }

    private void CarveCorridors()
    {
        int made = 0, failed = 0;

        foreach (var (rect, a, b) in WorldLayout.Corridors())
        {
            if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            {
                GD.PushWarning($"[TileWorld] 통로를 낼 수 없다: {a.Id} ↔ {b.Id} (겹치는 변이 좁다)");
                failed++;
                continue;
            }

            ZoneDef floorFrom = a.Danger <= b.Danger ? a : b;
            int index = System.Array.IndexOf(WorldLayout.Zones, floorFrom);

            for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
            {
                for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
                    SetFloor(x, y, ZoneFloorA(index), ZoneFloorB(index));
            }
            made++;
        }

        GD.Print($"[TileWorld] 구역 {WorldLayout.Zones.Length}곳, 통로 {made}개"
            + (failed > 0 ? $" (실패 {failed}개)" : ""));
    }

    /// <summary>
    /// 구역 안에 바위 덩어리를 흩뿌린다.
    ///
    /// 한 칸씩 뿌리면 점박이가 되므로 2~4칸짜리 덩어리로 뭉쳐 놓는다.
    /// 구역 가장자리 2칸과 통로 앞은 비워 둔다 — 들어서자마자 바위에
    /// 막히면 다른 구역이 아니라 막다른 길로 보인다.
    /// </summary>
    private void ScatterBoulders(ZoneDef zone, int index)
    {
        if (zone.Boulders <= 0 || zone.Safe)
            return;

        Rect2I r = zone.Tiles;
        var rng = new RandomNumberGenerator { Seed = (ulong)(StableZoneHash(zone.Id) & 0x7fffffff) };
        int solid = ZoneSolid(index);
        int solidB = ZoneSolidB(index);
        int margin = 3;
        int placed = 0;

        // 나무 그림이 깔리는 구역에서는 바위 덩어리를 줄인다 — 둘 다 꽉 채우면
        // 걸어 다닐 데가 없다. 소품이 없는 구역에서는 이 덩어리가 그 땅의
        // 유일한 지형이라 반대로 늘려야 한다. 원래 값으로는 이백 칸에 하나꼴이라
        // 잿물 골짜기를 가로질러도 아무 것도 안 나왔다.
        int clusters = PropsFor(zone.Pattern, zone.Indoor).Length > 0
            ? Mathf.Max(1, zone.Boulders / 4)
            : zone.Boulders * 3;

        for (int i = 0; i < clusters; i++)
        {
            int w = rng.RandiRange(2, 4), h = rng.RandiRange(2, 4);
            int x = rng.RandiRange(r.Position.X + margin, r.Position.X + r.Size.X - margin - w);
            int y = rng.RandiRange(r.Position.Y + margin, r.Position.Y + r.Size.Y - margin - h);
            var block = new Rect2I(x, y, w, h);

            if ((zone == WorldLayout.Meadow && MeadowLayout.Reserved(WorldLayout.TileCenter(x + w / 2, y + h / 2))) || NearCorridor(block))
                continue;

            for (int by = y; by < y + h; by++)
            {
                for (int bx = x; bx < x + w; bx++)
                    SetCellTile(bx, by, Hash(bx, by, 8821) < 0.5f ? solid : solidB);
            }
            placed++;
        }

        if (placed < clusters)
            GD.Print($"[TileWorld] {zone.DisplayName} 바위 {placed}/{clusters}덩이 (통로 앞은 비운다)");
    }

    /// <summary>통로 입구 4칸 안인가. 여기에 바위를 놓으면 구역이 막힌다.</summary>
    private static bool NearCorridor(Rect2I block)
    {
        var padded = block.Grow(4);
        foreach (var (rect, _, _) in WorldLayout.Corridors())
        {
            if (rect.Size.X > 0 && padded.Intersects(rect.Grow(4)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 초보자 마을을 찍는다. 잔디 위에 길 → 광장 → 집 → 훈련장 → 우물 순으로 덮는다.
    /// 뒤에 오는 것이 앞의 것을 가리므로 순서가 곧 우선순위다.
    /// </summary>
    private void PaintVillage()
    {
        // 생성기가 만든 바닥표를 그대로 찍는다.
        Rect2I town = WorldLayout.Town.Tiles;
        for (int y = 0; y < town.Size.Y; y++)
        {
            for (int x = 0; x < town.Size.X; x++)
            {
                int tx = town.Position.X + x, ty = town.Position.Y + y;
                switch (_town.Ground[x, y])
                {
                    case Surface.Dirt: SetFloor(tx, ty, TileDirtA, TileDirtB); break;
                    case Surface.Stone: SetFloor(tx, ty, TileStoneA, TileStoneB); break;
                    case Surface.Water: SetFloor(tx, ty, TileWaterA, TileWaterB); break;
                    case Surface.Wall: SetCellTile(tx, ty, WallShade(x, y)); break;
                    default: SetFloor(tx, ty, TileGrassA, TileGrassB); break;
                }
            }
        }

        PaintTrainingYard();

        // 건물·소품은 VillageProps 가 스프라이트로 얹는다. 그림이 없을 때만
        // 지붕색 블록으로 자리를 표시한다 — 게임은 그대로 돌아가야 한다.
        if (!_propsReady)
        {
            int[] roofs = { TileHouseA, TileHouseB, TileHouseC };
            for (int i = 0; i < _town.Props.Count; i++)
                FillSolid(_town.Props[i].Tiles, roofs[i % roofs.Length]);
        }

        // A single Wang pass blends the whole forest, including village lanes.
    }

    /// <summary>
    /// 성벽 한 칸이 띠의 어디쯤인가 — 위 끝이면 흉벽, 아래 끝이면 그늘.
    /// 좌표는 마을 안 지역 좌표다(생성기의 Ground 격자).
    /// </summary>
    private int WallShade(int x, int y)
    {
        Rect2I town = WorldLayout.Town.Tiles;
        bool aboveIsWall = y > 0 && _town.Ground[x, y - 1] == Surface.Wall;
        bool belowIsWall = y < town.Size.Y - 1 && _town.Ground[x, y + 1] == Surface.Wall;

        if (!aboveIsWall)
            return TileTownWallTop;
        return belowIsWall ? TileTownWall : TileTownWallBase;
    }

    /// <summary>
    /// 마을 바닥을 실제 픽셀아트로 다시 칠한다.
    ///
    /// 방금 찍어 둔 도형 타일을 그대로 '어디가 무슨 지형인지'의 정답표로 쓴다 —
    /// 배치 로직을 두 벌 유지하지 않기 위함이다. 집·울타리·우물처럼 막힌 칸은
    /// 지형이 없으므로 건너뛰고, 그 자리엔 도형 타일이 그대로 남는다.
    ///
    /// Wang 타일은 '칸'이 아니라 '네 모서리'로 정해진다. 그래서 칸 지형에서
    /// 정점 지형을 먼저 뽑고(맞닿은 칸 중 가장 위 지형이 이긴다), 각 칸의
    /// 네 정점으로 타일을 고른다. 이러면 경계가 칸 경계가 아니라 칸 한가운데를
    /// 지나가서 길이 자연스럽게 휜다.
    /// </summary>
    private void ApplyVillageTerrain()
    {
        ApplyTerrainIn(WorldLayout.Town.Tiles, "마을 바닥");
        ApplyTownWall();
    }

    /// <summary>같은 절차를 임의 영역에 건다. 마을 바닥과 강이 이걸 함께 쓴다.</summary>
    private void ApplyTerrainIn(Rect2I area, string label)
    {
        if (!_terrainReady)
            return;

        Rect2I town = area;
        int w = town.Size.X, h = town.Size.Y;
        int ox = town.Position.X, oy = town.Position.Y;

        // 1) 칸 지형표. 막힌 칸은 TerrainNone.
        var cell = new int[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                cell[x, y] = TerrainOfCell(ox + x, oy + y);
        }

        // 2) 정점 지형표 (w+1) x (h+1). 정점에 닿은 네 칸 중 가장 위 지형이 이긴다.
        //    막힌 칸은 지형이 없으므로 판정에서 빠지고, 아무 것도 없으면 잔디로 둔다.
        var vertex = new int[w + 1, h + 1];
        for (int vy = 0; vy <= h; vy++)
        {
            for (int vx = 0; vx <= w; vx++)
            {
                int best = TerrainNone;
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int cx = vx + dx, cy = vy + dy;
                        if (cx < 0 || cy < 0 || cx >= w || cy >= h)
                            continue;
                        if (cell[cx, cy] > best)
                            best = cell[cx, cy];
                    }
                }
                vertex[vx, vy] = best < 0 ? TerrainGrass : best;
            }
        }

        // 3) 칸마다 네 정점으로 타일을 고른다.
        int painted = 0, skipped = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (cell[x, y] == TerrainNone)
                    continue;                       // 집·울타리·우물은 도형 그대로.

                if (PaintWangCell(ox + x, oy + y,
                        vertex[x, y], vertex[x + 1, y], vertex[x, y + 1], vertex[x + 1, y + 1]))
                    painted++;
                else
                    skipped++;
            }
        }

        GD.Print($"[TileWorld] {label} {painted}칸 픽셀아트로 교체"
            + (skipped > 0 ? $", {skipped}칸은 도형 유지(지형 조합 없음)" : ""));
    }

    /// <summary>
    /// 성벽을 실제 픽셀아트로 칠한다.
    ///
    /// 성벽은 바닥이 아니라 **높이가 있는 지형**이다. 그래서 잔디↔성벽 Wang 한 벌을
    /// 따로 쓴다 — 타일셋의 transition 이 두 지형의 높이차를 벽면으로 그려 주므로,
    /// 팔각의 직선·대각선·모서리를 우리가 일일이 그릴 필요가 없다.
    ///
    /// 정점 판정은 바닥과 반대로 **네 칸이 모두 성벽일 때만** 성벽으로 친다.
    /// 바닥처럼 '하나라도 있으면' 으로 하면 경계가 띠 바깥으로 한 칸 번져서
    /// 성문 앞 길까지 벽 그림이 덮는다. 이 규칙이면 3칸 띠가
    /// 바깥 흉벽 / 벽 윗면 / 안쪽 밑동 세 줄로 딱 떨어진다.
    /// </summary>
    private void ApplyTownWall()
    {
        if (_townWall == null)
            return;

        Rect2I town = WorldLayout.Town.Tiles;
        int w = town.Size.X, h = town.Size.Y;
        int ox = town.Position.X, oy = town.Position.Y;

        var vertex = new bool[w + 1, h + 1];
        for (int vy = 0; vy <= h; vy++)
        {
            for (int vx = 0; vx <= w; vx++)
                vertex[vx, vy] = AllWall(vx, vy, w, h);
        }

        int painted = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (_town.Ground[x, y] != Surface.Wall)
                    continue;

                int wang = (vertex[x, y] ? 8 : 0) + (vertex[x + 1, y] ? 4 : 0)
                         + (vertex[x, y + 1] ? 2 : 0) + (vertex[x + 1, y + 1] ? 1 : 0);
                if (!_townWall.Has(wang))
                    continue;

                SetCell(new Vector2I(ox + x, oy + y), _townWall.SourceId, _townWall.Atlas(wang));
                painted++;
            }
        }

        GD.Print($"[TileWorld] 성벽 {painted}칸 픽셀아트로 교체");
    }

    /// <summary>그 정점에 닿은 네 칸이 전부 성벽인가. 격자 밖은 성벽이 아니다.</summary>
    private bool AllWall(int vx, int vy, int w, int h)
    {
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int cx = vx + dx, cy = vy + dy;
                if (cx < 0 || cy < 0 || cx >= w || cy >= h)
                    return false;
                if (_town.Ground[cx, cy] != Surface.Wall)
                    return false;
            }
        }
        return true;
    }

    /// <summary>도형 타일을 보고 그 칸의 바닥 지형을 되읽는다. 막힌 칸은 TerrainNone.</summary>
    private int TerrainOfCell(int x, int y)
    {
        if (GetCellSourceId(new Vector2I(x, y)) < 0) return TerrainNone;
        int tile = GetCellAtlasCoords(new Vector2I(x, y)).X;

        // 잔디 타일 번호가 구역표에서 나오므로 상수가 아니다 — switch 패턴을 못 쓴다.
        if (tile == TileGrassA || tile == TileGrassB) return TerrainGrass;

        // 마을 밖 구역 바닥도 '잔디'로 친다. 강은 마을 안팎을 가로지르는데,
        // 여기서 TerrainNone 을 돌려주면 성벽 밖 강가에 물가 타일이 안 붙는다.
        if (tile >= ZoneTileStart && (tile - ZoneTileStart) % TilesPerZone < 2)
            return TerrainGrass;
        if (tile == TileDirtA || tile == TileDirtB) return TerrainDirt;
        if (tile == TileStoneA || tile == TileStoneB) return TerrainStone;
        if (tile == TileWaterA || tile == TileWaterB) return TerrainWater;
        return TerrainNone;
    }

    /// <summary>
    /// 네 코너 지형으로 Wang 타일 하나를 찍는다.
    /// 잔디와 돌이 한 칸에서 만나면 해당 타일셋이 없다 — 그 칸은 손대지 않는다.
    /// (광장을 흙 마당으로 감쌌기 때문에 정상 배치에서는 생기지 않는다.)
    /// </summary>
    private bool PaintWangCell(int x, int y, int nw, int ne, int sw, int se)
    {
        // Wang 타일셋 한 벌은 지형 두 개짜리다. 네 모서리에 셋 이상이 모이면 맞는
        // 타일이 없으므로 가운데인 흙을 돌로 올려 두 개로 줄인다. (흙 골목이 돌
        // 거리에 붙는 자리라 이음매가 포장으로 읽혀도 어색하지 않다.)
        if (DistinctCount(nw, ne, sw, se) > 2)
        {
            nw = Promote(nw); ne = Promote(ne);
            sw = Promote(sw); se = Promote(se);
        }

        int lo = Mathf.Min(Mathf.Min(nw, ne), Mathf.Min(sw, se));
        int hi = Mathf.Max(Mathf.Max(nw, ne), Mathf.Max(sw, se));

        // 가진 전환 네 쌍 중에서 고른다. 없는 쌍(흙↔물, 돌↔물)은 배치로 막는다 —
        // 강은 잔디에만 닿게 판다.
        WangTileset set = (lo, hi) switch
        {
            (TerrainGrass, TerrainGrass) or (TerrainGrass, TerrainDirt)
                or (TerrainDirt, TerrainDirt) => _grassDirt,
            (TerrainDirt, TerrainStone) or (TerrainStone, TerrainStone) => _dirtStone,
            (TerrainGrass, TerrainStone) => _grassStone,
            (TerrainGrass, TerrainWater) or (TerrainWater, TerrainWater) => _grassWater,
            _ => null,
        };
        if (set == null)
            return false;

        int upper = hi == lo ? UpperOf(set) : hi;
        int wang = (nw == upper ? 8 : 0) + (ne == upper ? 4 : 0)
                 + (sw == upper ? 2 : 0) + (se == upper ? 1 : 0);
        if (!set.Has(wang))
            return false;

        SetCell(new Vector2I(x, y), set.SourceId, set.Atlas(wang));
        return true;
    }

    /// <summary>그 타일셋에서 'upper' 쪽 지형이 무엇인가. 네 모서리가 같을 때 쓴다.</summary>
    private int UpperOf(WangTileset set)
    {
        if (set == _grassDirt) return TerrainDirt;
        if (set == _dirtStone) return TerrainStone;
        if (set == _grassStone) return TerrainStone;
        return TerrainWater;
    }

    private static int DistinctCount(int a, int b, int c, int d)
    {
        int mask = (1 << a) | (1 << b) | (1 << c) | (1 << d);
        int n = 0;
        while (mask != 0) { n += mask & 1; mask >>= 1; }
        return n;
    }

    private static int Promote(int terrain) => terrain == TerrainDirt ? TerrainStone : terrain;

    /// <summary>
    /// 훈련장 바닥. 울타리는 타일이 아니라 목책 스프라이트로 세운다 —
    /// 단색 갈색 타일로 두르면 마당이 아니라 도면에 그린 사각형처럼 보였다.
    /// (목책은 TownGenerator.FenceYard 가 놓는다)
    /// </summary>
    private void PaintTrainingYard()
    {
        var yard=_town.TrainingYard;
        Vector2 center=(Vector2)yard.Position+(Vector2)yard.Size*.5f;
        for(int y=yard.Position.Y;y<yard.End.Y;y++) for(int x=yard.Position.X;x<yard.End.X;x++)
            if(((new Vector2(x+.5f,y+.5f)-center)/new Vector2(3.5f,2.5f)).LengthSquared()<1f)
                SetFloor(x,y,TileDirtA,TileDirtB);
    }

    private void FillRect(Rect2I r, int tileA, int tileB)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
                SetFloor(x, y, tileA, tileB);
        }
    }

    private void FillSolid(Rect2I r, int tile)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
                SetCellTile(x, y, tile);
        }
    }

    private void SetCellTile(int x, int y, int tile)
        => SetCell(new Vector2I(x, y), SourceId, new Vector2I(tile, 0));

    /// <summary>
    /// F8 손맛 테스트장. 마을과 완전히 떨어진 좌표(WorldLayout.TestArenaTiles)에
    /// 돌바닥 방을 하나 찍는다 — 칠하지 않으면 빈 칸은 기본이 막힌 칸이라 못 들어간다.
    /// </summary>
    private void PaintTestArena(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
        {
            bool border = x == r.Position.X || x == r.Position.X + r.Size.X - 1
                || y == r.Position.Y || y == r.Position.Y + r.Size.Y - 1;
            SetCellTile(x, y, border ? TileWall : TileStoneA);
        }
    }

    /// <summary>초원을 세로로 가르는 강(물)과, 중앙 통로의 다리를 찍는다.</summary>
    private void PaintEncounterPaths()
    {
        var zone = WorldLayout.Meadow.Tiles;
        // Only existing meadow terrain is changed; river and bridge are painted afterward.
        foreach (var camp in MeadowLayout.Data.Camps)
            for (int y = camp.Y - 2; y <= camp.Y + 2; y++)
                for (int x = camp.X - 2; x <= camp.X + 2; x++)
                    if (new Vector2(x - camp.X, y - camp.Y).Length() <= 2f)
                        SetFloor(x, y, TileDirtA, TileDirtB);
        foreach (var route in MeadowLayout.Data.Routes)
            for (int i = 1; i < route.Length; i++)
            {
                Vector2 a = new(route[i - 1][0], route[i - 1][1]);
                Vector2 b = new(route[i][0], route[i][1]);
                int steps = Mathf.CeilToInt(a.DistanceTo(b) * 2f);
                for (int n = 0; n <= steps; n++)
                {
                    var c = (Vector2I)a.Lerp(b, n / (float)Mathf.Max(1, steps)).Round();
                    if (zone.HasPoint(c)) SetFloor(c.X, c.Y, TileDirtA, TileDirtB);
                }
            }
    }

    private void PaintRiver()
    {
        Rect2I r = MeadowRiver;
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                int tile = (x + y) % 2 == 0 ? TileWaterA : TileWaterB;
                SetCell(new Vector2I(x, y), SourceId, new Vector2I(tile, 0));
            }
        }

        Rect2I b = MeadowBridge;
        for (int y = b.Position.Y; y < b.Position.Y + b.Size.Y; y++)
        {
            for (int x = b.Position.X; x < b.Position.X + b.Size.X; x++)
                SetCell(new Vector2I(x, y), SourceId, new Vector2I(TileBridge, 0));
        }

        // 강가를 픽셀아트 물가 타일로 바꾼다. 단색 파란 사각형이 마을 밖
        // 유일한 통행 지형이라, 여기만 도형으로 남으면 눈에 제일 먼저 걸린다.
        ApplyTerrainIn(r.Grow(2), "강");
        BlockRiver(r, b);
    }

    /// <summary>
    /// 강을 다시 막는다.
    ///
    /// 물 칸은 원래 SourceId 타일이라 타일셋에 붙은 충돌 폴리곤이 막아 줬는데,
    /// 위에서 Wang 지형으로 덮으면서 그 폴리곤이 사라졌다 — Wang 소스에는 바닥만
    /// 들어 있다. 그대로 두면 강을 걸어서 건널 수 있어서 다리가 의미를 잃는다.
    ///
    /// 강은 세로 사각형 하나에 다리가 가운데를 끊는 모양이라 사각형 둘이면 끝난다.
    /// </summary>
    private void BlockRiver(Rect2I river, Rect2I bridge)
    {
        var body = new StaticBody2D { Name = "RiverBlock" };
        body.CollisionLayer = CollisionLayers.World;
        body.CollisionMask = 0;
        AddChild(body);

        int t = WorldLayout.TileSize;
        Add(river.Position.Y, bridge.Position.Y);
        Add(bridge.Position.Y + bridge.Size.Y, river.Position.Y + river.Size.Y);

        void Add(int top, int bottom)
        {
            if (bottom <= top)
                return;

            var shape = new RectangleShape2D
            {
                Size = new Vector2(river.Size.X * t, (bottom - top) * t),
            };
            body.AddChild(new CollisionShape2D
            {
                Shape = shape,
                Position = new Vector2(
                    (river.Position.X + river.Size.X * 0.5f) * t,
                    (top + (bottom - top) * 0.5f) * t),
            });
        }
    }

    /// <summary>
    /// 다리 그림. 타일(TileBridge)이 통행을 맡고, 이 그림이 그 위를 덮는다.
    /// bridge.png 는 160x96 = 5x3 칸이라 배치 사각형도 그 크기여야 한다.
    /// </summary>
    private PropPlacement BridgeProp()
    {
        Rect2I b = MeadowBridge;
        int cx = b.Position.X + b.Size.X / 2;
        int cy = b.Position.Y + b.Size.Y / 2;
        return new PropPlacement(new Rect2I(cx - 2, cy - 1, 5, 3), "bridge", solid: false);
    }

    private void CarveZone(ZoneDef zone, int tileA, int tileB)
    {
        Rect2I r = zone.Tiles;
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
                SetFloor(x, y, tileA, tileB);
        }
    }



    private void SetFloor(int x, int y, int tileA, int tileB)
    {
        // 좌표 해시로 고른다. (x+y)%2 는 정확히 바둑판이라, 밝기 차가 아무리
        // 작아도 격자가 눈에 남는다. 해시는 같은 좌표면 같은 값이라 실행마다 같다.
        int tile = Hash(x, y, 4441) < 0.5f ? tileA : tileB;
        SetCell(new Vector2I(x, y), SourceId, new Vector2I(tile, 0));
    }

    private void SetWall(int x, int y)
        => SetCell(new Vector2I(x, y), SourceId, new Vector2I(TileWall, 0));
}
