using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// VillagePlan 의 소품(집·상점·나무·분수…)을 스프라이트 + 충돌체로 놓는다.
///
/// 왜 타일이 아니라 스프라이트인가: 건물 하나가 96x80~192x144 라 16px 격자에
/// 한 장으로 안 들어가고, 지붕 모서리가 투명해서 타일로 깔면 뒤 바닥이 비친다.
/// 스프라이트로 얹으면 바닥이 건물 밑으로 그대로 이어진다.
///
/// 충돌 범위는 배치 사각형이 아니라 **그림의 불투명 영역**에서 뽑는다.
/// 생성된 그림은 캔버스를 꽉 채우지 않아서(집 96x80 캔버스에 67x67 만 차는 식),
/// 캔버스대로 막으면 아무 것도 없는데 걸리는 벽이 생긴다.
/// 보이는 것이 막는 것과 같아야 한다.
///
/// 에셋이 하나라도 없으면 아무 것도 놓지 않는다. TileWorld 가 그때는 예전처럼
/// 도형 블록을 찍으므로 게임은 그대로 돌아간다.
/// </summary>
public partial class VillageProps : Node2D
{
    private const string Root = "res://art/objects32";

    /// <summary>이보다 옅은 픽셀은 없는 셈 친다 — 외곽 반투명 픽셀까지 막지 않도록.</summary>
    private const float AlphaFloor = 0.25f;

    /// <summary>실제로 막는 자리(월드 px).</summary>
    private readonly List<Rect2> _solid = new();

    /// <summary>
    /// 텍스처별 불투명 경계 캐시. 소품이 수백 개인데 같은 그림을 계속 쓰므로,
    /// 캐시하지 않으면 같은 픽셀을 수백 번 다시 훑어 로딩이 눈에 띄게 느려진다.
    /// </summary>
    private static readonly Dictionary<string, Rect2> BoundsCache = new();

    /// <summary>생성기가 쓰는 그림 이름 전부.</summary>
    private static readonly string[] RequiredTextures =
    {
        "gatehouse", "fountain", "tree_conifer", "bridge", "farm_plot", "wall_section",
        "training_dummy", "weapon_rack", "fence_section", "log_pile", "tree_stump",
        "training_hall", "barrels",
        "kit_s_shed", "kit_s02", "kit_s03", "kit_s04", "kit_s05", "kit_s06", "kit_s07", "kit_s08", "kit_m_cottage", "kit_m01", "kit_m02", "kit_m03", "kit_m04", "kit_m05", "kit_m06", "kit_m07", "kit_m08", "kit_m09", "kit_m10", "kit_m11", "kit_l_shop", "kit_l02", "kit_l03", "kit_l04", "kit_l05", "kit_l06", "kit_xl_guild",
    };

    /// <summary>쓸 그림이 전부 있는가.</summary>
    public static bool AssetsPresent()
    {
        foreach (string name in RequiredTextures)
        {
            if (!ResourceLoader.Exists($"{Root}/{name}.png"))
            {
                GD.Print($"[VillageProps] {name}.png 이 없다 — 도형 배치로 간다.");
                return false;
            }
        }
        return true;
    }

    private readonly List<PropPlacement> _plan;

    public VillageProps(List<PropPlacement> plan) => _plan = plan;

    public override void _Ready()
    {
        Name = "VillageProps";

        foreach (PropPlacement prop in _plan)
            Place(prop);

        GD.Print($"[VillageProps] 소품 {_plan.Count}개, 막는 것 {_solid.Count}개");
    }

    /// <summary>그 월드 좌표가 소품에 막혀 있는가. TileWorld.IsWalkable 이 묻는다.</summary>
    public bool IsBlocked(Vector2 world)
    {
        foreach (Rect2 box in _solid)
        {
            if (box.HasPoint(world))
                return true;
        }
        return false;
    }

    private void Place(PropPlacement prop)
    {
        var texture = GD.Load<Texture2D>($"{Root}/{prop.Texture}.png");
        if (texture == null)
        {
            GD.PushWarning($"[VillageProps] 못 읽었다: {prop.Texture}");
            return;
        }

        Rect2 opaque = OpaqueBounds(prop.Texture, texture);
        Vector2 half = texture.GetSize() * 0.5f;
        Vector2 center = CenterOf(prop.Tiles);

        // 건물·나무는 아래를 맞춘다 — 캔버스 여백 때문에 문이나 밑동이 떠 보이지 않도록.
        if (opaque.Size.Y > 0f)
        {
            float footBottom = (prop.Tiles.Position.Y + prop.Tiles.Size.Y) * WorldLayout.TileSize;
            center.Y = footBottom - (opaque.Position.Y + opaque.Size.Y) + half.Y;
        }

        AddChild(new Sprite2D
        {
            Texture = texture,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Centered = true,
            Position = center,
            ZIndex = 0,        // 바닥 위, 플레이어(1) 아래.
        });

        if (!prop.Solid || opaque.Size.X <= 0f || opaque.Size.Y <= 0f)
            return;

        var box = new Rect2(center + opaque.Position - half, opaque.Size);
        _solid.Add(box);

        var body = new StaticBody2D
        {
            Position = box.Position + box.Size * 0.5f,
            CollisionLayer = CollisionLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = box.Size } });
        AddChild(body);
    }

    /// <summary>불투명 픽셀을 감싸는 최소 사각형. 전부 투명이면 크기 0.</summary>
    private static Rect2 OpaqueBounds(string name, Texture2D texture)
    {
        if (BoundsCache.TryGetValue(name, out Rect2 cached))
            return cached;

        Rect2 result = Measure(texture);
        BoundsCache[name] = result;
        return result;
    }

    private static Rect2 Measure(Texture2D texture)
    {
        Image image = texture.GetImage();
        if (image == null)
            return new Rect2();

        int w = image.GetWidth(), h = image.GetHeight();
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (image.GetPixel(x, y).A <= AlphaFloor)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0
            ? new Rect2()
            : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Vector2 CenterOf(Rect2I tiles)
        => new((tiles.Position.X + tiles.Size.X * 0.5f) * WorldLayout.TileSize,
               (tiles.Position.Y + tiles.Size.Y * 0.5f) * WorldLayout.TileSize);
}
