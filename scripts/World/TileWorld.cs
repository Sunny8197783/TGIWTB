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
    private const int TileTownA = 0;
    private const int TileTownB = 1;
    private const int TileMeadowA = 2;
    private const int TileMeadowB = 3;
    private const int TileDenA = 4;
    private const int TileDenB = 5;
    private const int TileWall = 6;
    private const int TileCount = 7;

    private const int SourceId = 0;
    private const int PhysicsLayer = 0;

    /// <summary>같은 구역 안에서 체커보드로 밝기를 살짝 흔든다 — 움직임이 눈에 보이게.</summary>
    private static readonly float CheckerShade = 0.06f;

    /// <summary>초원에 박아 둘 장애물 기둥 (타일 좌표). 이동에 리듬을 준다.</summary>
    private static readonly Rect2I[] MeadowPillars =
    {
        new(44, 14, 4, 4),
        new(60, 30, 5, 3),
        new(48, 48, 3, 5),
        new(72, 20, 3, 6),
        new(70, 46, 6, 3),
    };

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
            && GetCellAtlasCoords(cell).X != TileWall;
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

        // 벽 타일만 충돌 폴리곤을 가진다. 좌표는 타일 중심 기준.
        float half = WorldLayout.TileSize * 0.5f;
        TileData wall = source.GetTileData(new Vector2I(TileWall, 0), 0);
        wall.SetCollisionPolygonsCount(PhysicsLayer, 1);
        wall.SetCollisionPolygonPoints(PhysicsLayer, 0, new[]
        {
            new Vector2(-half, -half),
            new Vector2(half, -half),
            new Vector2(half, half),
            new Vector2(-half, half),
        });

        return tileSet;
    }

    private static ImageTexture BuildAtlasTexture()
    {
        int size = WorldLayout.TileSize;
        var image = Image.CreateEmpty(size * TileCount, size, false, Image.Format.Rgba8);

        var wallColor = new Color(0.10f, 0.10f, 0.13f);
        Fill(image, TileTownA, WorldLayout.Town.FloorColor);
        Fill(image, TileTownB, Shade(WorldLayout.Town.FloorColor));
        Fill(image, TileMeadowA, WorldLayout.Meadow.FloorColor);
        Fill(image, TileMeadowB, Shade(WorldLayout.Meadow.FloorColor));
        Fill(image, TileDenA, WorldLayout.IronjawDen.FloorColor);
        Fill(image, TileDenB, Shade(WorldLayout.IronjawDen.FloorColor));
        Fill(image, TileWall, wallColor);

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
        CarveZone(WorldLayout.Town, TileTownA, TileTownB);
        CarveZone(WorldLayout.Meadow, TileMeadowA, TileMeadowB);
        CarveZone(WorldLayout.IronjawDen, TileDenA, TileDenB);

        // 3) 구역 사이 통로.
        CarveGate(WorldLayout.Town, WorldLayout.Meadow, TileMeadowA, TileMeadowB);
        CarveGate(WorldLayout.Meadow, WorldLayout.IronjawDen, TileDenA, TileDenB);

        // 4) 초원 장애물.
        foreach (Rect2I pillar in MeadowPillars)
        {
            for (int y = pillar.Position.Y; y < pillar.Position.Y + pillar.Size.Y; y++)
            {
                for (int x = pillar.Position.X; x < pillar.Position.X + pillar.Size.X; x++)
                    SetWall(x, y);
            }
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
