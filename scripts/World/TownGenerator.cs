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

    public PropPlacement(Rect2I tiles, string texture, bool solid = true)
    {
        Tiles = tiles;
        Texture = texture;
        Solid = solid;
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
/// 구조는 성벽으로 둘러싸인 방사형 마을이다. 격자로 자르면 아무리 빽빽해도
/// 신도시처럼 보인다 — 옛 마을은 광장에서 성문으로 길이 뻗고, 그 길을
/// 환상 도로가 가로지르며, 집이 길을 따라 뭉쳐 선다.
///
///   팔각 성벽 + 동서남북 성문
///   중앙 광장(분수·노점) → 성문으로 뻗는 방사형 대로, 대각선 골목
///   광장을 두 겹으로 감싸는 환상 도로
///   길가에 붙어 번지는 집 무리, 그 사이를 채우는 나무
///
/// 바탕은 잔디다. 길과 문 앞만 흙·돌이고 나머지는 풀과 나무로 남는다 —
/// 바탕을 흙으로 깔면 마을이 아니라 공사장처럼 보인다.
///
/// 부채꼴 섹터마다 성격이 다르다:
///   중심 상점가 / 북동 길드 구역 / 남서 바깥 허름한 집 / 서 훈련장 / 동 벌목장
///
/// 같은 씨앗이면 같은 마을이 나온다 — 세이브에 적힌 좌표가 다음 실행에서
/// 건물 안이 되면 안 되기 때문이다.
/// </summary>
public sealed class TownGenerator
{
    private const int Seed = 20260821;

    /// <summary>
    /// 건물을 세울지. 단계별로 진행하는 중이라 1~2단계에서는 꺼 둔다 —
    /// 도로망과 구역이 먼저 확정돼야 건물 배치가 의미가 있다.
    /// </summary>
    public const bool BuildingsEnabled = false;

    // ── 건물 ─────────────────────────────────────────────────────
    private static readonly BuildingKind[] Cottages =
    {
        new("cottage_a", 6, 5), new("cottage_b", 6, 5), new("cottage_c", 6, 5),
    };

    private static readonly BuildingKind[] TownHouses =
    {
        new("townhouse_a", 5, 6), new("townhouse_b", 5, 6),
    };

    private static readonly BuildingKind[] Shacks =
    {
        new("shack_a", 5, 4), new("shack_b", 5, 4),
    };

    private static readonly BuildingKind[] Shops =
    {
        new("shop_blacksmith", 7, 6), new("shop_general", 7, 6),
        new("shop_alchemist", 7, 6), new("shop_armor", 7, 6),
        new("shop_bakery", 7, 6), new("shop_inn", 8, 7),
    };

    private static readonly BuildingKind Manor = new("manor", 12, 9);

    /// <summary>상점 종류당 최대 채수. 대장간이 열 곳이면 마을이 아니다.</summary>
    private const int MaxPerShop = 2;

    // ── 형태 (마을 반지름에 대한 비율) ────────────────────────────
    // 성벽을 마을 가장자리까지 밀면 벽 바깥에 아무 것도 못 놓는다.
    // 명세가 벽 밖에 숲·강·밭을 요구하므로 안쪽으로 당겨 바깥 여백을 만든다.
    private const float WallRadius = 0.80f;
    private const int WallThickness = 3;

    private const float PlazaRadius = 0.18f;
    // 광장·순환로·대로가 서로 붙으면 중앙이 한 덩어리 포석이 되고, 구역에 남는
    // 띠가 조각난다. 사이를 벌려 구역마다 실제 면적을 준다.
    private const float InnerRingRadius = 0.38f;
    private const float OuterRingRadius = 0.60f;

    private const int AvenueHalfWidth = 2;    // 성문으로 뻗는 대로 (돌)
    private const int LaneHalfWidth = 1;      // 대각선 골목 (흙)
    private const int InnerRingWidth = 3;
    private const int OuterRingWidth = 3;

    /// <summary>팔각형을 만드는 대각선 절단 계수. 작으면 팔각, 1이면 마름모에 가깝다.</summary>
    private const float OctagonCut = 0.76f;

    // ── 결과 ─────────────────────────────────────────────────────
    public Surface[,] Ground { get; }
    public List<PropPlacement> Props { get; } = new();

    public Vector2 TrainingDummySpot { get; private set; }
    public Rect2I TrainingYard { get; private set; }
    public int YardGateY { get; private set; }
    public const int YardGateHeight = 4;

    private readonly int _w, _h, _ox, _oy;
    private readonly float _cx, _cy, _radius;
    private readonly bool[,] _taken;
    private readonly RandomNumberGenerator _rng = new();
    private readonly Dictionary<string, int> _shopCount = new();
    private int _converted;

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
        _rng.Seed = Seed;
    }

    public void Generate()
    {
        CarveWallAndRoads();
        if (BuildingsEnabled)
        {
            ReserveTraining();
            ReserveLogging();
        }
        PlacePlazaFurniture();
        PlaceGates();

        PaintDistrictGround();
        PaintOutside();

        // 1단계는 "길과 지형만 있는 빈 마을"이다. 건물은 3~4단계에서 놓는다.
        if (!BuildingsEnabled)
            return;

        GrowBuildings(roadFrontOnly: true);
        for (int pass = 0; pass < 4; pass++)
            GrowBuildings(roadFrontOnly: false);

        ScatterTrees();
    }

    // ── 형태 ─────────────────────────────────────────────────────

    /// <summary>중심에서의 팔각 거리. 이 값 하나로 성벽·환상 도로·광장을 다 만든다.</summary>
    private static float OctDist(float dx, float dy)
    {
        float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
        return Mathf.Max(Mathf.Max(ax, ay), (ax + ay) * OctagonCut);
    }

    private void CarveWallAndRoads()
    {
        float wall = _radius * WallRadius;
        float plaza = _radius * PlazaRadius;
        float inner = _radius * InnerRingRadius;
        float outer = _radius * OuterRingRadius;

        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                float dx = x + 0.5f - _cx;
                float dy = y + 0.5f - _cy;
                float d = OctDist(dx, dy);

                bool onAvenue = Mathf.Abs(dx) <= AvenueHalfWidth || Mathf.Abs(dy) <= AvenueHalfWidth;

                // 순환로는 팔각 거리가 아니라 진짜 원 거리로 잰다 — 팔각으로 재면
                // 마름모가 나온다. 명세는 원형 순환로다.
                float ringDist = Mathf.Sqrt(dx * dx + dy * dy);

                // 골목은 곧은 대각선이 아니라 휘어야 한다. 중심에서 멀어질수록
                // 어긋나게 흔들어 준다.
                float wobble = Mathf.Sin(ringDist * 0.22f) * 3.2f;
                bool onLane = Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy) + wobble) <= LaneHalfWidth;

                if (d > wall)
                {
                    // 성벽 바깥 — 마을을 둘러싼 풀밭. 성문 앞 길만 이어 준다.
                    // 여기는 _taken 으로 막지 않는다. 막으면 숲·밭·다리를 못 놓는다.
                    // 건물이 벽 밖으로 나가는 건 IsInsideWall 로 따로 막는다.
                    Ground[x, y] = onAvenue ? Surface.Dirt : Surface.Grass;
                    continue;
                }

                if (d > wall - WallThickness)
                {
                    Ground[x, y] = onAvenue ? Surface.Stone : Surface.Wall;   // 성문 통로
                    _taken[x, y] = true;
                    continue;
                }

                if (d <= plaza || onAvenue || Mathf.Abs(ringDist - inner) <= InnerRingWidth * 0.5f)
                {
                    Ground[x, y] = Surface.Stone;      // 광장 · 대로 · 안쪽 환상 도로
                    _taken[x, y] = true;
                    continue;
                }

                if (Mathf.Abs(ringDist - outer) <= OuterRingWidth * 0.5f || (onLane && d > inner))
                {
                    Ground[x, y] = Surface.Dirt;       // 바깥 환상 도로 · 대각선 골목
                    _taken[x, y] = true;
                    continue;
                }

                Ground[x, y] = Surface.Grass;
            }
        }
    }

    /// <summary>
    /// 구역별 바닥. 2단계는 건물이 없으므로, 바닥과 소품만으로 구역이 읽혀야 한다.
    /// 훈련장과 벌목장은 사람이 밟아 풀이 없어진 흙 마당이고, 나머지는 잔디를 둔다.
    /// </summary>
    private void PaintDistrictGround()
    {
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                if (Ground[x, y] != Surface.Grass)
                    continue;                    // 길·광장·성벽은 그대로

                District district = DistrictAt(x, y);
                if (district == District.Training || district == District.Logging)
                {
                    Ground[x, y] = Surface.Dirt;
                    _converted++;
                }
            }
        }

        PlaceDistrictProps();
        GD.Print($"[Town] 훈련장·벌목장 마당 {_converted}칸");
    }

    /// <summary>
    /// 구역을 알리는 비건물 소품. 훈련장은 허수아비·목책·무기 거치대,
    /// 벌목장은 통나무 더미·그루터기. 건물은 3~4단계 몫이다.
    /// </summary>
    private void PlaceDistrictProps()
    {
        // 마당 흙은 순환로 흙과 같은 재질이라 바닥만으로는 구역이 구분되지 않는다.
        // 구역을 읽히게 하는 건 결국 그 안에 무엇이 서 있느냐다 — 촘촘히 놓는다.
        for (int y = 2; y < _h - 4; y += 3)
        {
            for (int x = 2; x < _w - 4; x += 3)
            {
                District district = DistrictAt(x, y);
                if (district != District.Training && district != District.Logging)
                    continue;
                if (Ground[x, y] != Surface.Dirt || _rng.Randf() > 0.55f)
                    continue;

                string texture = district == District.Training
                    ? PickOne("training_dummy", "weapon_rack", "fence_section")
                    : PickOne("log_pile", "tree_stump", "log_pile");

                var rect = new Rect2I(x, y, 3, 2);
                if (!IsFree(rect))
                    continue;
                Take(rect);
                Props.Add(new PropPlacement(Abs(rect), texture));
            }
        }
    }

    private string PickOne(params string[] options)
        => options[_rng.RandiRange(0, options.Length - 1)];

    /// <summary>
    /// 성벽 바깥. 좌측은 침엽수림, 우측은 강과 나무다리, 우상단은 밭.
    /// 마을이 허공에 뜬 섬처럼 보이지 않으려면 벽 밖에도 사연이 있어야 한다.
    /// </summary>
    private void PaintOutside()
    {
        float wall = _radius * WallRadius;

        // 우측 강 — 세로로 흐른다. 성문 앞 길과 만나는 자리에 다리를 놓는다.
        int riverX = (int)(_cx + _radius * 0.86f);
        if (riverX + 5 < _w)
        {
            for (int y = 0; y < _h; y++)
            {
                for (int x = riverX; x < Mathf.Min(_w, riverX + 5); x++)
                {
                    if (Ground[x, y] == Surface.Dirt)
                        continue;                       // 성문 앞 길은 남긴다(다리 자리)
                    Ground[x, y] = Surface.Water;
                    _taken[x, y] = true;
                }
            }
            Props.Add(new PropPlacement(
                Abs(new Rect2I(riverX - 1, (int)_cy - 1, 7, 3)), "bridge", solid: false));
        }

        // 좌측 침엽수림 — 벽에서 떨어진 바깥쪽에 빽빽하게.
        for (int y = 1; y < _h - 4; y += 3)
        {
            for (int x = 1; x < _w - 3; x += 3)
            {
                float dx = x - _cx, dy = y - _cy;
                if (OctDist(dx, dy) < wall + 4f || dx > -_radius * 0.35f)
                    continue;
                if (_rng.Randf() > 0.5f)
                    continue;

                var rect = new Rect2I(x, y, 2, 3);
                if (!IsFree(rect) || Ground[x, y] != Surface.Grass)
                    continue;
                Take(rect);
                Props.Add(new PropPlacement(Abs(rect), "tree_conifer"));
            }
        }

        // 우상단 밭 — 강 이쪽 편, 성벽 바깥.
        int fx = (int)(_cx + wall + 4);
        int fy = 3;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                var rect = new Rect2I(fx + col * 5, fy + row * 4, 4, 3);
                if (!IsFree(rect))
                    continue;
                Take(rect);
                Props.Add(new PropPlacement(Abs(rect), "farm_plot", solid: false));
            }
        }
    }

    // ── 미리 잡아 두는 구역 ───────────────────────────────────────

    /// <summary>성문 네 개. 성벽을 뚫어 둔 자리에 문루를 얹는다.</summary>
    private void PlaceGates()
    {
        int r = (int)(_radius * WallRadius) - WallThickness / 2;
        int cx = (int)_cx, cy = (int)_cy;

        Add(cx - 2, cy - r - 2, 5, 4);      // 북문
        Add(cx - 2, cy + r - 2, 5, 4);      // 남문
        Add(cx - r - 2, cy - 2, 5, 4);      // 서문
        Add(cx + r - 2, cy - 2, 5, 4);      // 동문

        void Add(int x, int y, int w, int h)
            => Props.Add(new PropPlacement(Abs(new Rect2I(x, y, w, h)), "gatehouse", solid: false));
    }

    /// <summary>서쪽 훈련장 — 건물과 울타리 마당.</summary>
    private void ReserveTraining()
    {
        int x = (int)(_cx - _radius * 0.62f);
        int y = (int)(_cy - 11);

        var pad = new Rect2I(x - 2, y - 3, 30, 26);
        FillGround(pad, Surface.Dirt);
        Take(pad);

        Props.Add(new PropPlacement(Abs(new Rect2I(x, y, 10, 7)), "training_hall"));

        var yard = new Rect2I(x + 12, y + 1, 14, 18);
        TrainingYard = Abs(yard);
        YardGateY = TrainingYard.Position.Y + 7;
        TrainingDummySpot = WorldLayout.TileCenter(
            TrainingYard.Position.X + 7, TrainingYard.Position.Y + 12);

        Props.Add(new PropPlacement(Abs(new Rect2I(x + 1, y + 9, 3, 3)), "barrels"));
        Props.Add(new PropPlacement(Abs(new Rect2I(x + 6, y + 9, 3, 3)), "barrels"));
    }

    /// <summary>동쪽 벌목장 — 통나무를 쌓아 둔 흙 마당.</summary>
    private void ReserveLogging()
    {
        int x = (int)(_cx + _radius * 0.44f);
        int y = (int)(_cy - 10);

        var yard = new Rect2I(x, y, 22, 22);
        FillGround(yard, Surface.Dirt);
        Take(yard);

        for (int i = 0; i < 6; i++)
        {
            int px = x + 2 + _rng.RandiRange(0, 16);
            int py = y + 2 + _rng.RandiRange(0, 16);
            Props.Add(new PropPlacement(Abs(new Rect2I(px, py, 3, 3)), "barrels"));
        }
    }

    /// <summary>광장 — 분수와 노점, 게시판. 한가운데(부활 지점)는 비워 둔다.</summary>
    private void PlacePlazaFurniture()
    {
        int cx = (int)_cx, cy = (int)_cy;

        // 1단계 광장에는 분수만 선다. 노점·게시판은 5단계(빈 공간 채우기)에서.
        Add(cx - 1, cy - 2, 3, 3, "fountain");

        void Add(int x, int y, int w, int h, string texture)
            => Props.Add(new PropPlacement(Abs(new Rect2I(x, y, w, h)), texture));
    }

    // ── 건물 ─────────────────────────────────────────────────────

    /// <summary>
    /// 구역. 명세의 방위를 그대로 따른다 —
    /// 중앙 광장 둘레 상업, 남~남서 주거(가장 밀도 높음), 서 훈련장,
    /// 북동 길드, 동 벌목장.
    /// </summary>
    public enum District { None, Market, Residential, Training, Guild, Logging }

    public District DistrictAt(int x, int y)
    {
        float dx = x - _cx, dy = y - _cy;
        float d = OctDist(dx, dy) / _radius;

        if (d >= WallRadius - 0.02f)
            return District.None;                        // 성벽 바깥
        // 상업 구역은 광장을 두르는 띠까지만. 여기를 넓게 잡으면 나머지 구역이
        // 성벽과 순환로 사이 몇 타일로 눌려 구역이 눈에 안 보인다.
        if (d < PlazaRadius + 0.10f)
            return District.Market;

        // 각도로 부채꼴을 가른다. 0도가 동쪽, 시계 반대 방향.
        float deg = Mathf.RadToDeg(Mathf.Atan2(-dy, dx));
        if (deg < 0f)
            deg += 360f;

        if (deg >= 22.5f && deg < 67.5f)   return District.Guild;      // 북동
        if (deg >= 157.5f && deg < 202.5f) return District.Training;   // 서
        if (deg < 22.5f || deg >= 337.5f)  return District.Logging;    // 동
        if (deg >= 202.5f && deg < 315f)   return District.Residential; // 남서~남
        return District.Residential;
    }

    private BuildingKind Pick(District district) => district switch
    {
        District.Market => _rng.Randf() < 0.45f
            ? PickShop()
            : TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)],

        District.Guild => _rng.Randf() < 0.18f
            ? Manor
            : TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)],

        District.Training or District.Logging
            => Shacks[_rng.RandiRange(0, Shacks.Length - 1)],

        _ => _rng.Randf() < 0.22f
            ? TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)]
            : Cottages[_rng.RandiRange(0, Cottages.Length - 1)],
    };

    private BuildingKind PickShop()
    {
        for (int tries = 0; tries < 8; tries++)
        {
            BuildingKind shop = Shops[_rng.RandiRange(0, Shops.Length - 1)];
            _shopCount.TryGetValue(shop.Texture, out int used);
            if (used < MaxPerShop)
            {
                _shopCount[shop.Texture] = used + 1;
                return shop;
            }
        }
        return TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)];
    }

    /// <summary>
    /// 집을 한 겹 자라게 한다.
    ///
    /// roadFrontOnly 면 아래가 길에 닿는 자리에만 세운다 — 첫 겹이 거리를 따라
    /// 늘어서게 하는 것이 목적이다. 다음 겹부터는 이미 선 집 옆에도 붙을 수 있어,
    /// 거리에서 안쪽으로 번지듯 무리가 커진다. 격자로 찍는 것과 달리 길의 모양을
    /// 따라가므로 굽은 길·방사형 길에서도 자연스럽다.
    /// </summary>
    private void GrowBuildings(bool roadFrontOnly)
    {
        for (int y = 1; y < _h - 1; y++)
        {
            for (int x = 1; x < _w - 1; x++)
            {
                if (!IsInsideWall(x, y))
                    continue;                    // 성벽 밖에는 집을 짓지 않는다

                District district = DistrictAt(x, y);
                BuildingKind kind = Pick(district);
                var rect = new Rect2I(x, y, kind.W, kind.H);

                // 여백 없이 자리만 본다. Grow(1) 로 여백을 요구하면 길이 이미
                // 점유 상태라 건물이 길에 절대 못 붙고, 마을이 성기게 흩어진다.
                // 집은 길에 어깨를 대고 서야 거리가 생긴다.
                if (!IsFree(rect) || !FrontsSomething(rect, roadFrontOnly))
                    continue;

                Take(rect);

                // 문 앞 두 칸만 다진 흙. 건물을 통째로 흙으로 감싸면 마을이
                // 공사장처럼 보인다 — 바탕은 풀이어야 한다.
                FillGroundSoft(new Rect2I(x + kind.W / 2 - 1, y + kind.H, 2, 1), Surface.Dirt);

                Props.Add(new PropPlacement(Abs(rect), kind.Texture));
                x += kind.W;                       // 어깨를 맞대고 다음 자리로
            }
        }
    }

    /// <summary>집 아래가 길이거나(첫 겹), 옆에 이미 집이 있는가(다음 겹).</summary>
    private bool FrontsSomething(Rect2I rect, bool roadOnly)
    {
        int belowY = rect.Position.Y + rect.Size.Y;
        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        {
            for (int dy = 0; dy <= 2; dy++)
            {
                int y = belowY + dy;
                if (Inside(x, y) && (Ground[x, y] == Surface.Stone || Ground[x, y] == Surface.Dirt))
                    return true;
            }
        }

        if (roadOnly)
            return false;

        // 이미 선 집에 어깨를 붙인다. (길·성벽은 Ground 로 걸러진다)
        for (int y = rect.Position.Y - 2; y < rect.Position.Y + rect.Size.Y + 2; y++)
        {
            for (int x = rect.Position.X - 2; x < rect.Position.X + rect.Size.X + 2; x++)
            {
                if (Inside(x, y) && _taken[x, y] && Ground[x, y] == Surface.Grass)
                    return true;
            }
        }
        return false;
    }

    /// <summary>남은 풀밭에 나무를 흩는다. 집 사이가 허전하면 마을이 헐거워 보인다.</summary>
    private void ScatterTrees()
    {
        for (int y = 2; y < _h - 6; y += 3)
        {
            for (int x = 2; x < _w - 5; x += 3)
            {
                if (_rng.Randf() > 0.18f)
                    continue;

                bool poplar = _rng.Randf() < 0.4f;
                var rect = new Rect2I(x, y, poplar ? 3 : 4, 5);
                if (!IsFree(rect) || Ground[x, y] != Surface.Grass)
                    continue;

                Take(rect);
                Props.Add(new PropPlacement(Abs(rect), poplar ? "tree_poplar" : "tree_oak"));
            }
        }
    }

    // ── 격자 도우미 ──────────────────────────────────────────────

    private Rect2I Abs(Rect2I local)
        => new(local.Position.X + _ox, local.Position.Y + _oy, local.Size.X, local.Size.Y);

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

    /// <summary>성벽 안쪽인가. 건물은 여기에만 선다.</summary>
    private bool IsInsideWall(int x, int y)
        => OctDist(x - _cx, y - _cy) < _radius * WallRadius - WallThickness;

    private bool IsFree(Rect2I r)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || _taken[x, y])
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

    private void FillGround(Rect2I r, Surface surface)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (Inside(x, y))
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
