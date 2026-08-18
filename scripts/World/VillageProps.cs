using Godot;
using PixelMmo.Combat;
using Village = PixelMmo.Runtime.WorldLayout.Village;

namespace PixelMmo.Runtime;

/// <summary>
/// 마을 소품(집·우물)을 스프라이트 + 충돌체로 놓는다.
///
/// 왜 타일이 아니라 스프라이트인가: 집 한 채는 96x80 이라 16px 격자에 한 장으로
/// 안 들어가고, 지붕 모서리가 투명해서 타일로 깔면 뒤의 바닥이 비친다.
/// 스프라이트로 얹으면 바닥은 잔디 그대로 이어지고 집만 그 위에 선다.
///
/// 충돌은 예전 도형 타일이 하던 것과 똑같이 재현한다 — 6x5 중 아래 가운데
/// 두 칸(문간)만 비우고 나머지는 막는다. 그림만 바뀌고 다니는 길은 그대로다.
///
/// 에셋이 하나라도 없으면 아무 것도 놓지 않는다. TileWorld 가 그때는 예전처럼
/// 도형 블록을 찍으므로 게임은 그대로 돌아간다.
/// </summary>
public partial class VillageProps : Node2D
{
    private const string Root = "res://art/objects";

    private static readonly string[] HouseTextures = { "house_a", "house_b", "house_c" };
    private const string WellTexture = "well";

    /// <summary>에셋이 다 있는가. TileWorld 가 바닥을 어떻게 찍을지 정할 때 먼저 묻는다.</summary>
    public static bool AssetsPresent()
    {
        foreach (string name in HouseTextures)
        {
            if (!ResourceLoader.Exists($"{Root}/{name}.png"))
                return false;
        }
        return ResourceLoader.Exists($"{Root}/{WellTexture}.png");
    }

    /// <summary>실제로 막는 자리(월드 px). 그림의 불투명 영역에서 뽑는다.</summary>
    private readonly System.Collections.Generic.List<Rect2> _solid = new();

    public override void _Ready()
    {
        Name = "VillageProps";

        for (int i = 0; i < Village.Houses.Length; i++)
        {
            AddProp($"{Root}/{HouseTextures[i % HouseTextures.Length]}.png",
                Village.Houses[i], bottomAlign: true);
        }

        AddProp($"{Root}/{WellTexture}.png", Village.Well, bottomAlign: false);

        GD.Print($"[VillageProps] 소품 {_solid.Count}개 배치");
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

    /// <summary>
    /// 타일 사각형 한가운데에 스프라이트를 얹고, 그림에 맞춘 충돌체를 세운다.
    ///
    /// 충돌 범위를 캔버스가 아니라 **불투명 픽셀의 경계**에서 뽑는 이유:
    /// 생성된 스프라이트는 캔버스를 꽉 채우지 않는다. 캔버스 크기로 막으면
    /// 집 둘레에 아무 것도 없는데 걸리는 보이지 않는 벽이 생긴다.
    /// 보이는 것이 막는 것과 같아야 한다.
    /// </summary>
    private void AddProp(string path, Rect2I tiles, bool bottomAlign)
    {
        var texture = GD.Load<Texture2D>(path);
        if (texture == null)
        {
            GD.PushWarning($"[VillageProps] 못 읽었다: {path}");
            return;
        }

        Rect2 opaque = OpaqueBounds(texture);
        Vector2 half = texture.GetSize() * 0.5f;
        Vector2 center = CenterOf(tiles);

        // 집은 아래를 맞춘다 — 캔버스 여백 때문에 문이 문 앞 흙에서 떠 보이지 않도록.
        if (bottomAlign && opaque.Size.Y > 0f)
        {
            float footBottom = (tiles.Position.Y + tiles.Size.Y) * WorldLayout.TileSize;
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

        if (opaque.Size.X <= 0f || opaque.Size.Y <= 0f)
            return;

        // 텍스처 좌표 → 월드. 스프라이트가 중앙 정렬이므로 캔버스 중심을 뺀다.
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
    private static Rect2 OpaqueBounds(Texture2D texture)
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

    /// <summary>이보다 옅은 픽셀은 없는 셈 친다 — 외곽 반투명 픽셀까지 막지 않도록.</summary>
    private const float AlphaFloor = 0.25f;

    private static Vector2 CenterOf(Rect2I tiles)
        => new((tiles.Position.X + tiles.Size.X * 0.5f) * WorldLayout.TileSize,
               (tiles.Position.Y + tiles.Size.Y * 0.5f) * WorldLayout.TileSize);
}
