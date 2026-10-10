using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 소품을 128m 덩어리 × 종류마다 MultiMesh 하나로 그린다 (1km 지도의 소품 2만여 개).
/// 한 종류의 그림 여러 장은 불러올 때 가로로 이어 붙인 한 장(아틀라스)으로 만들어
/// 인스턴스 색에 제 칸(u0, u1, v0)을 담는다 — 그림이 늘어도 그리는 횟수는 그대로.
/// 그림이 아직 없으면 같은 크기의 임시 그림을 만들어 끼운다(배치 검토는 그림 없이도 된다).
/// </summary>
public static class PropBuilder
{
    private const string ArtDir = "res://art/env/";
    private const int Chunk = 128;
    private const int AtlasPad = 4;
    /// <summary>작은 장식·덤불은 이 거리(덩어리 가운데까지, m) 밖에서 안 그린다. 덩어리 반 대각선(90m) + 볼 거리.</summary>
    private const float SmallRange = 150f, BushRange = 220f, TreeRange = 450f;
    /// <summary>그림자 입체: 그림자 거리(55m) 밖은 필요 없다.</summary>
    private const float CasterRange = 110f;
    /// <summary>그림 아래쪽(땅에 닿은 곳)을 이만큼 어둡게 — 판이 땅에 붙어 보이게</summary>
    private const float ContactAo = 0.28f;

    private sealed class Atlas
    {
        public Texture2D Texture;
        public Texture2D Normal;
        public Rect2[] Cells;      // 텍셀 단위
        public Vector2I[] Sizes;
        public float Width, Height;
    }

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Props" };
        var shader = GD.Load<Shader>("res://shaders/sprite.gdshader");

        // (덩어리, 종류) → 배치 목록
        var groups = new Dictionary<(int, int, string), List<PropDef>>();
        var atlases = new Dictionary<string, Atlas>();
        int skipped = 0;
        foreach (var p in world.Props)
        {
            if (!PropCatalog.Kinds.TryGetValue(p.Type, out var kind))
            {
                skipped++;
                continue;
            }
            if (!atlases.ContainsKey(p.Type))
                atlases[p.Type] = BuildAtlas(kind);
            var key = ((int)(p.X / Chunk), (int)(p.Z / Chunk), p.Type);
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = new List<PropDef>();
            g.Add(p);
        }

        var quad = new QuadMesh { Size = Vector2.One, CenterOffset = new Vector3(0f, 0.5f, 0f) };
        var flatQuad = new QuadMesh { Size = Vector2.One, Orientation = PlaneMesh.OrientationEnum.Y };
        var materials = new Dictionary<string, ShaderMaterial>();
        var bodies = new Dictionary<(int, int), StaticBody3D>();
        var rng = new RandomNumberGenerator { Seed = 1234 };
        float sinPitch = Mathf.Sin(Mathf.DegToRad(Px.PitchDeg));

        foreach (var ((cx, cz, type), items) in groups)
        {
            var kind = PropCatalog.Kinds[type];
            var atlas = atlases[type];
            if (!materials.TryGetValue(type, out var mat))
            {
                materials[type] = mat = new ShaderMaterial { Shader = shader };
                mat.SetShaderParameter(Uniform.AlbedoTex, atlas.Texture);
                if (atlas.Normal != null)
                {
                    mat.SetShaderParameter(Uniform.NormalTex, atlas.Normal);
                    mat.SetShaderParameter("has_normal", true);
                }
                if (!kind.Flat && (kind.Shadow || kind.Trunk > 0f))
                    mat.SetShaderParameter("contact_ao", ContactAo);
                mat.SetShaderParameter("atlas", true);
                mat.SetShaderParameter("sway", kind.Sway);
                mat.SetShaderParameter("glow_at_night", kind.Glow);
            }

            // 인스턴스 하나 = 변환 12 + 색 4 + 사용자 4. 한 번에 통째로 넘긴다 (하나씩 넘기면 3만 개에 1초가 넘었다)
            var buf = new float[items.Count * 20];
            var casters = new List<Transform3D>();
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                int v = Math.Abs(p.Variant) % atlas.Cells.Length;
                var size = atlas.Sizes[v];
                float wM = size.X / Px.PerMeter;
                float hM = size.Y / Px.PerMeter * Px.UprightStretch;
                bool flip = kind.Flip && rng.Randf() < 0.5f;
                float z = float.IsNaN(p.FrontZ) ? p.Z : p.FrontZ;
                Transform3D xf;
                if (kind.Flat)
                {
                    float wy = world.WaterAt(p.X, p.Z);
                    float y = (wy > WorldData.NoWater + 1 ? wy : world.HeightAt(p.X, z)) + 0.03f;
                    // 돌리지 않는다 (픽셀 격자가 깨진다). 내려다보면 세로가 sin(피치)만큼 줄어드니 그만큼 늘려 1:1 로.
                    var basis = Basis.Identity.Scaled(new Vector3(wM * (rng.Randf() < 0.5f ? -1f : 1f), 1f, size.Y / Px.PerMeter / sinPitch));
                    xf = new Transform3D(basis, new Vector3(p.X, y, p.Z));
                }
                else
                {
                    float y = world.HeightAt(p.X, z) - kind.Sink / Px.PerMeter * Px.UprightStretch;
                    xf = new Transform3D(Basis.Identity.Scaled(new Vector3(flip ? -wM : wM, hM, 1f)), new Vector3(p.X, y, z));
                    if (kind.ShadowCaster != PropKind.Caster.None && kind.Shadow)
                        AddCasters(casters, kind, new Vector2(wM, size.Y / Px.PerMeter * 0.8f), new Vector3(p.X, world.HeightAt(p.X, z), z));
                }
                var cell = atlas.Cells[v];
                int o = i * 20;
                var b = xf.Basis;
                buf[o] = b.X.X; buf[o + 1] = b.Y.X; buf[o + 2] = b.Z.X; buf[o + 3] = xf.Origin.X;
                buf[o + 4] = b.X.Y; buf[o + 5] = b.Y.Y; buf[o + 6] = b.Z.Y; buf[o + 7] = xf.Origin.Y;
                buf[o + 8] = b.X.Z; buf[o + 9] = b.Y.Z; buf[o + 10] = b.Z.Z; buf[o + 11] = xf.Origin.Z;
                // 색 = 아틀라스 칸 (u0, u1, 위 v, 0)
                buf[o + 12] = cell.Position.X / atlas.Width;
                buf[o + 13] = cell.End.X / atlas.Width;
                buf[o + 14] = (atlas.Height - size.Y) / atlas.Height;
                buf[o + 15] = 0f;
                // 사용자 = (흔들림 위상, 흔들림 배율, 0, 0)
                buf[o + 16] = rng.Randf();
                buf[o + 17] = 0.7f + rng.Randf() * 0.6f;
                buf[o + 18] = 0f;
                buf[o + 19] = 0f;
                AddCollision(bodies, root, kind, p, world);
            }

            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = kind.Flat ? flatQuad : quad,
                InstanceCount = items.Count,
                Buffer = buf,
            };
            bool small = !kind.Shadow && kind.Footprint == Vector2.Zero && kind.Trunk == 0f;
            bool bush = kind.Placeholder == PropKind.Shape.Bush || kind.Placeholder == PropKind.Shape.Rock;
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"{type}_{cx}_{cz}",
                Multimesh = mm,
                MaterialOverride = mat,
                // 판은 그림자를 드리우지 않는다 — 아래 입체가 대신한다
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // 건물·랜드마크(바닥 있음)는 끝까지, 나무는 450m 까지 (전망에서 그리기 호출과 겹쳐 그리기를 줄인다)
                VisibilityRangeEnd = small ? SmallRange : bush ? BushRange : kind.Footprint != Vector2.Zero ? 0f : TreeRange,
            });
            if (casters.Count > 0)
                root.AddChild(CasterInstance(kind, casters, $"{type}_shadow_{cx}_{cz}"));
        }

        BakeAo(world, atlases);
        if (skipped > 0)
            GD.Print($"[Props] 목록에 없는 종류 {skipped}개는 건너뜀 (다리는 따로 만든다)");
        GD.Print($"[Props] {world.Props.Count - skipped}개, 묶음 {groups.Count}개, 그림 {atlases.Count}종");
        return root;
    }

    private static readonly Mesh UnitSphere = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 };
    private static readonly Mesh UnitCylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh UnitCone = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh UnitBox = new BoxMesh { Size = Vector3.One };

    /// <summary>
    /// 그림자만 드리우는 입체. 그림 크기에서 대략의 실제 부피를 잡고, 판보다 뒤(북쪽)에 둬서
    /// 판이 제 입체의 그림자를 뒤집어쓰지 않게 한다. 한 소품에 입체 둘(수관 + 줄기)까지 — 짝수 칸이 수관.
    /// </summary>
    private static void AddCasters(List<Transform3D> list, PropKind kind, Vector2 size, Vector3 basePos)
    {
        float w = size.X, h = size.Y;
        switch (kind.ShadowCaster)
        {
            case PropKind.Caster.Canopy:
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(w * 0.78f, h * 0.55f, w * 0.7f)), basePos + new Vector3(0f, h * 0.64f, -w * 0.4f)));
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(0.35f, h * 0.5f, 0.35f)), basePos + new Vector3(0f, h * 0.25f, -w * 0.3f)));
                break;
            case PropKind.Caster.Cone:
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(w * 0.7f, h * 0.9f, w * 0.7f)), basePos + new Vector3(0f, h * 0.5f, -w * 0.4f)));
                break;
            case PropKind.Caster.Blob:
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(w * 0.8f, h * 0.9f, w * 0.6f)), basePos + new Vector3(0f, h * 0.4f, -w * 0.35f)));
                break;
            case PropKind.Caster.Column:
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(0.3f, h, 0.3f)), basePos + new Vector3(0f, h * 0.5f, -0.25f)));
                break;
            case PropKind.Caster.Box:
                var fp = kind.Footprint == Vector2.Zero ? new Vector2(w * 0.8f, w * 0.6f) : kind.Footprint;
                list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(fp.X, h * 0.85f, fp.Y)), basePos + new Vector3(0f, h * 0.42f, -0.3f - fp.Y * 0.5f)));
                break;
        }
    }

    private static Node3D CasterInstance(PropKind kind, List<Transform3D> xfs, string name)
    {
        // 수관 소품은 [수관, 줄기] 짝으로 들어 있다 — 메시를 둘로 나눠 그린다
        var node = new Node3D { Name = name };
        void Add(Mesh mesh, Func<int, bool> pick)
        {
            var sel = new List<Transform3D>();
            for (int i = 0; i < xfs.Count; i++)
                if (pick(i))
                    sel.Add(xfs[i]);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = sel.Count };
            for (int i = 0; i < sel.Count; i++)
                mm.SetInstanceTransform(i, sel[i]);
            node.AddChild(new MultiMeshInstance3D
            {
                Multimesh = mm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
                VisibilityRangeEnd = CasterRange,
            });
        }
        switch (kind.ShadowCaster)
        {
            case PropKind.Caster.Canopy:
                Add(UnitSphere, i => i % 2 == 0);
                Add(UnitCylinder, i => i % 2 == 1);
                break;
            case PropKind.Caster.Cone: Add(UnitCone, _ => true); break;
            case PropKind.Caster.Blob: Add(UnitSphere, _ => true); break;
            case PropKind.Caster.Column: Add(UnitCylinder, _ => true); break;
            case PropKind.Caster.Box: Add(UnitBox, _ => true); break;
        }
        return node;
    }

    /// <summary>땅 그늘 지도 해상도 (1m 당 텍셀)</summary>
    private const int AoRes = 2;

    /// <summary>
    /// 땅 그늘 지도: 나무·바위·집 밑동에 짙은 타원, 큰 나무 둘레에 옅은 수관 그늘을 굽는다 (지형·풀·캐릭터가 읽는다).
    /// 실제 그림자(55m 안, 해 방향)와 달리 늘 그 자리에 있어서 판이 땅에 '앉아' 보이고, 멀리서도 숲 바닥이 짙다.
    /// </summary>
    private static void BakeAo(WorldData world, Dictionary<string, Atlas> atlases)
    {
        int w = world.Width * AoRes, h = world.Height * AoRes;
        var ao = new byte[w * h];
        void Ellipse(float cx, float cz, float rx, float rz, float dark)
        {
            int x0 = Math.Max(0, (int)((cx - rx) * AoRes)), x1 = Math.Min(w - 1, (int)((cx + rx) * AoRes) + 1);
            int z0 = Math.Max(0, (int)((cz - rz) * AoRes)), z1 = Math.Min(h - 1, (int)((cz + rz) * AoRes) + 1);
            for (int z = z0; z <= z1; z++)
            {
                float dz = ((z + 0.5f) / AoRes - cz) / rz;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = ((x + 0.5f) / AoRes - cx) / rx;
                    float d = dx * dx + dz * dz;
                    if (d >= 1f)
                        continue;
                    byte v = (byte)(dark * (1f - d) * 255f);
                    int i = z * w + x;
                    if (v > ao[i])
                        ao[i] = v;
                }
            }
        }
        foreach (var p in world.Props)
        {
            if (!PropCatalog.Kinds.TryGetValue(p.Type, out var kind) || kind.Flat)
                continue;
            var atlas = atlases[p.Type];
            float wM = atlas.Sizes[Math.Abs(p.Variant) % atlas.Sizes.Length].X / Px.PerMeter;
            float z = float.IsNaN(p.FrontZ) ? p.Z : p.FrontZ;
            if (kind.Footprint != Vector2.Zero)
            {
                // 건물: 바닥 전체에 옅게, 앞 벽 밑동에 짙게
                Ellipse(p.X, z - kind.Footprint.Y * 0.5f, kind.Footprint.X * 0.62f, kind.Footprint.Y * 0.75f, 0.35f);
                Ellipse(p.X, z, kind.Footprint.X * 0.55f, 0.7f, 0.45f);
            }
            else if (kind.Trunk > 0f || kind.Shadow)
            {
                float rx = Math.Max(0.45f, wM * 0.42f);
                Ellipse(p.X, z - rx * 0.15f, rx, rx * 0.5f, kind.Trunk > 0f ? 0.5f : 0.35f);
                if (kind.ShadowCaster == PropKind.Caster.Canopy || kind.ShadowCaster == PropKind.Caster.Cone)
                    Ellipse(p.X, z - wM * 0.3f, wM * 0.85f, wM * 0.55f, 0.16f);
            }
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.R8, ao);
        RenderingServer.GlobalShaderParameterSet("ao_map", ImageTexture.CreateFromImage(img));
    }

    private static readonly Dictionary<string, Shape3D> Shapes = new();

    /// <summary>줄기·건물 바닥 충돌. 노드 없이 덩어리마다 몸 하나에 모양만 붙인다 (모양은 종류마다 하나를 같이 쓴다).</summary>
    private static void AddCollision(Dictionary<(int, int), StaticBody3D> bodies, Node3D root, PropKind kind, PropDef p, WorldData world)
    {
        Shape3D shape;
        Vector3 pos;
        if (kind.Footprint != Vector2.Zero)
        {
            float front = float.IsNaN(p.FrontZ) ? p.Z + kind.Footprint.Y * 0.5f : p.FrontZ;
            pos = new Vector3(p.X, world.HeightAt(p.X, front) + 2f, front - kind.Footprint.Y * 0.5f);
            if (!Shapes.TryGetValue(kind.Id, out shape))
                Shapes[kind.Id] = shape = new BoxShape3D { Size = new Vector3(kind.Footprint.X, 4f, kind.Footprint.Y) };
        }
        else if (kind.Trunk > 0f)
        {
            pos = new Vector3(p.X, world.HeightAt(p.X, p.Z) + 1.5f, p.Z);
            if (!Shapes.TryGetValue(kind.Id, out shape))
                Shapes[kind.Id] = shape = new CylinderShape3D { Radius = kind.Trunk, Height = 3f };
        }
        else
            return;
        var key = ((int)(p.X / Chunk), (int)(p.Z / Chunk));
        if (!bodies.TryGetValue(key, out var body))
        {
            bodies[key] = body = new StaticBody3D { Name = $"PropCollision_{key.Item1}_{key.Item2}" };
            root.AddChild(body);
        }
        uint owner = body.CreateShapeOwner(body);
        body.ShapeOwnerAddShape(owner, shape);
        body.ShapeOwnerSetTransform(owner, new Transform3D(Basis.Identity, pos));
    }

    /// <summary>art/env/{종류}_{번호}.png 를 전부 읽어 가로로 이어 붙인다 (아래 맞춤). 하나도 없으면 임시 그림.
    /// 같은 이름의 _n.png(법선 지도)가 있으면 똑같이 붙인다.</summary>
    private static Atlas BuildAtlas(PropKind kind)
    {
        var imgs = new List<Image>();
        var normals = new List<Image>();
        bool allNormals = true;
        for (int v = 0; v < 8; v++)
        {
            string path = $"{ArtDir}{kind.Id}_{v}.png";
            if (!ResourceLoader.Exists(path))
                break;
            imgs.Add(GD.Load<Texture2D>(path).GetImage());
            string np = $"{ArtDir}{kind.Id}_{v}_n.png";
            if (ResourceLoader.Exists(np))
                normals.Add(GD.Load<Texture2D>(np).GetImage());
            else
                allNormals = false;
        }
        if (imgs.Count == 0)
        {
            imgs.Add(Placeholder.Make(kind));
            allNormals = false;
        }
        int w = AtlasPad, h = 0;
        foreach (var img in imgs)
        {
            w += img.GetWidth() + AtlasPad;
            h = Math.Max(h, img.GetHeight());
        }
        var atlas = new Atlas { Cells = new Rect2[imgs.Count], Sizes = new Vector2I[imgs.Count], Width = w, Height = h };
        Image Pack(List<Image> src, bool record)
        {
            var dst = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            int x = AtlasPad;
            for (int i = 0; i < src.Count; i++)
            {
                var img = src[i];
                img.Convert(Image.Format.Rgba8);
                int iw = img.GetWidth(), ih = img.GetHeight();
                dst.BlitRect(img, new Rect2I(0, 0, iw, ih), new Vector2I(x, h - ih));
                if (record)
                {
                    atlas.Cells[i] = new Rect2(x, h - ih, iw, ih);
                    atlas.Sizes[i] = new Vector2I(iw, ih);
                }
                x += iw + AtlasPad;
            }
            // 멀리서 픽셀이 지글거리지 않게 밉맵 (가까이선 셰이더가 0단을 쓴다)
            dst.GenerateMipmaps();
            return dst;
        }
        atlas.Texture = ImageTexture.CreateFromImage(Pack(imgs, true));
        if (allNormals && normals.Count == imgs.Count)
            atlas.Normal = ImageTexture.CreateFromImage(Pack(normals, false));
        return atlas;
    }
}
