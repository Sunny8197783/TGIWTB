using Godot;

namespace PixelMmo.Runtime;

/// <summary>구역 1개. 좌표는 전부 타일 단위. (§G)</summary>
public sealed class ZoneDef
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public Rect2I Tiles { get; init; }

    /// <summary>안전지대 — 몬스터가 스폰되지 않고, 진입 시 자동 저장한다.</summary>
    public bool Safe { get; init; }

    public Color FloorColor { get; init; }

    public Rect2 WorldRect => new(
        Tiles.Position.X * WorldLayout.TileSize,
        Tiles.Position.Y * WorldLayout.TileSize,
        Tiles.Size.X * WorldLayout.TileSize,
        Tiles.Size.Y * WorldLayout.TileSize);

    public Vector2 WorldCenter => WorldRect.Position + WorldRect.Size * 0.5f;

    public bool Contains(Vector2 worldPosition) => WorldRect.HasPoint(worldPosition);
}

/// <summary>
/// 맵 형태의 유일한 정의. 타일 16px, 월드 1920x1080 (화면 10개 분량). (§G)
/// 도형 타일이므로 여기 숫자만 고치면 맵이 바뀐다.
/// </summary>
public static class WorldLayout
{
    public const int TileSize = 16;

    /// <summary>1920 / 16</summary>
    public const int WidthTiles = 120;

    /// <summary>1088 / 16 — 명세의 1080 에 가장 가까운 타일 배수.</summary>
    public const int HeightTiles = 68;

    public static readonly Vector2 WorldSizePx = new(WidthTiles * TileSize, HeightTiles * TileSize);

    /// <summary>바깥 테두리 벽 두께(타일).</summary>
    public const int BorderThickness = 2;

    /// <summary>구역을 나누는 벽 두께(타일).</summary>
    public const int WallThickness = 2;

    /// <summary>통로 폭(타일).</summary>
    public const int GateHeight = 6;

    public static readonly ZoneDef Town = new()
    {
        Id = "town",
        DisplayName = "마을",
        Tiles = new Rect2I(2, 2, 30, 64),
        Safe = true,
        FloorColor = new Color(0.20f, 0.24f, 0.30f),
    };

    public static readonly ZoneDef Meadow = new()
    {
        Id = "meadow",
        DisplayName = "초원",
        Tiles = new Rect2I(34, 2, 52, 64),
        Safe = false,
        FloorColor = new Color(0.16f, 0.26f, 0.18f),
    };

    public static readonly ZoneDef IronjawDen = new()
    {
        Id = "ironjaw_den",
        DisplayName = "철턱의 굴",
        Tiles = new Rect2I(88, 2, 30, 64),
        Safe = false,
        FloorColor = new Color(0.26f, 0.16f, 0.16f),
    };

    public static readonly ZoneDef[] Zones = { Town, Meadow, IronjawDen };

    /// <summary>부활 지점. 마을 중앙. (§D-1 사망 시 마을 지점 부활)</summary>
    public static Vector2 SpawnPoint => Town.WorldCenter;

    public static ZoneDef ZoneAt(Vector2 worldPosition)
    {
        foreach (var zone in Zones)
        {
            if (zone.Contains(worldPosition))
                return zone;
        }
        return null;
    }

    public static Rect2 WorldBounds => new(Vector2.Zero, WorldSizePx);
}
