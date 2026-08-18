using Godot;
using PixelMmo.Combat;
using Village = PixelMmo.Runtime.WorldLayout.Village;

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
    private const int TileCount = 18;

    /// <summary>벽·건물·물·울타리면 막힌 칸. 다리(TileBridge)는 통행 가능.</summary>
    private static bool IsSolid(int tileX) => tileX == TileWall
        || (tileX >= TileHouseA && tileX <= TileHouseC)
        || tileX == TileWaterA || tileX == TileWaterB
        || tileX == TileFence;

    private const int SourceId = 0;
    private const int PhysicsLayer = 0;

    /// <summary>같은 구역 안에서 체커보드로 밝기를 살짝 흔든다 — 움직임이 눈에 보이게.</summary>
    private static readonly float CheckerShade = 0.06f;

    /// <summary>초원에 박아 둘 장애물 기둥 (타일 좌표). 이동에 리듬을 준다.</summary>
    private static readonly Rect2I[] MeadowPillars =
    {
        new(44, 14, 4, 4),
        new(48, 48, 3, 5),
        new(72, 20, 3, 6),
        new(70, 46, 6, 3),
    };

    /// <summary>초원을 가로지르는 세로 강(물). 못 지나감.</summary>
    private static readonly Rect2I MeadowRiver = new(56, 2, 4, 64);   // x56~59, y2~65

    /// <summary>강을 건너는 다리 — 통로 높이에 맞춰 중앙에. (게이트 중앙 y=34)</summary>
    private static readonly Rect2I MeadowBridge = new(56, 31, 4, 6);  // x56~59, y31~36

    public override void _Ready()
    {
        Name = "TileWorld";
        TileSet = BuildTileSet();
        Paint();
    }

    /// <summary>해당 월드 좌표가 벽이 아닌가.</summary>
    public bool IsWalkable(Vector2 worldPosition)
    {
        Vector2I cell = LocalToMap(ToLocal(worldPosition));
        return GetCellSourceId(cell) == SourceId
            && !IsSolid(GetCellAtlasCoords(cell).X);
    }

    private static TileSet BuildTileSet()
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

        return tileSet;
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

        return ImageTexture.CreateFromImage(image);
    }

    private static void Fill(Image image, int tileIndex, Color color)
    {
        int size = WorldLayout.TileSize;
        image.FillRect(new Rect2I(tileIndex * size, 0, size, size), color);
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
        FillRect(Village.MainRoad, TileDirtA, TileDirtB);
        FillRect(Village.CrossRoad, TileDirtA, TileDirtB);
        FillRect(Village.Plaza, TileStoneA, TileStoneB);

        // 집 — 지붕색 3종을 돌려 쓴다. 한 줄에 같은 색이 붙지 않는다.
        int[] roofs = { TileHouseA, TileHouseB, TileHouseC };
        for (int i = 0; i < Village.Houses.Length; i++)
        {
            Rect2I h = Village.Houses[i];
            StampHouse(h.Position.X, h.Position.Y, h.Size.X, h.Size.Y, roofs[i % roofs.Length]);
        }

        PaintTrainingYard();

        // 우물 — 광장 돌바닥 위에 올린다.
        FillSolid(Village.Well, TileWaterA);
    }

    /// <summary>훈련장 — 안은 흙바닥, 테두리는 울타리. 서쪽에 출입구를 낸다.</summary>
    private void PaintTrainingYard()
    {
        Rect2I y = Village.TrainingYard;

        FillRect(y, TileDirtA, TileDirtB);

        int x0 = y.Position.X, y0 = y.Position.Y;
        int x1 = x0 + y.Size.X - 1, y1 = y0 + y.Size.Y - 1;
        for (int x = x0; x <= x1; x++)
        {
            SetCellTile(x, y0, TileFence);
            SetCellTile(x, y1, TileFence);
        }
        for (int yy = y0; yy <= y1; yy++)
        {
            SetCellTile(x0, yy, TileFence);
            SetCellTile(x1, yy, TileFence);
        }

        // 서쪽 출입구 — 큰길 쪽에서 걸어 들어온다.
        for (int yy = Village.YardGateY; yy < Village.YardGateY + Village.YardGateHeight; yy++)
            SetFloor(x0, yy, TileDirtA, TileDirtB);
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

    /// <summary>집 하나 — 지붕색 블록 + 아래 가운데 2칸 문(바닥으로 뚫음).</summary>
    private void StampHouse(int x, int y, int w, int h, int tile)
    {
        for (int yy = y; yy < y + h; yy++)
        {
            for (int xx = x; xx < x + w; xx++)
                SetCell(new Vector2I(xx, yy), SourceId, new Vector2I(tile, 0));
        }

        // 문 앞은 밟고 다니는 자리라 흙으로 둔다 — 집이 길을 향한다는 표시.
        int doorX = x + w / 2 - 1;
        int doorY = y + h - 1;
        SetFloor(doorX, doorY, TileDirtA, TileDirtB);
        SetFloor(doorX + 1, doorY, TileDirtA, TileDirtB);
    }

    private void SetFloor(int x, int y, int tileA, int tileB)
    {
        int tile = (x + y) % 2 == 0 ? tileA : tileB;
        SetCell(new Vector2I(x, y), SourceId, new Vector2I(tile, 0));
    }

    private void SetWall(int x, int y)
        => SetCell(new Vector2I(x, y), SourceId, new Vector2I(TileWall, 0));
}
