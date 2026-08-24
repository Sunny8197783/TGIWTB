using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>바닥 재질. 숫자가 클수록 위에 얹힌다 — Wang 정점 판정에서 큰 쪽이 이긴다.</summary>
public enum Surface
{
    Grass = 0,
    Dirt = 1,
    Stone = 2,

    /// <summary>강. 못 건넌다 — 다리로만.</summary>
    Water = 3,

    /// <summary>성벽. 바닥이 아니라 막힌 칸이라 Wang 지형에서 빠진다.</summary>
    Wall = 4,
}

/// <summary>놓인 소품 하나. 타일 좌표와 쓸 그림.</summary>
public readonly struct PropPlacement
{
    public readonly Rect2I Tiles;
    public readonly string Texture;
    public readonly bool Solid;

    /// <summary>좌우 반전해서 그릴 것인가. 옆모습 한 장으로 길 양쪽을 다 쓴다.</summary>
    public readonly bool FlipH;

    public PropPlacement(Rect2I tiles, string texture, bool solid = true, bool flipH = false)
    {
        Tiles = tiles;
        Texture = texture;
        Solid = solid;
        FlipH = flipH;
    }
}

/// <summary>건물 한 종류의 타일 크기.</summary>
public readonly struct BuildingKind
{
    public readonly string Texture;
    public readonly int W;
    public readonly int H;

    public BuildingKind(string texture, int w, int h)
    {
        Texture = texture;
        W = w;
        H = h;
    }
}

/// <summary>
/// 초보자 마을을 만든다.
///
/// ── 핵심 규칙: 건물은 '땅'이 아니라 '길'에 붙는다 ──────────────
/// 예전에는 빈 칸마다 확률로 집을 세웠다. 아무리 확률을 올려도 결과는
/// 잔디밭에 흩뿌린 집이었지 마을이 아니었다. 마을이 마을로 보이는 이유는
/// 집이 많아서가 아니라 **집이 길을 따라 줄지어 벽을 이루기** 때문이다.
///
/// 그래서 길을 먼저 깔고, 길의 양옆을 따라 커서를 밀면서 어깨를 맞대고
/// 세운다. 남는 안쪽은 저절로 안마당이 되어 텃밭·빨래·우물이 들어간다.
///
/// ── 어느 면이 보이는가 ────────────────────────────────────────
///   길 위쪽 땅 → 문이 아래(길)를 향한다  → 정면 그림
///   길 아래쪽 땅 → 문이 위(길)를 향한다  → 지붕·뒷면 그림
///   세로 길 옆   → 옆면이 보인다          → 지붕 그림(한쪽은 좌우 반전)
/// 이래야 한 거리에서 앞집은 얼굴을, 뒷집은 등을 보인다.
///
/// 같은 씨앗이면 같은 마을이 나온다 — 세이브에 적힌 좌표가 다음 실행에서
/// 건물 안이 되면 안 되기 때문이다.
/// </summary>
public sealed class TownGenerator
{
    private const int Seed = 20260821;

    // ── 형태 (마을 반지름에 대한 비율) ────────────────────────────
    // 예전 값은 광장·순환로가 너무 굵어서 마을 한가운데가 통째로 포석 공터였다.
    // 길은 얇게, 땅은 넓게 — 그래야 건물이 들어설 띠가 생긴다.
    private const float WallRadius = 0.84f;
    private const int WallThickness = 3;

    /// <summary>
    /// 광장 원반. 분수 하나 놓을 만큼만 잡으면 노점을 둘러세울 자리가 없다 —
    /// 장이 서는 마당은 분수 반지름의 서너 배는 돼야 한다.
    /// </summary>
    private const float PlazaRadius = 0.17f;

    /// <summary>순환로 세 겹. 사이 간격이 곧 건물이 들어설 띠의 두께다.</summary>
    private const float RingA = 0.32f;   // 시장 거리 (포석)
    private const float RingB = 0.49f;   // 주거 순환로 (흙)
    private const float RingC = 0.69f;   // 바깥 순환로 (흙)
    /// <summary>
    /// 순환로 폭. Wang 타일은 경계를 칸 한가운데로 지나가게 그리므로, 2로 두면
    /// 화면에서는 4칸짜리 길로 보인다. 골목까지 겹치면 마을 바닥이 흙밭이 된다.
    /// 1이면 화면에서 3칸 — 마차 한 대 지날 뒷길로 읽힌다.
    /// </summary>
    private const int RingWidth = 1;

    /// <summary>
    /// 성문으로 뻗는 대로의 반폭. 2(=5칸)로 두면 게임 안에서 봤을 때 거리가 아니라
    /// 광장처럼 보인다 — 화면에 40칸밖에 안 들어오는데 그 중 5칸이 한 길이다.
    /// 3칸이면 마차가 지나갈 큰길로 읽히면서 양옆에 집이 들어설 땅이 남는다.
    /// </summary>
    private const int AvenueHalfWidth = 1;

    /// <summary>
    /// 골목 갈래. 안쪽은 둘레가 짧아 갈래를 늘리면 골목끼리 붙어 버린다 —
    /// 그러면 길 사이에 남는 땅이 건물 하나 못 넣을 만큼 얇아진다.
    /// (예전에 안쪽까지 16갈래를 넣었더니 마을 바닥의 절반이 흙길이 됐다)
    /// </summary>
    private const int AlleySpokesInner = 8;
    private const int AlleySpokesOuter = 16;

    /// <summary>골목 반폭. 흔들림을 더해도 2칸을 넘지 않아야 한다.</summary>
    private const float AlleyHalfWidth = 0.6f;
    private const float AlleyWobble = 0.35f;

    /// <summary>팔각형을 만드는 대각선 절단 계수. 작으면 팔각, 1이면 마름모에 가깝다.</summary>
    private const float OctagonCut = 0.76f;

    /// <summary>성벽에서 이만큼은 띄운다. 벽에 딱 붙는 건물은 없어야 한다.</summary>
    private const int WallClearance = 2;

    // ── 건물 키트 ────────────────────────────────────────────────
    // 32px 그리드 기준 발자국. 크기 위계가 키트의 핵심이다 — 전부 비슷하면
    // 마을이 납작해진다.

    /// <summary>정면(문이 보이는) 그림. 길 위쪽에 세운다.</summary>
    private static readonly BuildingKind[] FrontS =
    {
        new("kit_s_shed", 1, 1), new("kit_s02", 1, 1), new("kit_s03", 1, 1),
        new("kit_s04", 1, 1), new("kit_s05", 1, 1), new("kit_s06", 1, 1),
        new("kit_s07", 1, 1), new("kit_s08", 1, 1),
    };

    private static readonly BuildingKind[] FrontM =
    {
        new("kit_m_cottage", 2, 2), new("kit_m01", 2, 2), new("kit_m02", 2, 2),
        new("kit_m03", 2, 2), new("kit_m04", 2, 2), new("kit_m05", 2, 2),
        new("kit_m06", 2, 2), new("kit_m07", 2, 2), new("kit_m08", 2, 2),
        new("kit_m09", 2, 2), new("kit_m10", 2, 2), new("kit_m11", 2, 2),
        new("kit_m12", 2, 2), new("kit_m13", 2, 2), new("kit_m14", 2, 2),
    };

    private static readonly BuildingKind[] FrontL =
    {
        new("kit_l_shop", 3, 3), new("kit_l02", 3, 3), new("kit_l03", 3, 3),
        new("kit_l04", 3, 3), new("kit_l05", 3, 3), new("kit_l06", 3, 3),
        new("kit_l07", 3, 3),
    };

    /// <summary>
    /// 지붕면만 보이는 그림. 길 아래쪽·옆쪽에 세운다(뒷모습·옆모습).
    /// 정면 키트보다 종류가 적으면 뒷골목이 복붙처럼 보이므로 꾸준히 늘려야 한다.
    /// </summary>
    private static readonly BuildingKind[] RoofS =
    {
        new("roof_s01", 1, 1), new("roof_s02", 1, 1), new("roof_s03", 1, 1),
    };

    private static readonly BuildingKind[] RoofM =
    {
        new("roof_m01", 2, 2), new("roof_m02", 2, 2), new("roof_m03", 2, 2),
        new("roof_m04", 2, 2), new("roof_m05", 2, 2), new("roof_m06", 2, 2),
        new("roof_m07", 2, 2), new("roof_m08", 2, 3),
    };

    private static readonly BuildingKind[] RoofL =
    {
        new("roof_l01", 3, 3), new("roof_l02", 3, 3), new("roof_l03", 3, 3),
    };

    /// <summary>상점 종류당 최대 채수. 대장간이 열 곳이면 마을이 아니다.</summary>
    private const int MaxPerShop = 3;

    // ── 결과 ─────────────────────────────────────────────────────
    public Surface[,] Ground { get; }
    public List<PropPlacement> Props { get; } = new();

    /// <summary>그 칸이 사람이 다니는 길인가. TileWorld 가 길 막힘을 검사할 때 쓴다.</summary>
    public bool IsRoadCell(int x, int y) => Inside(x, y) && _road[x, y];

    public Vector2 TrainingDummySpot { get; private set; }
    public Rect2I TrainingYard { get; private set; }
    public int YardGateY { get; private set; }
    public const int YardGateHeight = 4;

    private readonly int _w, _h, _ox, _oy;
    private readonly float _cx, _cy, _radius;

    /// <summary>이미 무언가 차지한 칸.</summary>
    private readonly bool[,] _taken;

    /// <summary>사람이 다니는 길. 건물은 여기에 붙어서만 선다.</summary>
    private readonly bool[,] _road;

    /// <summary>건물이 서 있는 칸. 소품을 건물 옆에 붙일 때 본다.</summary>
    private readonly bool[,] _building;

    /// <summary>
    /// 길 위에 놓은 장식(노점·수레·가로등)이 차지한 칸.
    /// _taken 은 길을 통째로 잡아 두므로(건물이 길 위에 서면 안 되니까) 그것과
    /// 따로 세어야 광장 포석 위에 노점을 놓을 수 있다.
    /// </summary>
    private readonly bool[,] _deco;

    private readonly RandomNumberGenerator _rng = new();
    private readonly Dictionary<string, int> _shopCount = new();
    private int _placed;

    /// <summary>지붕(뒷모습·옆모습)으로 선 채수. 정면만 잔뜩이면 거리 한쪽만 사는 것처럼 보인다.</summary>
    private int _roofFaced;

    public TownGenerator(Rect2I town)
    {
        _ox = town.Position.X;
        _oy = town.Position.Y;
        _w = town.Size.X;
        _h = town.Size.Y;
        _cx = _w * 0.5f;
        _cy = _h * 0.5f;
        _radius = Mathf.Min(_w, _h) * 0.5f;

        Ground = new Surface[_w, _h];
        _taken = new bool[_w, _h];
        _road = new bool[_w, _h];
        _building = new bool[_w, _h];
        _deco = new bool[_w, _h];
        _rng.Seed = Seed;
    }

    public void Generate()
    {
        CarveWallAndRoads();
        ReserveTraining();

        PlaceLandmarks();
        PlaceGates();
        PlaceWatchtowers();

        // 길을 따라 어깨를 맞대고 세운다. 순서가 곧 우선순위다 —
        // 얼굴(정면)이 먼저 좋은 자리를 가져가고, 남은 자리에 뒷모습이 선다.
        PlaceFrontRows();
        PlaceBackRows();
        PlaceSideRows();

        PlacePlazaFurniture();
        FillYards();
        PaintOutside();

        GD.Print($"[Town] 건물 {_placed}채 (정면 {_placed - _roofFaced} / 뒷·옆 {_roofFaced}), "
            + $"소품 합계 {Props.Count}개");
        ReportGround();
    }

    /// <summary>성벽 안 바닥의 재질 비율. 길이 땅을 잡아먹고 있는지 눈이 아니라 숫자로 본다.</summary>
    private void ReportGround()
    {
        int grass = 0, dirt = 0, stone = 0, total = 0;
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                if (!IsInsideWall(x, y))
                    continue;
                total++;
                switch (Ground[x, y])
                {
                    case Surface.Grass: grass++; break;
                    case Surface.Dirt: dirt++; break;
                    case Surface.Stone: stone++; break;
                }
            }
        }

        GD.Print($"[Town] 성벽 안 {total}칸 — 잔디 {grass * 100 / total}% "
            + $"흙 {dirt * 100 / total}% 돌 {stone * 100 / total}%");
    }

    // ── 길 ───────────────────────────────────────────────────────

    /// <summary>중심에서의 팔각 거리. 이 값 하나로 성벽·성문을 만든다.</summary>
    private static float OctDist(float dx, float dy)
    {
        float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
        return Mathf.Max(Mathf.Max(ax, ay), (ax + ay) * OctagonCut);
    }

    private void CarveWallAndRoads()
    {
        float wall = _radius * WallRadius;
        float plaza = _radius * PlazaRadius;
        float ringA = _radius * RingA, ringB = _radius * RingB, ringC = _radius * RingC;

        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                float dx = x + 0.5f - _cx;
                float dy = y + 0.5f - _cy;
                float d = OctDist(dx, dy);
                float r = Mathf.Sqrt(dx * dx + dy * dy);   // 순환로는 진짜 원 거리

                bool onAvenue = Mathf.Abs(dx) <= AvenueHalfWidth || Mathf.Abs(dy) <= AvenueHalfWidth;

                if (d > wall)
                {
                    // 성벽 바깥 — 마을을 둘러싼 풀밭. 성문 앞 길만 이어 준다.
                    // 여기는 _taken 으로 막지 않는다. 막으면 숲·밭·다리를 못 놓는다.
                    Ground[x, y] = onAvenue ? Surface.Dirt : Surface.Grass;
                    if (onAvenue)
                        _road[x, y] = true;
                    continue;
                }

                if (d > wall - WallThickness)
                {
                    Ground[x, y] = onAvenue ? Surface.Stone : Surface.Wall;   // 성문 통로
                    _road[x, y] = onAvenue;
                    _taken[x, y] = true;
                    continue;
                }

                if (d <= plaza || onAvenue || OnRing(r, ringA))
                {
                    Ground[x, y] = Surface.Stone;      // 광장 · 대로 · 시장 거리
                    _road[x, y] = true;
                    _taken[x, y] = true;
                    continue;
                }

                if (OnRing(r, ringB) || OnRing(r, ringC) || OnAlley(dx, dy, r, ringA, ringB))
                {
                    Ground[x, y] = Surface.Dirt;       // 순환로 · 골목
                    _road[x, y] = true;
                    _taken[x, y] = true;
                    continue;
                }

                Ground[x, y] = Surface.Grass;
            }
        }
    }

    private static bool OnRing(float r, float ring) => Mathf.Abs(r - ring) <= RingWidth * 0.5f;

    /// <summary>
    /// 방사형 골목. 시장 거리 바깥에서만 열리고, 반지름에 따라 살짝 휜다 —
    /// 곧게 뻗으면 자로 그은 신도시가 된다.
    /// 흔들림은 골목 폭보다 작아야 한다. 크면 판정 폭이 통째로 벌어져 길이 뭉갠다.
    /// </summary>
    private static bool OnAlley(float dx, float dy, float r, float ringA, float ringB)
    {
        if (r < ringA + RingWidth)
            return false;

        int spokes = r < ringB ? AlleySpokesInner : AlleySpokesOuter;
        float step = Mathf.Pi * 2f / spokes;

        // 위상을 반 칸 준다 — 골목이 대로(0·90·180·270도)와 겹치면 대로만 굵어진다.
        float angle = Mathf.Atan2(dy, dx) - step * 0.5f;
        float spoke = angle / step;
        float offset = Mathf.Abs(spoke - Mathf.Round(spoke)) * step * r;
        float wobble = Mathf.Sin(r * 0.30f) * AlleyWobble;
        return offset + wobble <= AlleyHalfWidth;
    }

    // ── 건물 줄 세우기 ───────────────────────────────────────────

    /// <summary>
    /// 길 **위쪽** 땅에 정면을 보이고 선다. 아래 줄이 길이면 그 위로 건물을 앉히고,
    /// 커서를 건물 폭만큼 밀어 어깨를 맞댄다. 간격 0~1칸을 섞어 자로 잰 티를 없앤다.
    /// </summary>
    private void PlaceFrontRows()
    {
        for (int y = 2; y < _h - 2; y++)
        {
            int x = 1;
            while (x < _w - 1)
            {
                if (!_road[x, y] || _road[x, y - 1])
                {
                    x++;
                    continue;
                }

                BuildingKind kind = Pick(x, y, roof: false);
                var rect = new Rect2I(x, y - kind.H, kind.W, kind.H);

                if (!CanBuild(rect) || !RoadAlong(rect.Position.X, rect.Size.X, y))
                {
                    x++;
                    continue;
                }

                Raise(rect, kind.Texture);
                x += kind.W + _rng.RandiRange(0, 1);
            }
        }
    }

    /// <summary>길 **아래쪽** 땅. 문이 길(위)을 보므로 우리에겐 지붕·뒷면이 보인다.</summary>
    private void PlaceBackRows()
    {
        for (int y = 1; y < _h - 3; y++)
        {
            int x = 1;
            while (x < _w - 1)
            {
                if (!_road[x, y] || _road[x, y + 1])
                {
                    x++;
                    continue;
                }

                BuildingKind kind = Pick(x, y, roof: true);
                var rect = new Rect2I(x, y + 1, kind.W, kind.H);

                if (!CanBuild(rect) || !RoadAlong(rect.Position.X, rect.Size.X, y))
                {
                    x++;
                    continue;
                }

                Raise(rect, kind.Texture);
                x += kind.W + _rng.RandiRange(0, 1);
            }
        }
    }

    /// <summary>
    /// 세로 길의 좌우. 옆면이 보이므로 지붕 그림을 쓰고, 오른쪽 줄은 좌우를 뒤집어
    /// 처마 그늘이 길 쪽으로 가게 한다 — 그림 한 장으로 양쪽을 다 쓴다.
    /// </summary>
    private void PlaceSideRows()
    {
        for (int x = 2; x < _w - 2; x++)
        {
            int y = 1;
            while (y < _h - 1)
            {
                bool west = _road[x, y] && !_road[x - 1, y];
                bool east = _road[x, y] && !_road[x + 1, y];
                if (!west && !east)
                {
                    y++;
                    continue;
                }

                // 세로 길가라고 전부 지붕 그림을 쓰면 뒷·옆모습이 정면보다 많아지는데,
                // 지붕 키트는 종류가 몇 안 되므로 같은 집이 줄줄이 늘어선다.
                // 모퉁이집이 옆길을 바라보는 건 실제 마을에도 흔하니 일부는 정면으로.
                bool roof = _rng.Randf() < 0.55f;
                BuildingKind kind = Pick(x, y, roof);
                var rect = west
                    ? new Rect2I(x - kind.W, y, kind.W, kind.H)
                    : new Rect2I(x + 1, y, kind.W, kind.H);

                if (!CanBuild(rect) || !RoadDown(x, rect.Position.Y, rect.Size.Y))
                {
                    y++;
                    continue;
                }

                Raise(rect, kind.Texture, flipH: roof && east);
                y += kind.H + _rng.RandiRange(0, 1);
            }
        }
    }

    /// <summary>가로 구간 (x0..x0+w-1, y) 의 절반 이상이 길인가.</summary>
    private bool RoadAlong(int x0, int w, int y)
    {
        int hit = 0;
        for (int x = x0; x < x0 + w; x++)
        {
            if (Inside(x, y) && _road[x, y])
                hit++;
        }
        return hit * 2 >= w;
    }

    /// <summary>세로 구간 (x, y0..y0+h-1) 의 절반 이상이 길인가.</summary>
    private bool RoadDown(int x, int y0, int h)
    {
        int hit = 0;
        for (int y = y0; y < y0 + h; y++)
        {
            if (Inside(x, y) && _road[x, y])
                hit++;
        }
        return hit * 2 >= h;
    }

    private void Raise(Rect2I rect, string texture, bool flipH = false)
    {
        Take(rect);
        MarkBuilding(rect);
        Props.Add(new PropPlacement(Abs(rect), texture, solid: true, flipH: flipH));
        _placed++;
        if (texture.StartsWith("roof_", System.StringComparison.Ordinal))
            _roofFaced++;
    }

    /// <summary>세울 수 있는 자리인가 — 성벽 안, 벽에서 떨어져 있고, 아직 빈 땅.</summary>
    private bool CanBuild(Rect2I rect)
    {
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
            {
                if (!Inside(x, y) || _taken[x, y] || _road[x, y])
                    return false;
                if (!HasWallClearance(x, y))
                    return false;
            }
        }
        return true;
    }

    private bool HasWallClearance(int x, int y)
        => OctDist(x - _cx, y - _cy) < _radius * WallRadius - WallThickness - WallClearance;

    /// <summary>
    /// 무엇을 세울지. 광장에서 멀어질수록 작아진다 — 중심은 2층 상점,
    /// 중간은 민가, 바깥은 창고·헛간.
    /// </summary>
    private BuildingKind Pick(int x, int y, bool roof)
    {
        float d = OctDist(x - _cx, y - _cy) / _radius;

        BuildingKind[] s = roof ? RoofS : FrontS;
        BuildingKind[] m = roof ? RoofM : FrontM;
        BuildingKind[] l = roof ? RoofL : FrontL;

        if (d < RingA + 0.06f)
            return _rng.Randf() < 0.55f ? PickShop(l) : Any(m);

        if (d < RingB)
            return _rng.Randf() < 0.18f ? PickShop(l) : Any(m);

        if (d < RingC)
            return _rng.Randf() < 0.30f ? Any(s) : Any(m);

        return _rng.Randf() < 0.65f ? Any(s) : Any(m);
    }

    private BuildingKind Any(BuildingKind[] set) => set[_rng.RandiRange(0, set.Length - 1)];

    /// <summary>상점은 종류당 몇 채까지. 같은 대장간이 늘어서면 마을이 아니다.</summary>
    private BuildingKind PickShop(BuildingKind[] large)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            BuildingKind shop = Any(large);
            _shopCount.TryGetValue(shop.Texture, out int used);
            if (used < MaxPerShop)
            {
                _shopCount[shop.Texture] = used + 1;
                return shop;
            }
        }
        return shop_fallback();

        BuildingKind shop_fallback() => Any(large == RoofL ? RoofM : FrontM);
    }

    // ── 랜드마크 ─────────────────────────────────────────────────

    /// <summary>
    /// 이름 붙은 큰 건물. 무작위 배치에 맡기면 길드홀이 헛간 사이에 끼거나
    /// 아예 자리를 못 잡는다. 방위와 거리 띠를 정해 주고 먼저 앉힌다.
    /// (각도 0도가 동쪽, 반시계 방향. 북 90 / 서 180 / 남 270)
    /// </summary>
    private void PlaceLandmarks()
    {
        Seat("kit_xl_guild", 6, 5, 52f, 34f, 0.30f, 0.52f);   // 북동 — 모험가 길드
        Seat("town_hall", 5, 4, 90f, 30f, 0.30f, 0.48f);      // 북   — 마을 회관
        Seat("chapel", 3, 4, 128f, 28f, 0.30f, 0.48f);        // 북서 — 예배당
        Seat("inn", 4, 4, 270f, 30f, 0.30f, 0.48f);           // 남   — 여관
        Seat("tavern", 3, 3, 318f, 30f, 0.30f, 0.50f);        // 남동 — 선술집
        Seat("bakery", 3, 3, 220f, 30f, 0.30f, 0.50f);        // 남서 — 빵집
        Seat("smithy", 3, 3, 5f, 30f, 0.30f, 0.50f);          // 동   — 대장간
        Seat("apothecary", 3, 3, 155f, 28f, 0.30f, 0.50f);    // 서   — 약재상
        Seat("stable", 4, 3, 250f, 26f, 0.52f, 0.70f);        // 남   — 마구간
        Seat("warehouse", 4, 3, 15f, 28f, 0.52f, 0.72f);      // 동   — 창고
        Seat("windmill", 3, 5, 65f, 26f, 0.55f, 0.76f);       // 북동 — 풍차
        Seat("black_market", 2, 3, 205f, 34f, 0.55f, 0.78f);  // 남서 — 간판 없는 문
    }

    /// <summary>
    /// 그 방위·거리 띠 안에서 '아래가 길인 빈 터'를 찾아 앉힌다.
    /// 방위 한가운데에 가장 가까운 자리를 고른다 — 큰 건물은 눈에 띄는 곳에 서야 한다.
    /// </summary>
    private void Seat(string texture, int w, int h, float degCenter, float degSpan,
                      float rMin, float rMax)
    {
        // 조건을 세 번에 걸쳐 푼다. 처음에는 원하는 방위·거리에 정면이 길에 닿는
        // 자리를 찾고, 없으면 범위를 넓히고, 그래도 없으면 어느 면이든 길에
        // 닿기만 하면 받는다. 큰 건물은 6x5 라 딱 맞는 터가 드물다.
        if (TrySeat(texture, w, h, degCenter, degSpan, rMin, rMax, front: true))
            return;
        if (TrySeat(texture, w, h, degCenter, degSpan * 2f,
                    Mathf.Max(0.14f, rMin - 0.10f), rMax + 0.12f, front: true))
            return;
        if (TrySeat(texture, w, h, degCenter, 180f, 0.14f, WallRadius - 0.06f, front: false))
            return;

        GD.PushWarning($"[Town] {texture} 자리를 못 찾았다.");
    }

    private bool TrySeat(string texture, int w, int h, float degCenter, float degSpan,
                         float rMin, float rMax, bool front)
    {
        var best = new Rect2I();
        float bestScore = float.MaxValue;

        for (int y = 2; y < _h - h - 2; y++)
        {
            for (int x = 1; x < _w - w - 1; x++)
            {
                float dx = x + w * 0.5f - _cx, dy = y + h * 0.5f - _cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / _radius;
                if (r < rMin || r > rMax)
                    continue;

                float deg = Mathf.RadToDeg(Mathf.Atan2(-dy, dx));
                float off = Mathf.Abs(Mathf.Wrap(deg - degCenter, -180f, 180f));
                if (off > degSpan)
                    continue;

                var rect = new Rect2I(x, y, w, h);
                if (!CanBuild(rect))
                    continue;
                if (front ? !RoadAlong(x, w, y + h) : !TouchesRoad(rect))
                    continue;

                if (off < bestScore)
                {
                    bestScore = off;
                    best = rect;
                }
            }
        }

        if (bestScore >= float.MaxValue)
            return false;

        Raise(best, texture);
        return true;
    }

    /// <summary>사각형의 어느 면이든 길에 닿는가.</summary>
    private bool TouchesRoad(Rect2I r)
    {
        for (int y = r.Position.Y - 1; y <= r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X - 1; x <= r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y) && _road[x, y])
                    return true;
            }
        }
        return false;
    }

    /// <summary>성문 네 개. 성벽을 뚫어 둔 자리에 문루를 얹는다.</summary>
    private void PlaceGates()
    {
        int r = (int)(_radius * WallRadius) - WallThickness / 2;
        int cx = (int)_cx, cy = (int)_cy;

        Add(cx - 2, cy - r - 2);      // 북문
        Add(cx - 2, cy + r - 2);      // 남문
        Add(cx - r - 2, cy - 2);      // 서문
        Add(cx + r - 2, cy - 2);      // 동문

        void Add(int x, int y)
            => Props.Add(new PropPlacement(Abs(new Rect2I(x, y, 5, 4)), "gatehouse", solid: false));
    }

    /// <summary>성벽 망루. 팔각의 네 대각선 모서리에 세워 성벽에 높이를 준다.</summary>
    private void PlaceWatchtowers()
    {
        float r = _radius * WallRadius - WallThickness * 0.5f;
        for (int i = 0; i < 4; i++)
        {
            float deg = 45f + i * 90f;
            float rad = Mathf.DegToRad(deg);
            int x = Mathf.RoundToInt(_cx + Mathf.Cos(rad) * r) - 1;
            int y = Mathf.RoundToInt(_cy - Mathf.Sin(rad) * r) - 2;

            var rect = new Rect2I(x, y, 2, 4);
            if (!Inside(x, y) || !Inside(x + 1, y + 3))
                continue;
            Take(rect);
            MarkBuilding(rect);
            Props.Add(new PropPlacement(Abs(rect), "watchtower"));
        }
    }

    /// <summary>서쪽 훈련장 — 건물과 울타리 마당. 큰길에서 걸어 들어온다.</summary>
    private void ReserveTraining()
    {
        int x = (int)(_cx - _radius * 0.66f);
        int y = (int)(_cy - 8);

        var pad = new Rect2I(x - 1, y - 2, 15, 13);
        BlobGround(pad, Surface.Dirt);      // 네모로 깔면 갈색 상자가 하나 놓인 것처럼 보인다
        Take(pad);

        Props.Add(new PropPlacement(Abs(new Rect2I(x, y, 5, 4)), "training_hall"));

        var yard = new Rect2I(x + 6, y, 7, 9);
        TrainingYard = Abs(yard);
        YardGateY = TrainingYard.Position.Y + 3;
        TrainingDummySpot = WorldLayout.TileCenter(
            TrainingYard.Position.X + 3, TrainingYard.Position.Y + 6);

        // 훈련소 앞마당.
        Props.Add(new PropPlacement(Abs(new Rect2I(x, y + 5, 2, 2)), "weapon_rack"));
        Props.Add(new PropPlacement(Abs(new Rect2I(x + 3, y + 5, 2, 2)), "barrels"));
        Props.Add(new PropPlacement(Abs(new Rect2I(x, y + 8, 2, 2)), "training_dummy"));
        Props.Add(new PropPlacement(Abs(new Rect2I(x + 3, y + 8, 2, 2)), "crates"));

        // 울타리 안 — 가운데는 실제로 싸우는 자리라 비우고 가장자리를 두른다.
        Yard(1, 1, "training_dummy");
        Yard(4, 1, "training_dummy");
        Yard(1, 7, "weapon_rack");
        Yard(4, 7, "barrels");
        Yard(5, 4, "crates");

        FenceYard(yard);

        void Yard(int ox, int oy, string texture)
            => Props.Add(new PropPlacement(new Rect2I(
                TrainingYard.Position.X + ox, TrainingYard.Position.Y + oy, 2, 2), texture));
    }

    /// <summary>
    /// 마당 둘레에 목책을 두른다. 예전에는 타일로 칠했는데, 단색 갈색 사각형이라
    /// 도면에 자를 대고 그은 것처럼 보였다. 실제 울타리 그림을 세운다.
    /// 서쪽 가운데는 출입구로 비운다.
    /// </summary>
    private void FenceYard(Rect2I yard)
    {
        int x0 = yard.Position.X, y0 = yard.Position.Y;
        int x1 = x0 + yard.Size.X - 1, y1 = y0 + yard.Size.Y - 1;
        int gate = y0 + yard.Size.Y / 2;

        for (int x = x0; x <= x1 - 1; x += 2)
        {
            Post(x, y0);
            Post(x, y1);
        }
        for (int y = y0; y <= y1 - 1; y += 2)
        {
            if (y < gate - 1 || y > gate + 1)
                Post(x0, y);
            Post(x1, y);
        }

        // 마당 전체가 이미 예약(_taken)돼 있으므로 빈자리 검사는 하지 않는다.
        // 통과는 못 하게 solid 로 둔다 — 울타리는 넘는 것이 아니라 돌아가는 것이다.
        void Post(int px, int py)
            => Props.Add(new PropPlacement(Abs(new Rect2I(px, py, 2, 1)), "fence_section"));
    }

    /// <summary>
    /// 광장. 분수 하나만 두면 포석 벌판이 된다 — 장이 서는 곳처럼 보이려면
    /// 분수를 중심으로 노점·수레·통이 둘러서야 한다.
    /// 한가운데(부활 지점)와 대로가 지나는 십자만 비워 둔다.
    /// </summary>
    /// <summary>
    /// 노점을 여러 번 넣어 확률을 높인다. 통·화분 같은 작은 것만 깔면 넓은 포석에
    /// 점을 찍은 것처럼 보이고 장이 선 느낌이 안 난다.
    /// </summary>
    private static readonly string[] PlazaRing =
    {
        "stall_produce", "stall_produce", "stall_cloth", "stall_cloth",
        "stall_weapon", "stall_weapon", "hand_cart", "hand_cart",
        "barrels", "crates", "bench", "lamp_post",
    };

    private void PlacePlazaFurniture()
    {
        int cx = (int)_cx, cy = (int)_cy;

        Add(cx - 1, cy - 2, 3, 3, "fountain");
        Add(cx - 5, cy - 2, 2, 3, "statue");
        Add(cx - 1, cy + 4, 2, 2, "notice_board");
        Add(cx + 4, cy - 5, 2, 2, "well");

        // 장은 원이 아니라 줄로 선다. 격자로 훑어야 빈틈 없이 들어차고,
        // 원둘레로 돌면 대로에 걸리는 자리가 대부분 걸러져 몇 개 안 남는다.
        int reach = Mathf.CeilToInt(_radius * PlazaRadius) + 1;
        for (int dy = -reach; dy <= reach; dy += 2)
        {
            for (int dx = -reach; dx <= reach; dx += 2)
            {
                int x = cx + dx, y = cy + dy;
                var rect = new Rect2I(x, y, 2, 2);

                // 가운데 십자 세 칸만 비운다. 대로 폭(5칸)을 통째로 비우면
                // 광장이 네 조각으로 갈려 좌판을 놓을 자리가 거의 남지 않는다.
                // 좌판은 통과 가능(solid:false)이라 길을 실제로 막지는 않는다.
                if (Mathf.Abs(dx) <= 1 || Mathf.Abs(dy) <= 1)
                    continue;
                if (!DecoFree(rect) || !AllStone(rect))
                    continue;

                TakeDeco(rect);
                Props.Add(new PropPlacement(
                    Abs(rect), PlazaRing[_rng.RandiRange(0, PlazaRing.Length - 1)], solid: false));
            }
        }

        LineMarketStreet();

        void Add(int x, int y, int w, int h, string texture)
        {
            var rect = new Rect2I(x, y, w, h);
            Take(rect);
            TakeDeco(rect);
            Props.Add(new PropPlacement(Abs(rect), texture));
        }
    }

    /// <summary>
    /// 시장 거리(안쪽 순환로) 가장자리에 노점과 가로등을 세운다.
    /// 상점 앞에 좌판이 나와 있어야 '상점가'로 읽힌다 — 문만 있으면 그냥 집이다.
    /// </summary>
    private static readonly string[] StreetDressing =
        { "stall_produce", "stall_cloth", "stall_weapon", "lamp_post", "barrels", "crates", "bench" };

    private void LineMarketStreet()
    {
        float ringA = _radius * RingA;

        for (int i = 0; i < 64; i++)
        {
            float a = Mathf.Pi * 2f * i / 64f;
            for (int side = -1; side <= 1; side += 2)
            {
                float rr = ringA + side * (RingWidth * 0.5f + 1.0f);
                int x = (int)_cx + Mathf.RoundToInt(Mathf.Cos(a) * rr);
                int y = (int)_cy + Mathf.RoundToInt(Mathf.Sin(a) * rr);

                var rect = new Rect2I(x, y, 2, 2);
                if (!DecoFree(rect) || Ground[x, y] == Surface.Wall)
                    continue;
                if (_building[x, y] || _rng.Randf() > 0.45f)
                    continue;

                TakeDeco(rect);
                Props.Add(new PropPlacement(Abs(rect),
                    StreetDressing[_rng.RandiRange(0, StreetDressing.Length - 1)], solid: false));
            }
        }
    }

    private bool AllStone(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || Ground[x, y] != Surface.Stone)
                    return false;
            }
        }
        return true;
    }

    private bool DecoFree(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || _deco[x, y] || _building[x, y])
                    return false;
            }
        }
        return true;
    }

    private void TakeDeco(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y))
                    _deco[x, y] = true;
            }
        }
    }

    // ── 안마당 채우기 ────────────────────────────────────────────
    //
    // 건물 줄 사이에 남은 땅이 안마당이다. 무작위로 덮으면 창고 옆에 빨래가
    // 널리고 광장 한복판에 텃밭이 생긴다. 그 칸이 **어디인지** 보고 고른다.

    private static readonly string[] NearBuilding =
        { "barrels", "crates", "firewood", "ladder_bucket", "flower_pots" };

    private static readonly string[] Roadside =
        { "lamp_post", "signpost", "bench", "well" };

    private static readonly string[] AlleyClutter =
        { "laundry_line", "flower_pots", "crates", "barrels" };

    private static readonly string[] YardStuff =
    {
        "veg_patch", "chickens", "hand_cart", "fence_section", "firewood", "log_pile",
        "orchard_tree", "haystack", "flower_pots", "crates",
    };

    private void FillYards()
    {
        FillCourtyardSheds();

        for (int y = 1; y < _h - 2; y++)
        {
            for (int x = 1; x < _w - 2; x++)
            {
                if (!IsInsideWall(x, y) || _taken[x, y] || Ground[x, y] != Surface.Grass)
                    continue;

                string[] table = NextToBuilding(x, y) ? NearBuilding
                    : NextTo(x, y, Surface.Stone) ? Roadside
                    : NextTo(x, y, Surface.Dirt) ? AlleyClutter
                    : YardStuff;

                if (_rng.Randf() > 0.80f)
                    continue;

                var rect = new Rect2I(x, y, 2, 2);
                if (!IsFree(rect))
                    continue;

                Take(rect);
                Props.Add(new PropPlacement(
                    Abs(rect), table[_rng.RandiRange(0, table.Length - 1)], solid: false));
                x += 2;
            }
        }

        ScatterTrees();
    }

    /// <summary>
    /// 안마당의 헛간·창고. 실제 마을에서 집 뒤 마당은 비어 있지 않다 —
    /// 장작광, 닭장, 헛간이 들어차 있다. 길에 접하지 않으니 정면 규칙에서
    /// 빠지고, 그래서 뒷골목의 밀도를 여기서 벌어야 한다.
    /// </summary>
    private void FillCourtyardSheds()
    {
        for (int y = 2; y < _h - 2; y++)
        {
            for (int x = 2; x < _w - 2; x++)
            {
                if (!IsInsideWall(x, y) || _taken[x, y] || Ground[x, y] != Surface.Grass)
                    continue;

                // **바로 위 칸이 건물**일 때만 세운다. '근처에 건물이 있으면'으로
                // 느슨하게 잡았더니 마당마다 똑같은 헛간이 버섯처럼 돋아났다.
                // 집 뒤에 딱 붙어야 헛간으로 읽힌다.
                if (!_building[x, y - 1] || _rng.Randf() > 0.34f)
                    continue;

                // 길가는 이미 앞줄이 차지했다. 여기는 마당 안쪽만.
                if (NextTo(x, y, Surface.Stone) || NextTo(x, y, Surface.Dirt))
                    continue;

                var rect = new Rect2I(x, y, 1, 1);
                if (!IsFree(rect))
                    continue;

                // Raise 를 쓰면 이 헛간도 '건물'로 기록돼서, 바로 아래 칸이 다시
                // 조건을 만족한다 — 헛간이 아래로 줄줄이 이어져 잔디밭을 가로지른다.
                // 그래서 자리만 잡고 건물로는 세지 않는다.
                Take(rect);
                Props.Add(new PropPlacement(Abs(rect),
                    _rng.Randf() < 0.5f ? "roof_s01" : Any(FrontS).Texture));
                _placed++;
                x += 1;
            }
        }
    }

    private bool NextToBuilding(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (Inside(cx, cy) && _building[cx, cy])
                    return true;
            }
        }
        return false;
    }

    private bool NextTo(int x, int y, Surface surface)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (Inside(cx, cy) && Ground[cx, cy] == surface)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 안마당에 나무를 심는다. 길가에는 심지 않는다 — 거리에 나무가 늘어서면
    /// 집이 안 보이고 마을이 숲으로 읽힌다.
    /// </summary>
    private void ScatterTrees()
    {
        for (int y = 2; y < _h - 4; y += 3)
        {
            for (int x = 2; x < _w - 3; x += 3)
            {
                if (!IsInsideWall(x, y) || _rng.Randf() > 0.22f)
                    continue;
                if (NextTo(x, y, Surface.Stone) || NextTo(x, y, Surface.Dirt))
                    continue;

                var rect = new Rect2I(x, y, 2, 3);
                if (!IsFree(rect) || Ground[x, y] != Surface.Grass)
                    continue;

                // 마을 안은 활엽수가 어울린다. 침엽수는 성벽 밖 숲의 몫이다.
                Take(rect);
                Props.Add(new PropPlacement(
                    Abs(rect), _rng.Randf() < 0.7f ? "tree_oak" : "tree_conifer"));
            }
        }
    }

    /// <summary>
    /// 성벽 바깥. 마을이 허공에 뜬 섬처럼 보이지 않으려면 벽 밖에도 사연이 있어야 한다.
    ///
    /// 예전에는 여기에 강을 팠는데, 성벽(반지름 0.84)과 맵 끝 사이가 7칸뿐이라
    /// 강도 밭도 지도 밖으로 밀려나 아무 것도 안 보였다. 강은 어차피 옆 구역(초원)에
    /// 이미 있으므로 지우고, 좁은 띠를 방위별로 나눠 쓰기로 했다.
    ///
    ///   서·북서 : 침엽수림 (마을을 등지고 어두운 숲)
    ///   북동·동 : 밭과 과수원 (마을을 먹여 살리는 곳)
    ///   남·남동 : 목초지 — 건초더미와 바위
    /// </summary>
    private void PaintOutside()
    {
        float wall = _radius * WallRadius;

        for (int y = 1; y < _h - 3; y += 2)
        {
            for (int x = 1; x < _w - 3; x += 2)
            {
                float dx = x - _cx, dy = y - _cy;
                if (OctDist(dx, dy) < wall + 2f)
                    continue;                          // 성벽에 바짝 붙이지 않는다
                if (Ground[x, y] != Surface.Grass)
                    continue;                          // 성문 앞 길은 비운다

                float deg = Mathf.RadToDeg(Mathf.Atan2(-dy, dx));
                if (deg < 0f)
                    deg += 360f;

                if (deg >= 120f && deg < 250f)
                    Forest(x, y);
                else if (deg >= 250f && deg < 340f)
                    Pasture(x, y);
                else
                    Farmland(x, y);
            }
        }
    }

    /// <summary>서쪽 숲 — 빽빽할수록 좋다. 마을 뒤가 캄캄해야 성벽이 의미를 갖는다.</summary>
    private void Forest(int x, int y)
    {
        if (_rng.Randf() > 0.62f)
            return;

        var rect = new Rect2I(x, y, 2, 3);
        if (!IsFree(rect))
            return;
        Take(rect);
        Props.Add(new PropPlacement(
            Abs(rect), _rng.Randf() < 0.75f ? "tree_conifer" : "tree_oak"));
    }

    /// <summary>동쪽 농지 — 밭 구획과 과수원, 울타리.</summary>
    private void Farmland(int x, int y)
    {
        float roll = _rng.Randf();
        if (roll > 0.55f)
            return;

        if (roll < 0.18f)
        {
            var plot = new Rect2I(x, y, 4, 3);
            if (IsFree(plot))
            {
                Take(plot);
                Props.Add(new PropPlacement(Abs(plot), "farm_plot", solid: false));
                return;
            }
        }

        var rect = new Rect2I(x, y, 2, 2);
        if (!IsFree(rect))
            return;
        Take(rect);
        Props.Add(new PropPlacement(Abs(rect),
            PickOne("orchard_tree", "orchard_tree", "veg_patch", "fence_section", "haystack")));
    }

    /// <summary>남쪽 목초지 — 건초더미와 바위, 드문드문 나무.</summary>
    private void Pasture(int x, int y)
    {
        if (_rng.Randf() > 0.42f)
            return;

        var rect = new Rect2I(x, y, 2, 2);
        if (!IsFree(rect))
            return;
        Take(rect);
        Props.Add(new PropPlacement(Abs(rect),
            PickOne("haystack", "boulder", "fence_section", "log_pile", "tree_oak")));
    }

    private string PickOne(params string[] options)
        => options[_rng.RandiRange(0, options.Length - 1)];

    // ── 격자 도우미 ──────────────────────────────────────────────

    private Rect2I Abs(Rect2I local)
        => new(local.Position.X + _ox, local.Position.Y + _oy, local.Size.X, local.Size.Y);

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

    /// <summary>성벽 안쪽인가.</summary>
    private bool IsInsideWall(int x, int y)
        => OctDist(x - _cx, y - _cy) < _radius * WallRadius - WallThickness;

    private bool IsFree(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || _taken[x, y] || _deco[x, y])
                    return false;
            }
        }
        return true;
    }

    private void Take(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y))
                    _taken[x, y] = true;
            }
        }
    }

    private void MarkBuilding(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y))
                    _building[x, y] = true;
            }
        }
    }

    /// <summary>
    /// 사각형이 아니라 가장자리를 갉아먹은 덩어리로 바닥을 바꾼다.
    /// 사람이 밟아 풀이 죽은 자리는 직선으로 끝나지 않는다.
    /// </summary>
    private void BlobGround(Rect2I r, Surface surface)
    {
        float cx = r.Position.X + r.Size.X * 0.5f;
        float cy = r.Position.Y + r.Size.Y * 0.5f;
        float rx = r.Size.X * 0.5f, ry = r.Size.Y * 0.5f;

        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || Ground[x, y] != Surface.Grass)
                    continue;

                float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                if (d > 1.0f + Mathf.Sin(x * 1.7f + y * 2.3f) * 0.12f)
                    continue;

                Ground[x, y] = surface;
            }
        }
    }

    /// <summary>잔디인 칸만 바꾼다. 이미 깔린 길을 덮어쓰지 않는다.</summary>
    private void FillGroundSoft(Rect2I r, Surface surface)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y) && Ground[x, y] == Surface.Grass)
                    Ground[x, y] = surface;
            }
        }
    }
}
