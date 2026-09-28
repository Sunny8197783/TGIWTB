using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// 구역 바닥의 결. 같은 초록이라도 결이 다르면 다른 땅으로 읽힌다 —
/// 32px 타일 안에 무늬를 직접 찍어서 단색 사각형을 면하게 한다. (§A 아트 방침)
/// </summary>
public enum GroundPattern
{
    /// <summary>풀포기. 잔디·초원.</summary>
    Grass,
    /// <summary>낙엽과 잔가지. 숲.</summary>
    Leaf,
    /// <summary>물결과 부들. 늪.</summary>
    Bog,
    /// <summary>깨진 자갈. 채석장·굴.</summary>
    Rubble,
    /// <summary>다듬은 포석. 던전 바닥.</summary>
    Flagstone,
    /// <summary>서리 결정. 고원.</summary>
    Frost,
    /// <summary>갈라진 틈에서 새는 불빛. 화산.</summary>
    Ember,
    /// <summary>마른 모래와 뼛조각.</summary>
    Bone,
}

/// <summary>구역 1개. 좌표는 전부 타일 단위. (§G)</summary>
public sealed class ZoneDef
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public Rect2I Tiles { get; init; }

    /// <summary>안전지대 — 몬스터가 스폰되지 않고, 진입 시 자동 저장한다.</summary>
    public bool Safe { get; init; }

    public Color FloorColor { get; init; }

    /// <summary>바위·기둥 등 그 구역의 막힌 지형 색. 비워 두면 바닥색을 눌러서 쓴다.</summary>
    public Color AccentColor { get; init; }

    public GroundPattern Pattern { get; init; } = GroundPattern.Grass;

    /// <summary>
    /// 위험도 1~5. 스폰 밀도와 어느 몬스터가 나오는지를 정한다.
    /// 마을은 0 — 안전지대라 어차피 스폰하지 않는다.
    /// </summary>
    public int Danger { get; init; }

    /// <summary>실내(던전)인가. 바닥을 어둡게 깔고 바위를 촘촘히 넣는다.</summary>
    public bool Indoor { get; init; }

    /// <summary>이 구역에 흩뿌릴 바위 덩어리 수. 0 이면 뻥 뚫린 벌판.</summary>
    public int Boulders { get; init; }

    public Rect2 WorldRect => new(
        Tiles.Position.X * WorldLayout.TileSize,
        Tiles.Position.Y * WorldLayout.TileSize,
        Tiles.Size.X * WorldLayout.TileSize,
        Tiles.Size.Y * WorldLayout.TileSize);

    public Vector2 WorldCenter => WorldRect.Position + WorldRect.Size * 0.5f;

    public bool Contains(Vector2 worldPosition)
    {
        if (Id == "meadow") return WorldLayout.IsForest(worldPosition / WorldLayout.TileSize);
        if (Indoor) return WorldRect.HasPoint(worldPosition);
        Vector2 d = (worldPosition - WorldCenter) / (WorldRect.Size * .5f);
        return d.LengthSquared() <= 1f;
    }

    /// <summary>막힌 지형 색. 지정이 없으면 바닥색을 눌러서 쓴다.</summary>
    public Color SolidColor => AccentColor.A > 0f
        ? AccentColor
        : new Color(FloorColor.R * 0.45f, FloorColor.G * 0.45f, FloorColor.B * 0.48f);
}

/// <summary>두 구역을 잇는 통로 하나. 어느 쪽이 먼저인지는 상관없다.</summary>
public readonly struct ZoneLink
{
    public ZoneLink(ZoneDef a, ZoneDef b) { A = a; B = b; }
    public ZoneDef A { get; }
    public ZoneDef B { get; }
}

/// <summary>
/// 맵 형태의 유일한 정의. 타일 32px. (§G)
/// 도형 타일이므로 여기 숫자만 고치면 맵이 바뀐다.
/// </summary>
public static class WorldLayout
{
    public const int TileSize = 32, WidthTiles = 400, HeightTiles = 288;
    public const int BorderThickness = 1, WallThickness = 2, GateHeight = 5;
    public const string Revision = "forest-2026-09";
    public static readonly Vector2 WorldSizePx = new(WidthTiles * TileSize, HeightTiles * TileSize);
    public static readonly ZoneDef Town = new() { Id = "town", DisplayName = "새잎 마을",
        Tiles = new(70, 91, 52, 46), Safe = true, FloorColor = new(.28f,.42f,.22f) };
    public static readonly ZoneDef Whisperwood = new() { Id = "whisperwood", DisplayName = "가시깃 부락",
        Tiles = new(149, 55, 32, 30), Danger = 2, FloorColor = new(.28f,.38f,.19f), AccentColor = new(.4f,.4f,.32f) };
    public static readonly ZoneDef IronjawDen = new() { Id = "ironjaw_den", DisplayName = "뿌리 아래 성소",
        Tiles = new(-88, 20, 28, 24), Indoor = true, Danger = 3,
        FloorColor = new(.25f,.29f,.27f), AccentColor = new(.39f,.43f,.38f), Pattern = GroundPattern.Flagstone };
    public static readonly ZoneDef Meadow = new() { Id = "meadow", DisplayName = "새잎의 숲",
        Tiles = new(1, 1, WidthTiles-2, HeightTiles-2), Danger = 1, FloorColor = new(.3f,.44f,.23f), AccentColor = new(.4f,.43f,.34f) };
    public static readonly ZoneDef[] Zones = { Town, Whisperwood, IronjawDen, Meadow };
    // Open terrain uses authored paths, never walls between these descriptive areas.
    public static readonly ZoneLink[] Links = { new(Town, Meadow), new(Meadow, Whisperwood), new(Meadow, IronjawDen) };
    public static Vector2 SpawnPoint => TileCenter(96, 116);
    public static Vector2 DungeonDoor => TileCenter(48, 49);
    public static Vector2 DungeonArrival => TileCenter(-74, 40);
    public static Vector2 TileCenter(int x, int y) => new((x + .5f) * TileSize, (y + .5f) * TileSize);
    public static float ForestEdge(Vector2 p)
    {
        float Edge(Vector2 center, Vector2 radius)
        {
            Vector2 d = (p - center) / radius;
            float a = d.Angle();
            return .94f + .035f * Mathf.Sin(a * 5) + .025f * Mathf.Cos(a * 9) - d.Length();
        }
        return Mathf.Max(Mathf.Max(Edge(new(112,96), new(106,88)), Edge(new(196,136), new(116,112))),
            Mathf.Max(Edge(new(314,82), new(68,67)), Edge(new(310,177), new(78,91))));
    }
    public static bool IsForest(Vector2 p) => ForestEdge(p) > 0f;
    public static float RiverX(float y) => 131f + 7f * Mathf.Sin((y - 108f) * .047f);
    public static float RiverWidth(float y) => 2.2f + .32f * Mathf.Sin(y * .081f);
    public static float RiverDistance(Vector2 p) => Mathf.Abs(p.X - RiverX(p.Y)) - RiverWidth(p.Y);
    public static readonly (Vector2 Center, Vector2 Radius)[] ForestPools = {
        (new(306,198),new(4,3)), (new(337,185),new(5,3)), (new(349,225),new(4,4)) };
    public static float PoolDistance(Vector2 p)
    {
        float distance=float.MaxValue;
        foreach(var pool in ForestPools)
            distance=Mathf.Min(distance,((p-pool.Center)/pool.Radius).Length()*Mathf.Min(pool.Radius.X,pool.Radius.Y)-Mathf.Min(pool.Radius.X,pool.Radius.Y));
        return distance;
    }
    public static bool Water(Vector2 p) => River(p) || PoolDistance(p)<0f;
    public static bool River(Vector2 p) => IsForest(p) && RiverDistance(p) < 0f;
    public static bool Bridge(Vector2 p) => Mathf.Abs(p.X - RiverX(p.Y)) < 4.5f
        && (Mathf.Abs(p.Y - 108f) < 2.5f || Mathf.Abs(p.Y - 57f) < 2.5f || Mathf.Abs(p.Y - 150f) < 3.5f || Mathf.Abs(p.Y - 190f) < 3.5f);
    public static ZoneDef ZoneAt(Vector2 p)
    { foreach (var zone in Zones) if (zone.Contains(p)) return zone; return null; }
    public static ZoneDef ZoneById(string id)
    { foreach (var zone in Zones) if (zone.Id == id) return zone; return null; }
    public static Rect2I CorridorBetween(ZoneDef a, ZoneDef b) => default;
    public static IEnumerable<(Rect2I Rect, ZoneDef A, ZoneDef B)> Corridors() { yield break; }
    public static Rect2 WorldBounds => new(Vector2.Zero, WorldSizePx);
    public static readonly Rect2I TestArenaTiles = new(-30, -30, 20, 20);
    public static Vector2 TestArenaCenter => TileCenter(-20, -20);
}
