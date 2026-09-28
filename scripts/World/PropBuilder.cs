using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 소품을 그림별로 묶어 MultiMesh 하나로 그린다 — 1,100개를 노드 1,100개로 만들면 이 노트북이 못 버틴다.
/// 그림이 아직 없으면 같은 크기의 임시 그림을 만들어 끼운다(배치 검토는 그림 없이도 된다).
/// </summary>
public static class PropBuilder
{
    private const string ArtDir = "res://art/env/";

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Props" };
        var shader = GD.Load<Shader>("res://shaders/sprite.gdshader");
        var bodies = new StaticBody3D { Name = "PropCollision" };
        root.AddChild(bodies);

        // (종류, 그림 번호) → 배치 목록
        var groups = new Dictionary<(string, int), List<PropDef>>();
        var textures = new Dictionary<string, List<Texture2D>>();
        int skipped = 0;
        foreach (var p in world.Props)
        {
            if (!PropCatalog.Kinds.TryGetValue(p.Type, out var kind))
            {
                skipped++;
                continue;
            }
            if (!textures.TryGetValue(p.Type, out var list))
                textures[p.Type] = list = LoadVariants(kind);
            int v = Math.Abs(p.Variant) % list.Count;
            if (!groups.TryGetValue((p.Type, v), out var g))
                groups[(p.Type, v)] = g = new List<PropDef>();
            g.Add(p);
            AddCollision(bodies, kind, p, world);
        }

        var quad = new QuadMesh { Size = Vector2.One, CenterOffset = new Vector3(0f, 0.5f, 0f) };
        var flatQuad = new QuadMesh { Size = Vector2.One, Orientation = PlaneMesh.OrientationEnum.Y };
        var rng = new RandomNumberGenerator { Seed = 1234 };

        foreach (var ((type, v), items) in groups)
        {
            var kind = PropCatalog.Kinds[type];
            var tex = textures[type][v];
            var size = tex.GetSize();
            var mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter("albedo_tex", tex);
            mat.SetShaderParameter("sway", kind.Sway);
            mat.SetShaderParameter("glow_at_night", kind.Glow);

            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = kind.Flat ? flatQuad : quad,
                InstanceCount = items.Count,
            };
            float wM = size.X / Px.PerMeter;
            float hM = size.Y / Px.PerMeter * Px.UprightStretch;
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                bool flip = kind.Flip && rng.Randf() < 0.5f;
                float z = float.IsNaN(p.FrontZ) ? p.Z : p.FrontZ;
                Transform3D xf;
                if (kind.Flat)
                {
                    float wy = world.WaterAt(p.X, p.Z);
                    float y = (wy > WorldData.NoWater + 1 ? wy : world.HeightAt(p.X, z)) + 0.03f;
                    var basis = Basis.Identity.Rotated(Vector3.Up, rng.Randf() * Mathf.Tau).Scaled(new Vector3(wM, 1f, size.Y / Px.PerMeter));
                    xf = new Transform3D(basis, new Vector3(p.X, y, p.Z));
                }
                else
                {
                    float y = world.HeightAt(p.X, z) - kind.Sink / Px.PerMeter * Px.UprightStretch;
                    var basis = Basis.Identity.Scaled(new Vector3(flip ? -wM : wM, hM, 1f));
                    xf = new Transform3D(basis, new Vector3(p.X, y, z));
                }
                mm.SetInstanceTransform(i, xf);
                mm.SetInstanceCustomData(i, new Color(rng.Randf(), 0.7f + rng.Randf() * 0.6f, 0f, 0f));
            }

            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"{type}_{v}",
                Multimesh = mm,
                MaterialOverride = mat,
                // 판은 그림자를 드리우지 않는다 — 아래 BuildCasters 의 보이지 않는 입체가 대신한다
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        // 종류별로 그림자 입체를 한 묶음씩
        var byKind = new Dictionary<string, List<PropDef>>();
        foreach (var ((type, _), items) in groups)
        {
            if (!byKind.TryGetValue(type, out var all))
                byKind[type] = all = new List<PropDef>();
            all.AddRange(items);
        }
        foreach (var (type, items) in byKind)
        {
            var kind = PropCatalog.Kinds[type];
            if (kind.ShadowCaster != PropKind.Caster.None && kind.Shadow)
                BuildCasters(root, kind, items, textures[type][0].GetSize(), world);
        }

        if (skipped > 0)
            GD.Print($"[Props] 목록에 없는 종류 {skipped}개는 건너뜀 (다리·부두는 따로 만든다)");
        GD.Print($"[Props] {world.Props.Count - skipped}개, 그림 묶음 {groups.Count}개");
        return root;
    }

    private static readonly Mesh UnitSphere = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 };
    private static readonly Mesh UnitCylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh UnitCone = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh UnitBox = new BoxMesh { Size = Vector3.One };

    /// <summary>
    /// 그림자만 드리우는 입체. 그림 크기에서 대략의 실제 부피를 잡고, 판보다 뒤(북쪽)에 둬서
    /// 판이 제 입체의 그림자를 뒤집어쓰지 않게 한다.
    /// </summary>
    private static void BuildCasters(Node3D root, PropKind kind, List<PropDef> items, Vector2 tex, WorldData world)
    {
        float w = tex.X / Px.PerMeter, h = tex.Y / Px.PerMeter * 0.8f;
        var parts = new List<(Mesh mesh, Vector3 scale, Vector3 offset)>();
        switch (kind.ShadowCaster)
        {
            case PropKind.Caster.Canopy:
                parts.Add((UnitSphere, new Vector3(w * 0.78f, h * 0.55f, w * 0.7f), new Vector3(0f, h * 0.64f, -w * 0.4f)));
                parts.Add((UnitCylinder, new Vector3(0.35f, h * 0.5f, 0.35f), new Vector3(0f, h * 0.25f, -w * 0.3f)));
                break;
            case PropKind.Caster.Cone:
                parts.Add((UnitCone, new Vector3(w * 0.7f, h * 0.9f, w * 0.7f), new Vector3(0f, h * 0.5f, -w * 0.4f)));
                break;
            case PropKind.Caster.Blob:
                parts.Add((UnitSphere, new Vector3(w * 0.8f, h * 0.9f, w * 0.6f), new Vector3(0f, h * 0.4f, -w * 0.35f)));
                break;
            case PropKind.Caster.Column:
                parts.Add((UnitCylinder, new Vector3(0.3f, h, 0.3f), new Vector3(0f, h * 0.5f, -0.25f)));
                break;
            case PropKind.Caster.Box:
                var fp = kind.Footprint == Vector2.Zero ? new Vector2(w * 0.8f, w * 0.6f) : kind.Footprint;
                parts.Add((UnitBox, new Vector3(fp.X, h * 0.85f, fp.Y), new Vector3(0f, h * 0.42f, -0.3f - fp.Y * 0.5f)));
                break;
        }

        foreach (var (mesh, scale, offset) in parts)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = items.Count,
            };
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                float z = float.IsNaN(p.FrontZ) ? p.Z : p.FrontZ;
                var basePos = new Vector3(p.X, world.HeightAt(p.X, z), z);
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(scale), basePos + offset));
            }
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"{kind.Id}_shadow",
                Multimesh = mm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
            });
        }
    }

    private static void AddCollision(StaticBody3D body, PropKind kind, PropDef p, WorldData world)
    {
        if (kind.Footprint != Vector2.Zero)
        {
            float front = float.IsNaN(p.FrontZ) ? p.Z + kind.Footprint.Y * 0.5f : p.FrontZ;
            float y = world.HeightAt(p.X, front);
            body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(kind.Footprint.X, 4f, kind.Footprint.Y) },
                Position = new Vector3(p.X, y + 2f, front - kind.Footprint.Y * 0.5f),
            });
        }
        else if (kind.Trunk > 0f)
        {
            float y = world.HeightAt(p.X, p.Z);
            body.AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = kind.Trunk, Height = 3f },
                Position = new Vector3(p.X, y + 1.5f, p.Z),
            });
        }
    }

    /// <summary>art/env/{종류}_{번호}.png 를 전부 읽는다. 하나도 없으면 임시 그림 하나.</summary>
    private static List<Texture2D> LoadVariants(PropKind kind)
    {
        var list = new List<Texture2D>();
        for (int v = 0; v < 8; v++)
        {
            string path = $"{ArtDir}{kind.Id}_{v}.png";
            if (!ResourceLoader.Exists(path))
                break;
            list.Add(GD.Load<Texture2D>(path));
        }
        if (list.Count == 0)
            list.Add(ImageTexture.CreateFromImage(Placeholder.Make(kind)));
        return list;
    }
}
