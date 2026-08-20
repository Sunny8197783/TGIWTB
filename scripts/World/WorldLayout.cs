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
    /// <summary>
    /// 32px 그리드. 아트 규격을 high top-down / 32px 로 옮기면서 16 → 32 가 됐다.
    /// 타일 수를 절반으로 줄여 월드의 픽셀 크기는 그대로 뒀다 — 타일만 키우면
    /// 월드가 두 배가 되어 이미 넓은 마을을 감당할 수 없다.
    /// </summary>
    public const int TileSize = 32;

    /// <summary>5376 / 32. 32px 로 옮기면서 타일 수는 절반이 됐다(픽셀 크기 동일).</summary>
    public const int WidthTiles = 168;

    /// <summary>3072 / 32.</summary>
    public const int HeightTiles = 96;

    public static readonly Vector2 WorldSizePx = new(WidthTiles * TileSize, HeightTiles * TileSize);

    /// <summary>바깥 테두리 벽 두께(타일).</summary>
    public const int BorderThickness = 1;

    /// <summary>구역을 나누는 벽 두께(타일).</summary>
    public const int WallThickness = 1;

    /// <summary>통로 폭(타일).</summary>
    public const int GateHeight = 3;

    public static readonly ZoneDef Town = new()
    {
        Id = "town",
        DisplayName = "초보자 마을",
        Tiles = new Rect2I(1, 1, 94, 94),
        Safe = true,
        // 마을 바닥은 잔디다. 길·광장은 TileWorld 가 그 위에 덮는다.
        FloorColor = new Color(0.22f, 0.34f, 0.20f),
    };

    public static readonly ZoneDef Meadow = new()
    {
        Id = "meadow",
        DisplayName = "초원",
        Tiles = new Rect2I(96, 1, 50, 94),
        Safe = false,
        FloorColor = new Color(0.16f, 0.26f, 0.18f),
    };

    public static readonly ZoneDef IronjawDen = new()
    {
        Id = "ironjaw_den",
        DisplayName = "철턱의 굴",
        Tiles = new Rect2I(147, 1, 20, 94),
        Safe = false,
        FloorColor = new Color(0.26f, 0.16f, 0.16f),
    };

    public static readonly ZoneDef[] Zones = { Town, Meadow, IronjawDen };

    /// <summary>부활 지점. 마을 광장 한가운데. (§D-1 사망 시 마을 지점 부활)</summary>
    public static Vector2 SpawnPoint => Town.WorldCenter;

    /// <summary>타일 좌표 → 그 타일 중심의 월드 좌표.</summary>
    public static Vector2 TileCenter(int x, int y)
        => new((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);

    // 마을 내부 배치(거리·구획·건물)는 VillagePlan 에 있다.
    // 여기는 구역과 월드 크기까지만 정한다.

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
