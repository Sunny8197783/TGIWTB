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

    /// <summary>
    /// 곱할 색. 흰색이면 그림 그대로.
    ///
    /// 소품 그림은 전부 마을용이라 밑동에 잔디가 붙어 있다. 그걸 갱도나
    /// 잿물 골짜기에 그대로 놓으면 어두운 땅 위에 초록 잔디 조각이 뜬다 —
    /// 구역 색을 곱해 그 구역에서 자란 것처럼 만든다.
    /// </summary>
    public readonly Color Tint;

    public PropPlacement(Rect2I tiles, string texture, bool solid = true, bool flipH = false)
        : this(tiles, texture, Colors.White, solid, flipH)
    {
    }

    public PropPlacement(Rect2I tiles, string texture, Color tint,
                         bool solid = true, bool flipH = false)
    {
        Tiles = tiles;
        Texture = texture;
        Solid = solid;
        FlipH = flipH;
        Tint = tint;
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
    public Surface[,] Ground { get; }
    public List<PropPlacement> Props { get; } = new();
    public Dictionary<string, Rect2I> Landmarks { get; } = new();
    public Vector2 TrainingDummySpot => WorldLayout.TileCenter(109, 124);
    public Rect2I TrainingYard => new(105, 120, 9, 8);
    public int YardGateY => 124;
    public const int YardGateHeight = 4;
    private readonly Rect2I _town;
    private readonly bool[,] _road;
    public TownGenerator(Rect2I town)
    { _town = town; Ground = new Surface[town.Size.X, town.Size.Y]; _road = new bool[town.Size.X, town.Size.Y]; }
    public bool IsRoadCell(int x, int y) => x >= 0 && y >= 0 && x < _town.Size.X && y < _town.Size.Y && _road[x,y];
    public void Generate()
    {
        foreach (var route in MeadowLayout.Data.Routes)
            for (int i = 1; i < route.Length; i++)
                Lane(new(route[i-1][0], route[i-1][1]), new(route[i][0], route[i][1]), .55f);
        House("inn", 83, 103, 5, 4);
        House("bakery", 94, 100, 4, 4);
        House("apothecary", 106, 104, 4, 4);
        House("smithy", 111, 111, 5, 4);
        House("training_hall", 116, 119, 4, 3);
        House("stable", 78, 117, 5, 4);
        House("kit_m_cottage", 86, 121, 4, 3);
        House("kit_m03", 93, 126, 4, 3);
        House("kit_s02", 102, 128, 3, 3);
        House("kit_m07", 78, 109, 4, 3);
        House("chapel", 99, 93, 5, 4);
        House("kit_m08", 115, 103, 3, 3);
        Landmarks["guild"] = Landmarks["training_hall"];
        Landmarks["kit_xl_guild"] = Landmarks["training_hall"];
        Landmarks["plaza"] = new(95,114,1,1);
        Landmarks["town_hall"] = Landmarks["chapel"];
        Landmarks["tavern"] = Landmarks["inn"];
        Landmarks["warehouse"] = Landmarks["stable"];
        Landmarks["black_market"] = Landmarks["apothecary"];
        Landmarks["windmill"] = Landmarks["stable"];
        Landmarks["gate_east"] = new(119,112,1,1);
        Landmarks["gate_south"] = new(99,135,1,1);
        Landmarks["training"] = TrainingYard;
        // Small shared green: well, shade tree and a bench leave the arrival space open.
        Decor("well", 92,111,2,2);
        Decor("tree_oak", 99,108,3,4);
        Decor("bench", 98,112,2,1, false);
        Decor("flower_pots", 87,106,1,1, false);
        Decor("flower_pots", 107,107,1,1, false);
        Decor("firewood", 115,114,1,1);
        Decor("hand_cart", 75,123,2,1);
        Decor("farm_plot", 79,126,3,2, false);
        Decor("farm_plot", 83,127,3,2, false);
        Decor("chickens", 85,126,1,1, false);
        Decor("lamp_post", 91,117,1,1, false);
        Decor("lamp_post", 108,116,1,1, false);
        foreach (var p in new[]{new Vector2I(84,99),new(91,94),new(103,99),new(112,99),new(119,108),
            new(117,123),new(112,130),new(98,125),new(89,128),new(75,121),new(73,106),new(84,114)})
            Decor("tree_oak",p.X,p.Y,3,3);
        GD.Print($"[ForestVillage] buildings=12 props={Props.Count}");
    }
    private void House(string name, int x, int y, int w, int h)
    {
        var rect = new Rect2I(x,y,w,h); Landmarks[name] = rect;
        Props.Add(new(rect, name));
        Vector2 door = new(x + w / 2, y + h + 1);
        Vector2 closest = door; float best = float.MaxValue;
        foreach (var route in MeadowLayout.Data.Routes)
            for (int i=1; i<route.Length; i++)
            {
                Vector2 p = Geometry2D.GetClosestPointToSegment(door,
                    new(route[i-1][0],route[i-1][1]), new(route[i][0],route[i][1]));
                if (p.Y < door.Y) continue;
                if (door.DistanceSquaredTo(p) < best) { best = door.DistanceSquaredTo(p); closest = p; }
            }
        Lane(door, closest, .65f);
    }
    private void Decor(string name,int x,int y,int w,int h,bool solid=true) => Props.Add(new(new(x,y,w,h),name,solid));
    private void Lane(Vector2 a, Vector2 b, float width)
    {
        for (int y=0;y<_town.Size.Y;y++) for(int x=0;x<_town.Size.X;x++)
        {
            Vector2 p = new(x+_town.Position.X,y+_town.Position.Y);
            if(Geometry2D.GetClosestPointToSegment(p,a,b).DistanceTo(p)>width) continue;
            Ground[x,y] = Surface.Dirt; _road[x,y] = true;
        }
    }
}
