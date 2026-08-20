using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>바닥 재질. 숫자가 클수록 위에 얹힌다 — 정점 판정에서 큰 쪽이 이긴다.</summary>
public enum Surface
{
    Grass = 0,
    Dirt = 1,
    Stone = 2,
}

/// <summary>길·마당 한 조각. 좌표는 타일 단위.</summary>
public readonly struct PathDef
{
    public readonly Rect2I Tiles;
    public readonly Surface Surface;

    public PathDef(int x, int y, int w, int h, Surface surface)
    {
        Tiles = new Rect2I(x, y, w, h);
        Surface = surface;
    }
}

/// <summary>소품 한 개. Tiles 는 '어디에 놓을지'만 정하고, 실제로 막는 범위는 그림에서 나온다.</summary>
public readonly struct PropDef
{
    public readonly Rect2I Tiles;
    public readonly string Texture;

    /// <summary>지나갈 수 없는가. 나무·집은 true, 화단 같은 장식은 false.</summary>
    public readonly bool Solid;

    /// <summary>그림 아래를 타일 사각형 아래에 맞출지. 건물·나무처럼 '땅에 선' 것은 true.</summary>
    public readonly bool BottomAlign;

    public PropDef(int x, int y, int w, int h, string texture,
        bool solid = true, bool bottomAlign = true)
    {
        Tiles = new Rect2I(x, y, w, h);
        Texture = texture;
        Solid = solid;
        BottomAlign = bottomAlign;
    }
}

/// <summary>
/// 초보자 마을의 전체 배치.
///
/// 구조는 격자 거리다. 돌길 큰길이 남북·동서로 마을을 가르고, 그 교차점이 광장이다.
/// 흙 골목이 큰길 사이를 잇는다. 거리로 나뉜 구획마다 성격이 다르다 —
///
///   북서: 주택가 (평범한 오두막)          북동: 부촌 (저택과 2층집)
///   광장 둘레: 상점가                      서/동: 연금술사·빵집
///   남서: 빈민가 (판잣집, 뒷골목 끝에 무언가)
///   남동: 훈련소와 훈련장
///
/// 좌표는 전부 타일 단위이고 Town(2,2,56,64) 안에 들어간다.
/// 손으로 놓는 사각형이 서른 개가 넘으므로 Validate() 가 시작할 때 겹침을 잡는다.
/// </summary>
public static class VillagePlan
{
    // ── 거리와 골목 ────────────────────────────────────────────────
    // 뒤에 오는 것이 앞의 것을 덮는다. 잔디 → 흙 → 돌 순서로 적어야 큰길이 위로 온다.
    public static readonly PathDef[] Paths =
    {
        // 흙 골목 — 큰길 사이를 잇는 좁은 길.
        new(9, 2, 2, 64, Surface.Dirt),      // 서쪽 세로 골목
        new(16, 50, 2, 14, Surface.Dirt),    // 빈민가 세로 골목
        new(2, 63, 25, 2, Surface.Dirt),     // 빈민가 뒷골목 (막다른 길)
        new(44, 50, 2, 14, Surface.Dirt),    // 훈련장 진입로

        // 돌길 큰길.
        new(2, 16, 56, 2, Surface.Stone),    // 북쪽 거리
        new(2, 48, 56, 2, Surface.Stone),    // 남쪽 거리
        new(28, 2, 4, 64, Surface.Stone),    // 세로 큰길
        new(2, 32, 56, 4, Surface.Stone),    // 가로 큰길 — 동쪽 성문(y31~36)으로 이어진다
        new(21, 24, 18, 15, Surface.Stone),  // 광장
    };

    // ── 건물과 소품 ────────────────────────────────────────────────
    public static readonly PropDef[] Props =
    {
        // 북서 주택가
        new(2, 3, 6, 5, "cottage_a"),
        new(11, 3, 6, 5, "cottage_b"),
        new(19, 3, 6, 5, "cottage_c"),
        new(2, 10, 6, 5, "cottage_a"),
        new(11, 10, 6, 5, "cottage_b"),
        new(19, 9, 5, 6, "townhouse_a"),
        new(25, 3, 3, 5, "tree_poplar"),
        new(25, 10, 3, 5, "tree_poplar"),

        // 북동 부촌
        new(34, 3, 12, 9, "manor"),
        new(48, 3, 5, 6, "townhouse_b"),
        new(48, 10, 5, 6, "townhouse_a"),
        new(54, 4, 3, 5, "tree_poplar"),

        // 광장 북쪽 상점
        new(18, 18, 7, 6, "shop_blacksmith"),
        new(33, 18, 7, 6, "shop_general"),
        new(25, 18, 3, 3, "barrels", solid: true),

        // 광장 좌우 상점
        new(11, 24, 7, 6, "shop_alchemist"),
        new(41, 24, 7, 6, "shop_bakery"),

        // 광장 남쪽 상점
        new(18, 40, 8, 7, "shop_inn"),
        new(33, 40, 7, 6, "shop_armor"),
        new(40, 40, 3, 3, "barrels"),

        // 광장 안 — 분수, 게시판, 노점
        new(22, 25, 5, 5, "fountain"),
        new(34, 29, 3, 3, "notice_board"),
        new(22, 36, 4, 3, "market_stall_a", solid: true),
        new(34, 36, 4, 3, "market_stall_b", solid: true),

        // 마을 곳곳의 나무와 잡동사니. 거리로 나뉜 구획이 텅 비면 마을로 안 보인다.
        new(5, 19, 4, 5, "tree_oak"),
        new(52, 19, 4, 5, "tree_oak"),
        new(5, 40, 4, 5, "tree_oak"),
        new(52, 40, 4, 5, "tree_oak"),
        new(12, 36, 4, 5, "tree_oak"),
        new(12, 42, 3, 5, "tree_poplar"),
        new(44, 36, 3, 5, "tree_poplar"),
        new(49, 36, 3, 5, "tree_poplar"),
        new(54, 25, 3, 5, "tree_poplar"),
        new(2, 19, 3, 3, "barrels"),
        new(2, 25, 3, 3, "barrels"),
        new(2, 36, 3, 3, "barrels"),
        new(2, 44, 3, 3, "barrels"),
        new(25, 51, 3, 3, "barrels"),

        // 남서 빈민가 — 판잣집. 뒷골목 끝에 간판 없는 건물이 하나 있다.
        new(2, 51, 5, 4, "shack_a"),
        new(11, 51, 5, 4, "shack_b"),
        new(19, 51, 5, 4, "shack_a"),
        new(2, 57, 5, 4, "shack_b"),
        new(11, 57, 5, 4, "shack_a"),
        new(19, 57, 6, 5, "backalley_door"),

        // 남동 훈련소. 울타리 안의 허수아비는 실제로 때릴 수 있는 개체라
        // 여기 장식으로 두지 않는다 — 똑같이 생겼는데 하나만 반응하면 헷갈린다.
        new(33, 51, 10, 7, "training_hall"),
    };

    /// <summary>훈련장 울타리. 테두리만 막고 안은 흙바닥이다.</summary>
    public static readonly Rect2I TrainingYard = new(46, 51, 11, 12);

    /// <summary>울타리 서쪽 출입구가 뚫리는 세로 구간 (훈련장 진입로에서 들어온다).</summary>
    public const int YardGateY = 56;
    public const int YardGateHeight = 3;

    /// <summary>때릴 수 있는 허수아비 자리 — 울타리 안.</summary>
    public static Vector2 TrainingDummySpot => WorldLayout.TileCenter(50, 59);

    /// <summary>
    /// 사각형끼리 겹치는지 검사한다. 손으로 놓은 배치라 오타 한 글자면 건물이 겹치는데,
    /// 겹치면 화면에서는 '조금 이상한' 정도로만 보여서 놓치기 쉽다. 시작할 때 로그로 잡는다.
    /// </summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        Rect2I town = WorldLayout.Town.Tiles;

        for (int i = 0; i < Props.Length; i++)
        {
            Rect2I a = Props[i].Tiles;

            if (!town.Encloses(a))
                problems.Add($"{Props[i].Texture} {a} 가 마을 밖으로 나갔다");

            for (int j = i + 1; j < Props.Length; j++)
            {
                if (a.Intersects(Props[j].Tiles))
                    problems.Add($"{Props[i].Texture} {a} 와 {Props[j].Texture} {Props[j].Tiles} 가 겹친다");
            }

            // 울타리 '테두리'와 겹치는 것만 문제다. 안쪽에 놓는 건 정상.
            if (a.Intersects(TrainingYard) && !TrainingYard.Grow(-1).Encloses(a))
                problems.Add($"{Props[i].Texture} {a} 가 훈련장 울타리에 걸친다");
        }

        return problems;
    }
}
