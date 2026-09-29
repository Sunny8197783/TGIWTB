using System.Collections.Generic;
using Godot;

namespace PixelMmo.World;

/// <summary>소품 종류별 그림 크기·흔들림·그림자·충돌.</summary>
public sealed class PropKind
{
    public string Id;
    /// <summary>그림이 아직 없을 때 만들 임시 그림 크기(텍셀). 실제 그림이 있으면 그 크기를 쓴다.</summary>
    public Vector2I Size;
    public Color Color;
    public Shape Placeholder;
    public float Sway;
    public bool Shadow = true;
    public bool Flip = true;
    public float Glow;
    /// <summary>수면/지면에 눕는다 (수련잎).</summary>
    public bool Flat;
    /// <summary>줄기 충돌 반지름(m). 0 이면 통과.</summary>
    public float Trunk;
    /// <summary>건물 바닥(폭×깊이, m). 0 이면 없음.</summary>
    public Vector2 Footprint;
    /// <summary>그림 아래 몇 텍셀이 땅속인가 — 뿌리/받침이 공중에 뜨지 않게.</summary>
    public int Sink = 2;

    public enum Shape { Tree, Pine, Bush, Flowers, Rock, House, Tower, Lamp, Flat, Box }

    /// <summary>
    /// 그림자는 스프라이트 판이 아니라 보이지 않는 입체가 드리운다. 판이 그림자를 드리우면
    /// 해가 낮을 때 판이 제 그림자를 받아 줄무늬가 생기고, 해가 옆에 있으면 그림자가 선 하나로 줄어든다.
    /// </summary>
    public enum Caster { None, Canopy, Cone, Blob, Box, Column }
    public Caster ShadowCaster = Caster.None;
}

public static class PropCatalog
{
    public static readonly Dictionary<string, PropKind> Kinds = new();

    static PropCatalog()
    {
        Tree("tree_sakura", 128, 160, new Color(0.96f, 0.66f, 0.78f));
        Tree("tree_oak", 128, 160, new Color(0.34f, 0.62f, 0.26f));
        Tree("tree_maple", 128, 160, new Color(0.9f, 0.42f, 0.16f));
        Add(new PropKind { Id = "tree_pine", Size = new(96, 160), Color = new Color(0.16f, 0.42f, 0.3f), Placeholder = PropKind.Shape.Pine, Sway = 0.03f, Trunk = 0.35f, Sink = 4, ShadowCaster = PropKind.Caster.Cone });

        Bush("bush", new Color(0.3f, 0.56f, 0.24f));
        Bush("bush_pink", new Color(0.94f, 0.62f, 0.74f));
        Bush("bush_orange", new Color(0.88f, 0.5f, 0.2f));
        Small("flowers_mix", new Color(0.95f, 0.6f, 0.7f));
        Small("flowers_yellow", new Color(0.98f, 0.86f, 0.3f));
        Small("flowers_white", new Color(0.97f, 0.97f, 0.92f));
        Small("fern", new Color(0.3f, 0.6f, 0.3f));
        Small("mushrooms", new Color(0.86f, 0.3f, 0.26f));
        // 작은 땅 장식 — 발밑에서 보이는 것들
        Small("flowers_blue", new Color(0.4f, 0.55f, 0.95f));
        Small("flowers_pink", new Color(0.95f, 0.55f, 0.75f));
        Small("lavender", new Color(0.6f, 0.45f, 0.85f));
        Small("dandelion", new Color(0.95f, 0.95f, 0.9f));
        Small("fern_small", new Color(0.3f, 0.6f, 0.3f));
        Small("grass_tuft", new Color(0.36f, 0.66f, 0.26f));
        Small("sapling", new Color(0.4f, 0.6f, 0.3f));
        Small("pebbles", new Color(0.6f, 0.6f, 0.62f));
        Small("stone", new Color(0.55f, 0.56f, 0.52f));
        Add(new PropKind { Id = "petals", Size = new(32, 24), Color = new Color(0.98f, 0.75f, 0.85f), Placeholder = PropKind.Shape.Flat, Flat = true, Shadow = false });
        Add(new PropKind { Id = "reeds", Size = new(32, 48), Color = new Color(0.5f, 0.62f, 0.3f), Placeholder = PropKind.Shape.Flowers, Sway = 0.05f, Shadow = false });
        Add(new PropKind { Id = "lily_pad", Size = new(32, 32), Color = new Color(0.3f, 0.6f, 0.3f), Placeholder = PropKind.Shape.Flat, Flat = true, Shadow = false });
        Add(new PropKind { Id = "rock", Size = new(48, 40), Color = new Color(0.55f, 0.53f, 0.5f), Placeholder = PropKind.Shape.Rock, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });

        Add(new PropKind { Id = "street_lamp", Size = new(32, 96), Color = new Color(0.25f, 0.25f, 0.3f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Trunk = 0.15f, Flip = false, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "stone_lantern", Size = new(32, 64), Color = new Color(0.6f, 0.6f, 0.58f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Trunk = 0.3f, Flip = false, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "bench", Size = new(64, 48), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false });
        Add(new PropKind { Id = "signpost", Size = new(48, 96), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Lamp, Flip = false, Trunk = 0.2f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "well", Size = new(80, 96), Color = new Color(0.6f, 0.6f, 0.62f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(2.2f, 1.8f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "crates", Size = new(48, 64), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(1.3f, 1.0f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "planter", Size = new(64, 48), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(2.0f, 0.9f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "fountain", Size = new(96, 80), Color = new Color(0.7f, 0.7f, 0.72f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(5.2f, 3.0f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "boat", Size = new(96, 64), Color = new Color(0.6f, 0.4f, 0.25f), Placeholder = PropKind.Shape.Box, Shadow = false, Flip = false });
        Add(new PropKind { Id = "dock", Size = new(96, 96), Color = new Color(0.6f, 0.4f, 0.25f), Placeholder = PropKind.Shape.Box, Flip = false, Glow = 1f });
        Add(new PropKind { Id = "dock_sea", Size = new(96, 96), Color = new Color(0.6f, 0.4f, 0.25f), Placeholder = PropKind.Shape.Box, Flip = false, Glow = 1f });
        Add(new PropKind { Id = "buoy", Size = new(64, 80), Color = new Color(0.9f, 0.3f, 0.3f), Placeholder = PropKind.Shape.Box, Shadow = false, Flip = false });
        Add(new PropKind { Id = "fish_crates", Size = new(80, 80), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(2.2f, 1.2f), ShadowCaster = PropKind.Caster.Box });

        House("house_red", new Color(0.8f, 0.3f, 0.26f));
        House("house_blue", new Color(0.3f, 0.45f, 0.75f));
        House("house_green", new Color(0.3f, 0.6f, 0.4f));
        House("house_yellow", new Color(0.9f, 0.75f, 0.3f));
        House("bakery", new Color(0.85f, 0.55f, 0.3f));
        House("flower_shop", new Color(0.9f, 0.5f, 0.65f));
        House("blacksmith", new Color(0.4f, 0.38f, 0.4f));
        House("general_store", new Color(0.5f, 0.35f, 0.6f));
        House("inn", new Color(0.7f, 0.3f, 0.3f), 256, 224, 8f);
        House("tavern", new Color(0.6f, 0.4f, 0.25f), 256, 224, 8f);

        Add(new PropKind { Id = "lighthouse", Size = new(96, 256), Color = new Color(0.95f, 0.95f, 0.92f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(3f, 3f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "windmill", Size = new(192, 256), Color = new Color(0.9f, 0.85f, 0.75f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(4f, 4f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "torii", Size = new(128, 128), Color = new Color(0.86f, 0.22f, 0.16f), Placeholder = PropKind.Shape.Box, Flip = false });
        Add(new PropKind { Id = "shrine", Size = new(192, 176), Color = new Color(0.7f, 0.3f, 0.2f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(5f, 4f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "pavilion", Size = new(160, 160), Color = new Color(0.75f, 0.3f, 0.25f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(4f, 4f) , ShadowCaster = PropKind.Caster.Box });
    }

    private static void Add(PropKind k) => Kinds[k.Id] = k;

    private static void Tree(string id, int w, int h, Color c)
        => Add(new PropKind { Id = id, Size = new(w, h), Color = c, Placeholder = PropKind.Shape.Tree, Sway = 0.045f, Trunk = 0.45f, Sink = 4, ShadowCaster = PropKind.Caster.Canopy });

    private static void Bush(string id, Color c)
        => Add(new PropKind { Id = id, Size = new(64, 48), Color = c, Placeholder = PropKind.Shape.Bush, Sway = 0.02f, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });

    private static void Small(string id, Color c)
        => Add(new PropKind { Id = id, Size = new(32, 24), Color = c, Placeholder = PropKind.Shape.Flowers, Sway = 0.03f, Shadow = false });

    private static void House(string id, Color roof, int w = 192, int h = 192, float width = 6f)
        => Add(new PropKind
        {
            Id = id, Size = new(w, h), Color = roof, Placeholder = PropKind.Shape.House,
            Flip = false, Footprint = new(width, 5f), Sink = 1, ShadowCaster = PropKind.Caster.Box,
        });
}
