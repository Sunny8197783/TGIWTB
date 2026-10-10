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
        // 큰 지도 (WORLD.md): 지역마다 대표 수종
        Tree("tree_ginkgo", 112, 176, new Color(0.95f, 0.78f, 0.25f));
        Tree("tree_willow", 144, 176, new Color(0.45f, 0.65f, 0.3f));
        Tree("tree_poplar", 96, 192, new Color(0.4f, 0.62f, 0.28f));
        Tree("tree_bare", 128, 160, new Color(0.45f, 0.32f, 0.24f));
        Tree("tree_seapine", 160, 160, new Color(0.18f, 0.38f, 0.3f));
        Tree("tree_ancient", 192, 208, new Color(0.22f, 0.42f, 0.24f), 0.7f);
        Tree("tree_birch", 128, 176, new Color(0.5f, 0.7f, 0.35f), 0.3f);
        Tree("tree_birch_gold", 128, 176, new Color(0.95f, 0.75f, 0.25f), 0.3f);
        Tree("tree_apple", 128, 160, new Color(0.35f, 0.6f, 0.28f));
        Add(new PropKind { Id = "tree_cedar", Size = new(160, 208), Color = new Color(0.2f, 0.4f, 0.3f), Placeholder = PropKind.Shape.Pine, Sway = 0.02f, Trunk = 0.8f, Sink = 4, ShadowCaster = PropKind.Caster.Cone });
        Add(new PropKind { Id = "tree_snowfir", Size = new(112, 176), Color = new Color(0.85f, 0.9f, 0.95f), Placeholder = PropKind.Shape.Pine, Sway = 0.02f, Trunk = 0.35f, Sink = 4, ShadowCaster = PropKind.Caster.Cone });
        // 랜드마크 나무: 멀리서 보이는 길잡이
        Tree("tree_lone", 192, 224, new Color(0.3f, 0.58f, 0.24f), 0.8f);
        Tree("tree_sakura_giant", 256, 256, new Color(0.97f, 0.7f, 0.82f), 1.0f);
        Tree("tree_giant", 256, 256, new Color(0.22f, 0.45f, 0.24f), 1.4f);
        // 세계수: 줄기 + 수관 셋을 겹친 한 장 (tools/compose_world_tree.py)
        Tree("tree_world", 404, 424, new Color(0.2f, 0.45f, 0.26f), 1.8f);

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
        Bush("snow_bush", new Color(0.88f, 0.92f, 0.96f));
        // 꽃밭·숲·단풍 작은 소품 (map_object 격자)
        Small("tulip", new Color(0.95f, 0.3f, 0.35f));
        Small("daisy", new Color(0.97f, 0.97f, 0.92f));
        Small("hydrangea", new Color(0.65f, 0.45f, 0.85f));
        Small("sunflower", new Color(0.98f, 0.82f, 0.2f));
        Small("rose", new Color(0.95f, 0.5f, 0.65f));
        Small("twig_pot", new Color(0.8f, 0.3f, 0.2f));
        Small("maple_sapling", new Color(0.85f, 0.3f, 0.15f));
        Small("fiddlehead", new Color(0.35f, 0.6f, 0.3f));
        Small("grass_tall", new Color(0.4f, 0.65f, 0.3f));
        Small("berries", new Color(0.85f, 0.15f, 0.15f));
        Bush("bush_flower", new Color(0.4f, 0.6f, 0.3f));
        Bush("shrub", new Color(0.3f, 0.55f, 0.25f));
        Small("wildflower_a", new Color(0.9f, 0.4f, 0.4f));
        Small("wildflower_b", new Color(0.6f, 0.5f, 0.9f));
        Add(new PropKind { Id = "rope", Size = new(40, 30), Color = new Color(0.7f, 0.6f, 0.4f), Placeholder = PropKind.Shape.Box, Shadow = false });
        Add(new PropKind { Id = "oars", Size = new(30, 50), Color = new Color(0.6f, 0.45f, 0.3f), Placeholder = PropKind.Shape.Box, Shadow = false });
        Add(new PropKind { Id = "seagull", Size = new(36, 36), Color = new Color(0.95f, 0.95f, 0.95f), Placeholder = PropKind.Shape.Box, Shadow = false });
        Add(new PropKind { Id = "lobster_trap", Size = new(44, 44), Color = new Color(0.6f, 0.45f, 0.3f), Placeholder = PropKind.Shape.Box, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "pumpkins", Size = new(48, 40), Color = new Color(0.95f, 0.55f, 0.15f), Placeholder = PropKind.Shape.Rock, Shadow = false });
        Add(new PropKind { Id = "persimmon_basket", Size = new(46, 48), Color = new Color(0.9f, 0.45f, 0.15f), Placeholder = PropKind.Shape.Box, Shadow = false, Flip = false });
        Add(new PropKind { Id = "watering_can", Size = new(50, 42), Color = new Color(0.7f, 0.72f, 0.75f), Placeholder = PropKind.Shape.Box, Shadow = false });
        Add(new PropKind { Id = "wheelbarrow", Size = new(51, 46), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Box, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "scarecrow", Size = new(35, 50), Color = new Color(0.8f, 0.6f, 0.3f), Placeholder = PropKind.Shape.Lamp, Trunk = 0.25f, Sway = 0.01f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "birdhouse", Size = new(42, 53), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Lamp, Flip = false, Trunk = 0.2f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "jlantern", Size = new(26, 38), Color = new Color(0.6f, 0.6f, 0.58f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Flip = false, Trunk = 0.2f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "paper_lantern", Size = new(15, 47), Color = new Color(0.9f, 0.2f, 0.15f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Flip = false, Trunk = 0.12f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "log", Size = new(53, 44), Color = new Color(0.5f, 0.35f, 0.22f), Placeholder = PropKind.Shape.Box, Trunk = 0.6f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "stump", Size = new(52, 48), Color = new Color(0.5f, 0.35f, 0.22f), Placeholder = PropKind.Shape.Box, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "mushroom_big", Size = new(50, 50), Color = new Color(0.85f, 0.3f, 0.25f), Placeholder = PropKind.Shape.Bush, Trunk = 0.4f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "mushroom_glow", Size = new(46, 47), Color = new Color(0.4f, 0.7f, 0.95f), Placeholder = PropKind.Shape.Flowers, Shadow = false, Glow = 0.8f });
        // 겨울 소품 (작은 것들) — 눈꽃 마을 마당·서리 고원
        Add(new PropKind { Id = "snowman", Size = new(45, 53), Color = new Color(0.95f, 0.95f, 1f), Placeholder = PropKind.Shape.Bush, Trunk = 0.4f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "ice", Size = new(47, 46), Color = new Color(0.7f, 0.85f, 0.95f), Placeholder = PropKind.Shape.Rock, Glow = 0f });
        Add(new PropKind { Id = "sled", Size = new(53, 47), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Box });
        Add(new PropKind { Id = "snow_fence", Size = new(48, 52), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Box, Footprint = new(1.4f, 0.3f) });
        Add(new PropKind { Id = "snow_stump", Size = new(50, 51), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Box, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "snow_sapling", Size = new(42, 55), Color = new Color(0.3f, 0.5f, 0.35f), Placeholder = PropKind.Shape.Pine, Sway = 0.02f, Trunk = 0.25f, ShadowCaster = PropKind.Caster.Cone });
        Add(new PropKind { Id = "snow_sign", Size = new(29, 53), Color = new Color(0.55f, 0.38f, 0.25f), Placeholder = PropKind.Shape.Lamp, Flip = false, Trunk = 0.2f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "snow_rock", Size = new(48, 40), Color = new Color(0.8f, 0.84f, 0.9f), Placeholder = PropKind.Shape.Rock, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "standing_stone", Size = new(40, 80), Color = new Color(0.55f, 0.56f, 0.6f), Placeholder = PropKind.Shape.Tower, Flip = true, Trunk = 0.45f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "ruin_pillar", Size = new(40, 100), Color = new Color(0.6f, 0.6f, 0.65f), Placeholder = PropKind.Shape.Tower, Flip = true, Trunk = 0.4f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "ruin_arch", Size = new(110, 100), Color = new Color(0.55f, 0.52f, 0.6f), Placeholder = PropKind.Shape.Box, Flip = true, Footprint = new(3.2f, 0.8f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "ruin_wall", Size = new(100, 60), Color = new Color(0.55f, 0.55f, 0.58f), Placeholder = PropKind.Shape.Box, Flip = true, Footprint = new(2.8f, 0.8f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "campfire", Size = new(48, 48), Color = new Color(0.95f, 0.55f, 0.2f), Placeholder = PropKind.Shape.Bush, Glow = 1f, Shadow = false, Flip = false });
        Add(new PropKind { Id = "log_seat", Size = new(64, 32), Color = new Color(0.5f, 0.35f, 0.22f), Placeholder = PropKind.Shape.Box, Flip = true });
        Add(new PropKind { Id = "rock", Size = new(48, 40), Color = new Color(0.55f, 0.53f, 0.5f), Placeholder = PropKind.Shape.Rock, Trunk = 0.5f, ShadowCaster = PropKind.Caster.Blob });

        Add(new PropKind { Id = "street_lamp", Size = new(32, 96), Color = new Color(0.25f, 0.25f, 0.3f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Trunk = 0.15f, Flip = false, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "stone_lantern", Size = new(32, 64), Color = new Color(0.6f, 0.6f, 0.58f), Placeholder = PropKind.Shape.Lamp, Glow = 1f, Trunk = 0.3f, Flip = false, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "bench", Size = new(64, 48), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false });
        Add(new PropKind { Id = "signpost", Size = new(48, 96), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Lamp, Flip = false, Trunk = 0.2f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "well", Size = new(80, 96), Color = new Color(0.6f, 0.6f, 0.62f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(2.2f, 1.8f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "crates", Size = new(48, 64), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(1.3f, 1.0f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "barrel", Size = new(56, 72), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = false, Trunk = 0.7f, ShadowCaster = PropKind.Caster.Column });
        Add(new PropKind { Id = "market_stall", Size = new(92, 92), Color = new Color(0.9f, 0.3f, 0.3f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(2.6f, 1.4f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "fence", Size = new(48, 48), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Footprint = new(1.5f, 0.3f) });
        Add(new PropKind { Id = "hay_bale", Size = new(64, 64), Color = new Color(0.9f, 0.75f, 0.35f), Placeholder = PropKind.Shape.Bush, Flip = true, Trunk = 0.9f, ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "cart", Size = new(96, 56), Color = new Color(0.6f, 0.42f, 0.26f), Placeholder = PropKind.Shape.Box, Flip = true, Footprint = new(2.6f, 1.2f), ShadowCaster = PropKind.Caster.Box });
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

        // 지역 마을 (그림이 오기 전엔 임시 그림)
        House("jhouse", new Color(0.35f, 0.33f, 0.38f));
        House("teahouse", new Color(0.45f, 0.3f, 0.25f), 224, 192, 7f);
        House("chalet", new Color(0.9f, 0.92f, 0.96f));
        House("lodge", new Color(0.86f, 0.9f, 0.95f), 256, 224, 8f);
        House("cottage", new Color(0.75f, 0.45f, 0.6f));
        House("greenhouse", new Color(0.7f, 0.9f, 0.85f), 224, 176, 7f);
        House("stable", new Color(0.6f, 0.4f, 0.25f), 192, 160, 6f);
        House("barn", new Color(0.75f, 0.2f, 0.18f), 224, 224, 7f);
        House("house_stone", new Color(0.4f, 0.4f, 0.45f));
        House("house_fisher", new Color(0.35f, 0.5f, 0.7f));
        House("warehouse", new Color(0.55f, 0.4f, 0.28f), 224, 192, 7f);
        House("chapel", new Color(0.8f, 0.3f, 0.25f), 192, 224, 6f);
        House("shepherd_hut", new Color(0.6f, 0.55f, 0.45f));
        House("stump_house", new Color(0.5f, 0.35f, 0.22f), 192, 224, 5f);
        House("ice_hut", new Color(0.75f, 0.25f, 0.2f), 192, 192, 4f);
        House("boathouse", new Color(0.4f, 0.55f, 0.8f), 224, 192, 7f);
        House("house_tower", new Color(0.3f, 0.6f, 0.4f));
        Add(new PropKind { Id = "moon_deck", Size = new(224, 208), Color = new Color(0.7f, 0.2f, 0.15f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(6f, 4f), Glow = 0.8f, ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "pass_gate", Size = new(224, 224), Color = new Color(0.6f, 0.6f, 0.65f), Placeholder = PropKind.Shape.Box, Flip = false, Trunk = 0.4f });
        Add(new PropKind { Id = "fairy_ring", Size = new(160, 160), Color = new Color(0.8f, 0.3f, 0.3f), Placeholder = PropKind.Shape.Flat, Flat = true, Shadow = false });
        Add(new PropKind { Id = "goddess", Size = new(192, 208), Color = new Color(0.9f, 0.9f, 0.88f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(2.6f, 2.2f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "gazebo", Size = new(192, 192), Color = new Color(0.9f, 0.9f, 0.92f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(4.5f, 4f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "rose_arch", Size = new(192, 192), Color = new Color(0.9f, 0.4f, 0.5f), Placeholder = PropKind.Shape.Box, Flip = false, Sway = 0.01f, Trunk = 0.2f });
        Add(new PropKind { Id = "boulder", Size = new(128, 160), Color = new Color(0.5f, 0.55f, 0.5f), Placeholder = PropKind.Shape.Rock, Footprint = new(3f, 2f), ShadowCaster = PropKind.Caster.Blob });
        Add(new PropKind { Id = "clocktower", Size = new(192, 256), Color = new Color(0.4f, 0.5f, 0.75f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(6f, 5f), Sink = 1, ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "observatory", Size = new(192, 224), Color = new Color(0.75f, 0.78f, 0.82f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(5f, 5f), Sink = 1, Glow = 0.6f, ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "ice_shrine", Size = new(192, 192), Color = new Color(0.7f, 0.85f, 0.95f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(5f, 4f), Sink = 1, ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "hokora", Size = new(128, 128), Color = new Color(0.4f, 0.35f, 0.38f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(3f, 2f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "temple", Size = new(224, 208), Color = new Color(0.6f, 0.25f, 0.2f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(7f, 5f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "pagoda", Size = new(128, 256), Color = new Color(0.65f, 0.25f, 0.2f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(4f, 4f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "castle_ruin", Size = new(256, 224), Color = new Color(0.55f, 0.55f, 0.58f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(8f, 5f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "watchtower_ruin", Size = new(128, 224), Color = new Color(0.6f, 0.58f, 0.55f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(3.5f, 3.5f), ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "onsen", Size = new(192, 128), Color = new Color(0.55f, 0.6f, 0.65f), Placeholder = PropKind.Shape.Box, Flip = false, Footprint = new(5f, 3f) });
        Add(new PropKind { Id = "tent", Size = new(96, 80), Color = new Color(0.85f, 0.75f, 0.55f), Placeholder = PropKind.Shape.House, Flip = true, Footprint = new(2.6f, 2f), ShadowCaster = PropKind.Caster.Box });

        Add(new PropKind { Id = "lighthouse", Size = new(96, 256), Color = new Color(0.95f, 0.95f, 0.92f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(3f, 3f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "windmill", Size = new(192, 256), Color = new Color(0.9f, 0.85f, 0.75f), Placeholder = PropKind.Shape.Tower, Flip = false, Footprint = new(4f, 4f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "torii", Size = new(128, 128), Color = new Color(0.86f, 0.22f, 0.16f), Placeholder = PropKind.Shape.Box, Flip = false, Trunk = 0.3f });
        Add(new PropKind { Id = "shrine", Size = new(192, 176), Color = new Color(0.7f, 0.3f, 0.2f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(5f, 4f) , ShadowCaster = PropKind.Caster.Box });
        Add(new PropKind { Id = "pavilion", Size = new(160, 160), Color = new Color(0.75f, 0.3f, 0.25f), Placeholder = PropKind.Shape.House, Flip = false, Footprint = new(4f, 4f) , ShadowCaster = PropKind.Caster.Box });
    }

    private static void Add(PropKind k) => Kinds[k.Id] = k;

    private static void Tree(string id, int w, int h, Color c, float trunk = 0.45f)
        => Add(new PropKind { Id = id, Size = new(w, h), Color = c, Placeholder = PropKind.Shape.Tree, Sway = 0.045f, Trunk = trunk, Sink = 4, ShadowCaster = PropKind.Caster.Canopy });

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
