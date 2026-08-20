using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>바닥 재질. 숫자가 클수록 위에 얹힌다 — Wang 정점 판정에서 큰 쪽이 이긴다.</summary>
public enum Surface
{
    Grass = 0,
    Dirt = 1,
    Stone = 2,
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

/// <summary>건물 한 종류의 크기와 쓰임.</summary>
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
/// 초보자 마을을 만든다. 188x188 타일에 건물이 수백 채라 손으로는 못 놓는다.
///
/// 마을처럼 보이게 하는 핵심은 크기가 아니라 **건물이 길에 붙어 줄지어 서는 것**이다.
/// 잔디 한가운데 예쁜 집을 한 채씩 놓으면 아무리 많아도 전시장처럼 보인다.
/// 그래서 이렇게 만든다:
///
///   1) 마을 사각형을 거리로 재귀 분할한다. 깊이가 얕으면 넓은 돌길(대로),
///      깊어질수록 좁은 돌길, 마지막에는 흙 골목이 된다. 분할 위치를 흔들어
///      바둑판처럼 반듯해지지 않게 한다.
///   2) 남은 블록마다 건물을 **가로 줄**로 채운다. 한 줄 안에서는 서로 어깨를
///      맞대고 붙고, 줄과 줄 사이에는 흙길이 난다. 이게 밀도를 만든다.
///   3) 어디에 무엇이 서는지는 중심에서의 거리와 방향으로 정한다 —
///      중심은 상점가, 북동은 부촌, 남서는 빈민가, 나머지는 주택가.
///
/// 광장·훈련소·뒷골목처럼 '있어야 할 자리에 있어야 하는 것'은 생성 전에 미리
/// 자리를 잡아 두고, 생성기가 그 위를 침범하지 않게 한다.
///
/// 같은 씨앗이면 같은 마을이 나온다 — 세이브에 적힌 좌표가 다음 실행에서
/// 건물 안이 되면 안 되기 때문이다.
/// </summary>
public sealed class TownGenerator
{
    private const int Seed = 20260820;

    // ── 건물 목록 (타일 크기) ─────────────────────────────────────
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

    // ── 거리 규격 ────────────────────────────────────────────────
    /// <summary>깊이별 길 폭. 얕을수록 큰길이다.</summary>
    private static readonly int[] RoadWidthByDepth = { 5, 4, 3, 3, 2, 2 };

    /// <summary>이 깊이부터는 돌길이 아니라 흙 골목이다.</summary>
    private const int DirtFromDepth = 4;

    /// <summary>블록이 이보다 작아지면 더 쪼개지 않는다.</summary>
    private const int MinBlock = 15;

    /// <summary>건물 줄과 줄 사이에 두는 뒷길 폭.</summary>
    private const int RowGap = 3;

    /// <summary>건물이 길에서 떨어지는 여백. 0이면 문이 길에 바로 붙는다.</summary>
    private const int StreetMargin = 1;

    // ── 결과 ─────────────────────────────────────────────────────
    public Surface[,] Ground { get; private set; }
    public List<PropPlacement> Props { get; } = new();

    /// <summary>광장 한가운데 — 부활 지점.</summary>
    public Vector2 PlazaCenter { get; private set; }

    /// <summary>훈련장 안 허수아비 자리.</summary>
    public Vector2 TrainingDummySpot { get; private set; }

    /// <summary>훈련장 울타리 (타일 좌표, 마을 절대 좌표계).</summary>
    public Rect2I TrainingYard { get; private set; }

    public int YardGateY { get; private set; }
    public const int YardGateHeight = 4;

    private readonly Rect2I _town;
    private readonly int _w;
    private readonly int _h;
    private readonly int _ox;
    private readonly int _oy;

    /// <summary>이미 무언가 차지한 칸. 건물끼리 겹치지 않게 하는 유일한 수단.</summary>
    private readonly bool[,] _taken;

    private readonly RandomNumberGenerator _rng = new();

    public TownGenerator(Rect2I town)
    {
        _town = town;
        _ox = town.Position.X;
        _oy = town.Position.Y;
        _w = town.Size.X;
        _h = town.Size.Y;

        Ground = new Surface[_w, _h];
        _taken = new bool[_w, _h];
        _rng.Seed = Seed;
    }

    public void Generate()
    {
        ReservePlaza(out Rect2I plaza);
        ReserveTraining();

        // 한 번만 쪼갠다. 길을 낼 때와 건물을 채울 때 난수를 따로 뽑으면
        // 두 결과가 어긋나 건물이 길 위에 선다 — 쪼개면서 잎 블록을 모아 둔다.
        Split(new Rect2I(0, 0, _w, _h), 0);

        foreach (Rect2I block in _blocks)
            FillBlock(block, plaza);

        PlacePlazaFurniture(plaza);
    }

    /// <summary>더 쪼개지지 않은 블록들 — 여기에 건물이 선다.</summary>
    private readonly List<Rect2I> _blocks = new();

    // ── 미리 잡아 두는 자리 ───────────────────────────────────────

    /// <summary>
    /// 마을 한가운데 광장. 거리 분할이 여기를 지나가지 않도록 먼저 자리를 잡는다.
    /// 큰길이 광장으로 모이는 그림이 나와야 마을 중심으로 읽힌다.
    /// </summary>
    private void ReservePlaza(out Rect2I plaza)
    {
        const int size = 26;
        int x = _w / 2 - size / 2;
        int y = _h / 2 - size / 2;
        plaza = new Rect2I(x, y, size, size);

        FillGround(plaza, Surface.Stone);
        Take(plaza);          // 여기엔 건물이 서지 않는다. 길도 광장 앞에서 멈춘다.
        PlazaCenter = WorldLayout.TileCenter(_ox + _w / 2, _oy + _h / 2);
    }

    /// <summary>남동쪽 훈련소 — 건물 하나와 울타리 마당.</summary>
    private void ReserveTraining()
    {
        int x = _w - 46;
        int y = _h - 44;

        var hall = new Rect2I(x, y, 10, 7);
        FillGround(new Rect2I(x - 2, y - 2, 34, 36), Surface.Dirt);
        Take(new Rect2I(x - 2, y - 2, 34, 36));       // 생성기가 여기 못 짓게 막는다
        Props.Add(new PropPlacement(Abs(hall), "training_hall"));

        var yard = new Rect2I(x + 12, y + 2, 18, 18);
        TrainingYard = Abs(yard);
        YardGateY = TrainingYard.Position.Y + 7;
        TrainingDummySpot = WorldLayout.TileCenter(
            TrainingYard.Position.X + 9, TrainingYard.Position.Y + 12);

        // 마당 둘레에 무기 상자 몇 개.
        Props.Add(new PropPlacement(Abs(new Rect2I(x, y + 9, 3, 3)), "barrels"));
        Props.Add(new PropPlacement(Abs(new Rect2I(x + 5, y + 9, 3, 3)), "barrels"));
    }

    // ── 거리 ─────────────────────────────────────────────────────

    /// <summary>
    /// 블록을 길로 반 가른다. 가르는 위치를 40~60% 사이에서 흔들어 바둑판을 피한다.
    /// 길게 뻗은 쪽을 자르므로 블록이 지나치게 길쭉해지지 않는다.
    /// </summary>
    private void Split(Rect2I block, int depth)
    {
        bool vertical = block.Size.X >= block.Size.Y;
        int span = vertical ? block.Size.X : block.Size.Y;

        if (depth >= RoadWidthByDepth.Length || span < MinBlock * 2)
        {
            _blocks.Add(block);
            return;
        }

        int width = RoadWidthByDepth[depth];
        int lo = (int)(span * 0.40f);
        int hi = (int)(span * 0.60f);
        int at = _rng.RandiRange(lo, Mathf.Max(lo, hi - width));

        Rect2I road = vertical
            ? new Rect2I(block.Position.X + at, block.Position.Y, width, block.Size.Y)
            : new Rect2I(block.Position.X, block.Position.Y + at, block.Size.X, width);

        // 광장·훈련소를 지나는 길은 놓지 않는다 — 미리 잡아 둔 자리는 건드리지 않는다.
        Surface surface = depth >= DirtFromDepth ? Surface.Dirt : Surface.Stone;
        FillGroundSkippingTaken(road, surface);

        Rect2I a = vertical
            ? new Rect2I(block.Position.X, block.Position.Y, at, block.Size.Y)
            : new Rect2I(block.Position.X, block.Position.Y, block.Size.X, at);
        Rect2I b = vertical
            ? new Rect2I(block.Position.X + at + width, block.Position.Y,
                block.Size.X - at - width, block.Size.Y)
            : new Rect2I(block.Position.X, block.Position.Y + at + width,
                block.Size.X, block.Size.Y - at - width);

        Split(a, depth + 1);
        Split(b, depth + 1);
    }

    // ── 건물 ─────────────────────────────────────────────────────

    /// <summary>
    /// 블록 하나를 건물 가로 줄로 채운다.
    ///
    /// 한 줄 안에서 건물은 0~1칸 띄우고 어깨를 맞댄다. 줄과 줄 사이에는 뒷길이
    /// 난다. 이 '줄 + 뒷길'이 반복되는 것이 마을의 밀도다.
    /// </summary>
    private void FillBlock(Rect2I block, Rect2I plaza)
    {
        Rect2I inner = block.Grow(-StreetMargin);
        if (inner.Size.X < 5 || inner.Size.Y < 4)
            return;

        District district = DistrictAt(block, plaza);

        int y = inner.Position.Y;
        while (y < inner.Position.Y + inner.Size.Y)
        {
            int rowHeight = PlaceRow(inner.Position.X, y,
                inner.Position.X + inner.Size.X, district);
            if (rowHeight <= 0)
                break;

            // 줄 아래에 뒷길을 내고 다음 줄로.
            var lane = new Rect2I(inner.Position.X, y + rowHeight,
                inner.Size.X, Mathf.Min(RowGap, inner.Position.Y + inner.Size.Y - y - rowHeight));
            if (lane.Size.Y > 0)
                FillGroundSkippingTaken(lane, Surface.Dirt);

            y += rowHeight + RowGap;
        }
    }

    /// <summary>건물 한 줄. 놓은 줄의 높이를 돌려준다(못 놓았으면 0).</summary>
    private int PlaceRow(int x0, int y, int xEnd, District district)
    {
        int tallest = 0;
        int x = x0;

        while (x < xEnd)
        {
            BuildingKind kind = Pick(district);
            if (x + kind.W > xEnd || y + kind.H > _h)
                break;

            var rect = new Rect2I(x, y, kind.W, kind.H);
            if (IsFree(rect))
            {
                Take(rect);

                // 건물 둘레를 다진 흙으로 깐다. 이걸 안 하면 블록마다 잔디 띠가
                // 그대로 남아 마을이 아니라 초록 리본처럼 보인다. 사람이 밟고 사는
                // 자리는 풀이 남지 않는다.
                FillGroundSoft(rect.Grow(1), Surface.Dirt);

                Props.Add(new PropPlacement(Abs(rect), kind.Texture));
                tallest = Mathf.Max(tallest, kind.H);
                x += kind.W + _rng.RandiRange(0, 1);   // 어깨를 맞대거나 한 칸만 띄운다
            }
            else
            {
                x += 1;
            }
        }

        return tallest;
    }

    private enum District { Core, Wealthy, Poor, Residential }

    private District DistrictAt(Rect2I block, Rect2I plaza)
    {
        int cx = block.Position.X + block.Size.X / 2;
        int cy = block.Position.Y + block.Size.Y / 2;

        float dx = (cx - _w * 0.5f) / (_w * 0.5f);
        float dy = (cy - _h * 0.5f) / (_h * 0.5f);
        float ring = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

        if (ring < 0.30f)
            return District.Core;                    // 광장 둘레 — 상점가
        if (dx > 0.25f && dy < -0.25f)
            return District.Wealthy;                 // 북동 부촌
        if (dx < -0.25f && dy > 0.25f)
            return District.Poor;                    // 남서 빈민가
        return District.Residential;
    }

    /// <summary>상점 종류별로 이미 몇 채를 세웠는지. 같은 가게가 늘어서지 않게 막는다.</summary>
    private readonly Dictionary<string, int> _shopCount = new();

    /// <summary>한 종류당 최대 채수. 마을에 대장간이 열 곳 있으면 마을이 아니다.</summary>
    private const int MaxPerShop = 2;

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

    private BuildingKind Pick(District district) => district switch
    {
        District.Core => _rng.Randf() < 0.35f
            ? PickShop()
            : TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)],

        District.Wealthy => _rng.Randf() < 0.30f
            ? Manor
            : TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)],

        District.Poor => Shacks[_rng.RandiRange(0, Shacks.Length - 1)],

        _ => _rng.Randf() < 0.25f
            ? TownHouses[_rng.RandiRange(0, TownHouses.Length - 1)]
            : Cottages[_rng.RandiRange(0, Cottages.Length - 1)],
    };

    private void PlacePlazaFurniture(Rect2I plaza)
    {
        int cx = plaza.Position.X + plaza.Size.X / 2;
        int cy = plaza.Position.Y + plaza.Size.Y / 2;

        Add(new Rect2I(cx - 7, cy - 8, 5, 5), "fountain");
        Add(new Rect2I(cx + 4, cy - 7, 3, 3), "notice_board");
        Add(new Rect2I(cx - 8, cy + 4, 4, 3), "market_stall_a");
        Add(new Rect2I(cx - 2, cy + 6, 4, 3), "market_stall_b");
        Add(new Rect2I(cx + 4, cy + 4, 4, 3), "market_stall_a");
        Add(new Rect2I(cx + 8, cy - 2, 3, 3), "barrels");

        // 광장은 통째로 Taken 이라 IsFree 로는 못 놓는다. 자리를 손으로 정했고
        // 서로 겹치지 않는 것을 확인했으므로 그대로 놓는다.
        void Add(Rect2I r, string texture)
            => Props.Add(new PropPlacement(Abs(r), texture));
    }

    // ── 격자 도우미 ──────────────────────────────────────────────

    private Rect2I Abs(Rect2I local)
        => new(local.Position.X + _ox, local.Position.Y + _oy, local.Size.X, local.Size.Y);

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

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

    /// <summary>잔디인 칸만 바꾼다. 이미 깔린 돌길·흙길을 덮어쓰지 않는다.</summary>
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

    /// <summary>이미 잡아 둔 자리(광장·훈련소)는 건너뛰고 바닥을 칠한다.</summary>
    private void FillGroundSkippingTaken(Rect2I r, Surface surface)
    {
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
            {
                if (!Inside(x, y) || _taken[x, y])
                    continue;
                Ground[x, y] = surface;
                _taken[x, y] = true;           // 길에는 건물이 서지 않는다
            }
        }
    }
}
