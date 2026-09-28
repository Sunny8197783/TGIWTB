using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// TownGenerator 가 정한 소품(집·상점·나무·분수…)을 스프라이트 + 충돌체로 놓는다.
///
/// 왜 타일이 아니라 스프라이트인가: 건물 하나가 64x64~192x160 이라 32px 격자에
/// 한 장으로 안 들어가고, 지붕 모서리가 투명해서 타일로 깔면 뒤 바닥이 비친다.
/// 스프라이트로 얹으면 바닥이 건물 밑으로 그대로 이어진다.
///
/// ── 막는 자리 = 땅에 닿는 밑동만 ──────────────────────────────
/// 예전에는 그림 전체의 불투명 사각형으로 막았다. 그래서 지붕 꼭대기나 나무
/// 우듬지처럼 **공중에 있는 부분**까지 벽이 됐고, 아무 것도 없는데 걸렸다.
/// 이제는 그림 아래쪽 띠(밑동)만, 그것도 **기둥별 실루엣 그대로** 막는다.
/// 침엽수는 밑동 폭만 막히고, 집은 앞벽 바닥선 폭만 막힌다.
///
/// ── 뒤로 지나가면 가려진다 ────────────────────────────────────
/// 스프라이트의 정렬 기준을 '그림 한가운데'가 아니라 '땅에 닿는 선'으로 둔다.
/// GameWorld 가 Y 정렬이므로, 플레이어가 그 선보다 위(뒤)에 있으면 건물이
/// 플레이어를 덮고, 아래(앞)로 나오면 플레이어가 건물을 덮는다.
///
/// 에셋이 하나라도 없으면 아무 것도 놓지 않는다. TileWorld 가 그때는 예전처럼
/// 도형 블록을 찍으므로 게임은 그대로 돌아간다.
/// </summary>
public partial class VillageProps : Node2D
{
    private const string Root = "res://art/objects32";

    /// <summary>이보다 옅은 픽셀은 없는 셈 친다 — 외곽 반투명 픽셀과 그림자까지 막지 않도록.</summary>
    private const float AlphaFloor = 0.35f;

    /// <summary>
    /// 땅에 닿는 띠의 높이 = 불투명 높이 × 이 비율. 이 띠 안의 실루엣만 막는다.
    /// </summary>
    private const float BaseBandFraction = 0.25f;

    /// <summary>띠가 너무 얇으면 대시로 뚫고 지나간다. 너무 두꺼우면 다시 공중이 막힌다.</summary>
    private const int MinBandPx = 14;
    private const int MaxBandPx = 40;

    /// <summary>
    /// 예외적으로 밑동을 더 좁게 잡을 것. 나무는 우듬지가 넓고 밑동만 실물이라
    /// 비율로 자르면 아직도 가지에 걸린다.
    /// </summary>
    private static readonly Dictionary<string, int> BaseBandOverride = new()
    {
        ["tree_conifer"] = 14,
        ["tree_oak"] = 14,
        ["tree_willow"] = 14,
        ["orchard_tree"] = 12,
        ["tree_stump"] = 14,
    };

    /// <summary>
    /// 바닥에 깔리는 그림. Y 정렬에서 빼고 항상 맨 아래에 둔다 —
    /// 다리 한가운데 서 있는데 다리가 플레이어를 덮으면 안 된다.
    /// </summary>
    private static readonly HashSet<string> GroundDecals = new()
    {
        "bridge", "farm_plot", "veg_patch",
    };

    /// <summary>
    /// 플레이어 원점에서 발끝까지의 거리(px).
    ///
    /// 플레이어 스프라이트는 몸 한가운데가 원점이라, 원점 Y 로 정렬하면 발이 아직
    /// 건물 앞줄에 못 미쳤는데도 앞으로 나와 버린다. 소품 기준선을 같은 만큼 올려
    /// '발이 건물 바닥선을 넘는 순간' 앞뒤가 바뀌게 맞춘다.
    /// (캔버스 104px 중심 52 → 발끝 76, 차이 24px × 스케일 0.55 ≈ 13px)
    /// </summary>
    private const float PlayerFootOffset = 13f;

    /// <summary>실제로 막는 자리(월드 px). 하나의 소품이 여러 조각을 낼 수 있다.</summary>
    private readonly List<Rect2> _solid = new();

    /// <summary>
    /// IsBlocked 는 프레임마다(몬스터 이동·스폰 위치 찾기) 수백~수천 번 불린다.
    /// _solid 를 매번 통째로 훑으면(소품 1000개 이상) 그게 그대로 렉이 된다 —
    /// 128px 격자 버킷에 미리 나눠 담아, 점 하나가 걸리는 칸 하나만 본다.
    /// </summary>
    private const int BucketSize = 128;
    private readonly Dictionary<Vector2I, List<Rect2>> _grid = new();

    /// <summary>
    /// 텍스처별 밑동 조각 캐시. 소품이 수백 개인데 같은 그림을 계속 쓰므로,
    /// 캐시하지 않으면 같은 픽셀을 수백 번 다시 훑어 로딩이 눈에 띄게 느려진다.
    /// </summary>
    private static readonly Dictionary<string, Rect2[]> FootprintCache = new();

    /// <summary>텍스처별 불투명 경계 캐시 (그림을 바닥선에 맞출 때 쓴다).</summary>
    private static readonly Dictionary<string, Rect2> BoundsCache = new();

    /// <summary>생성기가 쓰는 그림 이름 전부.</summary>
    private static readonly string[] RequiredTextures =
    {
        // 길 위쪽 — 정면(문이 보인다)
        "kit_s_shed", "kit_s02", "kit_s03", "kit_s04", "kit_s05", "kit_s06", "kit_s07", "kit_s08",
        "kit_m_cottage", "kit_m01", "kit_m02", "kit_m03", "kit_m04", "kit_m05", "kit_m06",
        "kit_m07", "kit_m08", "kit_m09", "kit_m10", "kit_m11", "kit_m12", "kit_m13", "kit_m14",
        "kit_l_shop", "kit_l02", "kit_l03", "kit_l04", "kit_l05", "kit_l06", "kit_l07",

        // 길 아래·옆 — 지붕면(뒷모습·옆모습)
        "roof_s01", "roof_s02", "roof_s03",
        "roof_m01", "roof_m02", "roof_m03", "roof_m04",
        "roof_m05", "roof_m06", "roof_m07", "roof_m08",
        "roof_l01", "roof_l02", "roof_l03",

        // 이름 붙은 건물
        "kit_xl_guild", "town_hall", "chapel", "inn", "tavern", "bakery", "smithy",
        "stable", "windmill", "warehouse", "apothecary", "black_market",
        "training_hall", "gatehouse", "watchtower",

        // 광장·거리 시설
        "fountain", "statue", "well", "signpost", "notice_board",
        "stall_produce", "stall_cloth", "stall_weapon",

        // 살림살이·마당
        "barrels", "crates", "firewood", "ladder_bucket", "flower_pots", "lamp_post",
        "bench", "laundry_line", "veg_patch", "chickens", "hand_cart", "fence_section",
        "log_pile", "training_dummy", "weapon_rack",

        // 성 밖 — 숲 / 농지 / 목초지
        "tree_conifer", "tree_oak", "tree_willow", "farm_plot", "orchard_tree", "haystack", "boulder",
    };

    /// <summary>
    /// 쓸 그림이 전부 있는가. 없는 것은 **전부** 모아서 한 번에 알린다 —
    /// 하나씩 알리면 에셋을 채울 때마다 다시 돌려 봐야 한다.
    /// </summary>
    public static bool AssetsPresent()
    {
        var missing = new List<string>();
        foreach (string name in RequiredTextures)
        {
            if (!ResourceLoader.Exists($"{Root}/{name}.png"))
                missing.Add(name);
        }

        if (missing.Count == 0)
            return true;

        GD.Print($"[VillageProps] 그림 {missing.Count}개가 없다 — 도형 배치로 간다: "
            + string.Join(", ", missing));
        return false;
    }

    private readonly List<PropPlacement> _plan;

    public VillageProps(List<PropPlacement> plan) => _plan = plan;

    public override void _Ready()
    {
        Name = "VillageProps";

        // 자기 자식들을 부모(GameWorld)의 Y 정렬에 섞어 넣는다. 이게 있어야
        // 건물과 플레이어가 한 줄로 정렬돼 앞뒤가 갈린다.
        YSortEnabled = true;

        foreach (PropPlacement prop in _plan)
            Place(prop);

        GD.Print($"[VillageProps] 소품 {_plan.Count}개, 막는 조각 {_solid.Count}개");
    }

    /// <summary>그 월드 좌표가 소품에 막혀 있는가. TileWorld.IsWalkable 이 묻는다.</summary>
    public bool IsBlocked(Vector2 world)
    {
        var key = new Vector2I(Mathf.FloorToInt(world.X / BucketSize), Mathf.FloorToInt(world.Y / BucketSize));
        if (!_grid.TryGetValue(key, out var bucket))
            return false;

        foreach (Rect2 box in bucket)
        {
            if (box.HasPoint(world))
                return true;
        }
        return false;
    }

    /// <summary>사각형이 걸치는 모든 버킷에 등록한다 — 큰 건물은 여러 칸에 걸릴 수 있다.</summary>
    private void AddSolid(Rect2 box)
    {
        _solid.Add(box);

        int x0 = Mathf.FloorToInt(box.Position.X / BucketSize);
        int y0 = Mathf.FloorToInt(box.Position.Y / BucketSize);
        int x1 = Mathf.FloorToInt((box.Position.X + box.Size.X) / BucketSize);
        int y1 = Mathf.FloorToInt((box.Position.Y + box.Size.Y) / BucketSize);

        for (int gy = y0; gy <= y1; gy++)
        for (int gx = x0; gx <= x1; gx++)
        {
            var key = new Vector2I(gx, gy);
            if (!_grid.TryGetValue(key, out var bucket))
                _grid[key] = bucket = new List<Rect2>();
            bucket.Add(box);
        }
    }

    private void Place(PropPlacement prop)
    {
        Texture2D texture = ArtPalette.Prop($"{Root}/{prop.Texture}.png");
        if (texture == null)
        {
            GD.PushWarning($"[VillageProps] 못 읽었다: {prop.Texture}");
            return;
        }

        Rect2 opaque = OpaqueBounds(prop.Texture, texture);
        Vector2 size = texture.GetSize();

        // 그림에서 땅에 닿는 줄(불투명 영역의 아래 끝). 여기를 배치 사각형의
        // 아랫변에 맞춰야 문이나 밑동이 공중에 뜨지 않는다.
        float artBottom = opaque.Size.Y > 0f ? opaque.Position.Y + opaque.Size.Y : size.Y;

        float centerX = (prop.Tiles.Position.X + prop.Tiles.Size.X * 0.5f) * WorldLayout.TileSize;
        float groundY = (prop.Tiles.Position.Y + prop.Tiles.Size.Y) * WorldLayout.TileSize;

        bool decal = GroundDecals.Contains(prop.Texture);

        // Centered=false 이면 '위치 + 오프셋'이 그림의 좌상단이다. 위치를 정렬
        // 기준선에 두고 오프셋으로 그림을 제자리에 올린다 — 그래야 노드의 Y 가
        // 곧 정렬 키가 된다.
        float sortY = groundY - PlayerFootOffset;
        var sprite = new Sprite2D
        {
            Texture = texture,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Centered = false,
            Position = new Vector2(centerX, sortY),
            Offset = new Vector2(-size.X * 0.5f, -artBottom + PlayerFootOffset),

            // 좌우 반전은 FlipH 가 아니라 배율로 준다. FlipH 는 Centered=false 일 때
            // 그림을 원점 왼쪽으로 옮겨 버린다. 배율 -1 은 노드 위치를 축으로
            // 뒤집으므로 자리가 그대로다.
            Scale = prop.FlipH ? new Vector2(-1f, 1f) : Vector2.One,
            ZIndex = decal ? -1 : 0,
            Modulate = prop.Tint,
        };
        sprite.SetMeta("prop_kind", prop.Texture);
        sprite.SetMeta("solid", prop.Solid);
        AddChild(sprite);

        if (!prop.Solid)
            return;

        Rect2[] pieces = Footprint(prop.Texture, texture, opaque);
        if (pieces.Length == 0)
            return;

        // 그림의 좌상단이 월드 어디인가. 밑동 조각은 그림 좌표라 여기에 더한다.
        var origin = new Vector2(centerX - size.X * 0.5f, groundY - artBottom);

        var body = new StaticBody2D
        {
            Position = origin,
            CollisionLayer = CollisionLayers.World,
            CollisionMask = 0,
        };

        foreach (Rect2 raw in pieces)
        {
            // 그림을 뒤집었으면 막는 자리도 같이 뒤집힌다.
            Rect2 piece = prop.FlipH
                ? new Rect2(size.X - raw.Position.X - raw.Size.X, raw.Position.Y, raw.Size)
                : raw;

            body.AddChild(new CollisionShape2D
            {
                Position = piece.Position + piece.Size * 0.5f,
                Shape = new RectangleShape2D { Size = piece.Size },
            });
            AddSolid(new Rect2(origin + piece.Position, piece.Size));
        }

        AddChild(body);
    }

    // ── 밑동 뽑기 ────────────────────────────────────────────────

    /// <summary>
    /// 그림 아래쪽 띠에서 불투명한 세로줄을 찾아, 이어진 줄끼리 묶어 사각형으로 낸다.
    ///
    /// 결과는 그림 좌표(좌상단 0,0) 기준이다. 나무면 밑동 하나, 집이면 앞벽
    /// 바닥선 하나, 문 옆에 화분이 붙은 그림이면 두세 조각이 나온다.
    /// </summary>
    private static Rect2[] Footprint(string name, Texture2D texture, Rect2 opaque)
    {
        if (FootprintCache.TryGetValue(name, out Rect2[] cached))
            return cached;

        Rect2[] result = Measure(name, texture, opaque);
        FootprintCache[name] = result;
        return result;
    }

    private static Rect2[] Measure(string name, Texture2D texture, Rect2 opaque)
    {
        Image image = texture.GetImage();
        if (image == null || opaque.Size.X <= 0f || opaque.Size.Y <= 0f)
            return System.Array.Empty<Rect2>();

        int band = BaseBandOverride.TryGetValue(name, out int fixedBand)
            ? fixedBand
            : Mathf.Clamp(Mathf.RoundToInt(opaque.Size.Y * BaseBandFraction), MinBandPx, MaxBandPx);

        int bottom = (int)(opaque.Position.Y + opaque.Size.Y);      // 배타적
        int top = Mathf.Max((int)opaque.Position.Y, bottom - band);
        int x0 = (int)opaque.Position.X;
        int x1 = (int)(opaque.Position.X + opaque.Size.X);           // 배타적

        // 띠 안에 불투명 픽셀이 하나라도 있는 세로줄만 막는다.
        var solidColumn = new bool[x1 - x0];
        for (int x = x0; x < x1; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                if (image.GetPixel(x, y).A > AlphaFloor)
                {
                    solidColumn[x - x0] = true;
                    break;
                }
            }
        }

        // 이어진 줄을 하나의 사각형으로 묶는다. 충돌체가 폭 1px 로 수백 개가 되면
        // 물리 비용이 그대로 수백 배가 된다.
        var pieces = new List<Rect2>();
        int runStart = -1;
        for (int i = 0; i <= solidColumn.Length; i++)
        {
            bool solid = i < solidColumn.Length && solidColumn[i];
            if (solid && runStart < 0)
                runStart = i;
            else if (!solid && runStart >= 0)
            {
                pieces.Add(new Rect2(x0 + runStart, top, i - runStart, bottom - top));
                runStart = -1;
            }
        }

        return pieces.ToArray();
    }

    /// <summary>불투명 픽셀을 감싸는 최소 사각형. 전부 투명이면 크기 0.</summary>
    private static Rect2 OpaqueBounds(string name, Texture2D texture)
    {
        if (BoundsCache.TryGetValue(name, out Rect2 cached))
            return cached;

        Rect2 result = MeasureBounds(texture);
        BoundsCache[name] = result;
        return result;
    }

    private static Rect2 MeasureBounds(Texture2D texture)
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
}
