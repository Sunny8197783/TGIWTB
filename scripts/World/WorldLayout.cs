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

    /// <summary>
    /// 2144 / 16. 마을을 30 → 44 타일로 넓히면서 초원·굴을 오른쪽으로 밀었다.
    /// 세로로 긴 골목처럼 보이던 마을을 광장·길이 들어갈 만한 폭으로 만들기 위함.
    /// </summary>
    public const int WidthTiles = 134;

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
        DisplayName = "초보자 마을",
        Tiles = new Rect2I(2, 2, 44, 64),
        Safe = true,
        // 마을 바닥은 잔디다. 길·광장은 TileWorld 가 그 위에 덮는다.
        FloorColor = new Color(0.22f, 0.34f, 0.20f),
    };

    public static readonly ZoneDef Meadow = new()
    {
        Id = "meadow",
        DisplayName = "초원",
        Tiles = new Rect2I(48, 2, 52, 64),
        Safe = false,
        FloorColor = new Color(0.16f, 0.26f, 0.18f),
    };

    public static readonly ZoneDef IronjawDen = new()
    {
        Id = "ironjaw_den",
        DisplayName = "철턱의 굴",
        Tiles = new Rect2I(102, 2, 30, 64),
        Safe = false,
        FloorColor = new Color(0.26f, 0.16f, 0.16f),
    };

    public static readonly ZoneDef[] Zones = { Town, Meadow, IronjawDen };

    /// <summary>부활 지점. 마을 광장 한가운데. (§D-1 사망 시 마을 지점 부활)</summary>
    public static Vector2 SpawnPoint => Town.WorldCenter;

    /// <summary>타일 좌표 → 그 타일 중심의 월드 좌표.</summary>
    public static Vector2 TileCenter(int x, int y)
        => new((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);

    /// <summary>
    /// 초보자 마을 배치. 좌표는 전부 타일 단위이고 Town(2,2,44,64) 안에 들어간다.
    ///
    /// 구조는 십자로다 — 가로길이 동쪽 성문(초원 통로)까지 곧게 이어지고, 세로길이
    /// 그 길을 가로지른다. 교차점에 광장을 두고 광장 한가운데가 부활 지점이다.
    /// 길이 만든 네 구획 중 셋에 집을 넣고, 남동 구획은 훈련장으로 비웠다.
    /// </summary>
    public static class Village
    {
        /// <summary>가로 큰길. 동쪽 통로(y31~36) 안에 들어가도록 y32~35.</summary>
        public static readonly Rect2I MainRoad = new(2, 32, 44, 4);

        /// <summary>세로 길. 마을을 남북으로 관통한다.</summary>
        public static readonly Rect2I CrossRoad = new(22, 2, 4, 64);

        /// <summary>
        /// 광장 둘레의 흙 마당. 돌바닥이 잔디에 직접 닿지 않게 하는 완충대다.
        ///
        /// Wang 타일셋은 '두 지형 사이의 전환'만 담는다. 돌이 잔디에도 닿으면
        /// 잔디↔돌 타일셋이 한 벌 더 필요해진다. 돌을 흙으로만 감싸면
        /// 잔디↔흙, 흙↔돌 두 벌로 마을 전체를 덮을 수 있다.
        /// </summary>
        public static readonly Rect2I PlazaSkirt = new(16, 26, 16, 16);

        /// <summary>광장 — 두 길의 교차점을 덮는 돌바닥.</summary>
        public static readonly Rect2I Plaza = new(17, 27, 14, 14);

        /// <summary>광장 북서쪽 우물. 못 지나간다.</summary>
        public static readonly Rect2I Well = new(18, 29, 2, 2);

        /// <summary>집. 전부 6x5 이고 아래 가운데 2칸이 문이다.</summary>
        public static readonly Rect2I[] Houses =
        {
            new(5, 8, 6, 5),   new(14, 8, 6, 5),    // 북서 구획
            new(5, 18, 6, 5),  new(14, 18, 6, 5),
            new(28, 8, 6, 5),  new(37, 8, 6, 5),    // 북동 구획
            new(28, 18, 6, 5), new(37, 18, 6, 5),
            new(5, 44, 6, 5),  new(14, 44, 6, 5),   // 남서 구획
            new(5, 54, 6, 5),  new(14, 54, 6, 5),
        };

        /// <summary>훈련장 울타리. 테두리만 막고 안은 흙바닥이다.</summary>
        public static readonly Rect2I TrainingYard = new(30, 44, 13, 13);

        /// <summary>울타리 서쪽 출입구가 뚫리는 세로 구간 (큰길에서 들어온다).</summary>
        public static readonly int YardGateY = 49;
        public static readonly int YardGateHeight = 3;

        /// <summary>훈련장 한가운데 — 허수아비 자리.</summary>
        public static Vector2 TrainingDummySpot => TileCenter(36, 50);
    }

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
