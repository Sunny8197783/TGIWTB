using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// 16px 타일맵을 코드로 만든다. 스프라이트를 그리지 않는다 — 타일 텍스처는
/// 단색 사각형을 런타임에 찍어서 만든 플레이스홀더다. (CLAUDE.md 규칙 5)
/// </summary>
public partial class TileWorld : TileMapLayer
{
    // 아틀라스 타일 인덱스 (x 좌표)
    private const int TileGrassA = 0;      // 마을 잔디
    private const int TileGrassB = 1;
    private const int TileMeadowA = 2;
    private const int TileMeadowB = 3;
    private const int TileDenA = 4;
    private const int TileDenB = 5;
    private const int TileWall = 6;

    // 건물(집) — 지붕색만 다른 플레이스홀더. 전부 충돌한다(못 지나감). (규칙 5)
    private const int TileHouseA = 7;
    private const int TileHouseB = 8;
    private const int TileHouseC = 9;

    // 강(물) — 못 지나감. 다리 — 건널 수 있음.
    private const int TileWaterA = 10;
    private const int TileWaterB = 11;
    private const int TileBridge = 12;

    // 마을 길(흙)과 광장(돌), 훈련장 울타리.
    private const int TileDirtA = 13;
    private const int TileDirtB = 14;
    private const int TileStoneA = 15;
    private const int TileStoneB = 16;
    private const int TileFence = 17;

    /// <summary>
    /// 마을을 두르는 돌 성벽. 한 색으로 3칸을 칠하면 회색 띠일 뿐이라
    /// 위(볕 드는 흉벽) · 몸통 · 아래(그늘) 세 단으로 나눠 두께를 보이게 한다.
    /// </summary>
    private const int TileTownWallTop = 18;
    private const int TileTownWall = 19;
    private const int TileTownWallBase = 20;
    private const int TileCount = 21;

    /// <summary>벽·건물·물·울타리면 막힌 칸. 다리(TileBridge)는 통행 가능.</summary>
    private static bool IsSolid(int tileX) => tileX == TileWall
        || (tileX >= TileHouseA && tileX <= TileHouseC)
        || tileX == TileWaterA || tileX == TileWaterB
        || tileX == TileFence
        || tileX == TileTownWallTop || tileX == TileTownWall || tileX == TileTownWallBase;

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

    /// <summary>훈련장 허수아비 자리. GameWorld 가 개체를 놓을 때 쓴다.</summary>
    public Vector2 TrainingDummySpot => _town?.TrainingDummySpot ?? WorldLayout.SpawnPoint;

    /// <summary>같은 구역 안에서 체커보드로 밝기를 살짝 흔든다 — 움직임이 눈에 보이게.</summary>
    private static readonly float CheckerShade = 0.06f;

    // 초원 안의 것들은 초원 왼쪽 끝을 기준으로 잡는다.
    // 예전에는 절대 좌표라, 마을을 넓혀 초원을 오른쪽으로 밀었을 때 강과 바위가
    // 마을 한복판에 남아 있었다. 구역이 움직이면 같이 움직여야 한다.
    private static Rect2I MeadowTiles => WorldLayout.Meadow.Tiles;
    private static int MeadowX(float frac)
        => MeadowTiles.Position.X + (int)(MeadowTiles.Size.X * frac);
    private static int MeadowY(float frac)
        => MeadowTiles.Position.Y + (int)(MeadowTiles.Size.Y * frac);

    /// <summary>초원에 박아 둘 장애물 기둥. 이동에 리듬을 준다.</summary>
    private static Rect2I[] MeadowPillars => new Rect2I[]
    {
        new(MeadowX(0.12f), MeadowY(0.15f), 5, 5),
        new(MeadowX(0.18f), MeadowY(0.70f), 4, 6),
        new(MeadowX(0.72f), MeadowY(0.22f), 4, 7),
        new(MeadowX(0.66f), MeadowY(0.68f), 7, 4),
    };

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
            _props = new VillageProps(_town.Props);
            ReportBlockedRoads();
        }
    }

    /// <summary>
    /// 길 위인데 막혀 있는 칸을 센다.
    ///
    /// "건물에 닿지도 않았는데 막힌다"는 건 눈으로는 잘 안 보인다 — 걸어 보다
    /// 우연히 걸려야 안다. 건물 충돌은 그림에서 뽑으므로 캔버스가 크거나
    /// 그림이 캔버스 밖으로 삐져나오면 길을 덮을 수 있다. 그러면 여기 숫자가 뛴다.
    /// </summary>
    private void ReportBlockedRoads()
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

                if (_props.IsBlocked(WorldLayout.TileCenter(town.Position.X + x, town.Position.Y + y)))
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
        Fill(image, TileGrassA, WorldLayout.Town.FloorColor);
        Fill(image, TileGrassB, Shade(WorldLayout.Town.FloorColor));
        Fill(image, TileMeadowA, WorldLayout.Meadow.FloorColor);
        Fill(image, TileMeadowB, Shade(WorldLayout.Meadow.FloorColor));
        Fill(image, TileDenA, WorldLayout.IronjawDen.FloorColor);
        Fill(image, TileDenB, Shade(WorldLayout.IronjawDen.FloorColor));
        Fill(image, TileWall, wallColor);

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

    private void Paint()
    {
        Clear();

        // 1) 전부 벽으로 채운다.
        for (int y = 0; y < WorldLayout.HeightTiles; y++)
        {
            for (int x = 0; x < WorldLayout.WidthTiles; x++)
                SetWall(x, y);
        }

        // 2) 구역 내부를 바닥으로 판다.
        CarveZone(WorldLayout.Town, TileGrassA, TileGrassB);
        CarveZone(WorldLayout.Meadow, TileMeadowA, TileMeadowB);
        CarveZone(WorldLayout.IronjawDen, TileDenA, TileDenB);

        // 3) 마을.
        PaintVillage();

        // 4) 구역 사이 통로.
        CarveGate(WorldLayout.Town, WorldLayout.Meadow, TileMeadowA, TileMeadowB);
        CarveGate(WorldLayout.Meadow, WorldLayout.IronjawDen, TileDenA, TileDenB);

        // 5) 초원 장애물.
        foreach (Rect2I pillar in MeadowPillars)
        {
            for (int y = pillar.Position.Y; y < pillar.Position.Y + pillar.Size.Y; y++)
            {
                for (int x = pillar.Position.X; x < pillar.Position.X + pillar.Size.X; x++)
                    SetWall(x, y);
            }
        }

        // 6) 강 + 다리 — 다른 것 위에 덮어써야 하니 마지막에.
        PaintRiver();
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

        ApplyVillageTerrain();
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
        if (!_terrainReady)
            return;

        Rect2I town = WorldLayout.Town.Tiles;
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

        GD.Print($"[TileWorld] 마을 바닥 {painted}칸 픽셀아트로 교체"
            + (skipped > 0 ? $", {skipped}칸은 도형 유지(지형 조합 없음)" : ""));

        ApplyTownWall();
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
    private int TerrainOfCell(int x, int y) => GetCellAtlasCoords(new Vector2I(x, y)).X switch
    {
        TileGrassA or TileGrassB => TerrainGrass,
        TileDirtA or TileDirtB => TerrainDirt,
        TileStoneA or TileStoneB => TerrainStone,
        TileWaterA or TileWaterB => TerrainWater,
        _ => TerrainNone,
    };

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
    private void PaintTrainingYard() => FillRect(_town.TrainingYard, TileDirtA, TileDirtB);

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

    /// <summary>초원을 세로로 가르는 강(물)과, 중앙 통로의 다리를 찍는다.</summary>
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

    /// <summary>두 구역 사이의 벽을 세로 중앙에서 GateHeight 만큼 뚫는다.</summary>
    private void CarveGate(ZoneDef left, ZoneDef right, int tileA, int tileB)
    {
        int fromX = left.Tiles.Position.X + left.Tiles.Size.X;
        int toX = right.Tiles.Position.X;
        int centerY = left.Tiles.Position.Y + left.Tiles.Size.Y / 2;
        int startY = centerY - WorldLayout.GateHeight / 2;

        for (int y = startY; y < startY + WorldLayout.GateHeight; y++)
        {
            for (int x = fromX; x < toX; x++)
                SetFloor(x, y, tileA, tileB);
        }
    }



    private void SetFloor(int x, int y, int tileA, int tileB)
    {
        int tile = (x + y) % 2 == 0 ? tileA : tileB;
        SetCell(new Vector2I(x, y), SourceId, new Vector2I(tile, 0));
    }

    private void SetWall(int x, int y)
        => SetCell(new Vector2I(x, y), SourceId, new Vector2I(TileWall, 0));
}
