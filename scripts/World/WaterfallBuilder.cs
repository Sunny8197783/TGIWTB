using System.Collections.Generic;
using Godot;

namespace PixelMmo.World;

/// <summary>
/// 폭포: 절벽 면을 따라 흘러내리는 띠 + 밑의 물보라·물안개·반짝임.
/// 띠는 절벽 모양을 따라 휘어야 한다 — 평평한 판을 세우면 절벽에서 떠 보인다.
/// </summary>
public static class WaterfallBuilder
{
    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Waterfalls" };
        if (!world.Meta.TryGetProperty("waterfalls", out var list))
            return root;

        foreach (var wf in list.EnumerateArray())
        {
            float x = wf.GetProperty("x").GetSingle();
            float topZ = wf.GetProperty("top_z").GetSingle();
            float topY = wf.GetProperty("top_y").GetSingle();
            float bottomY = wf.GetProperty("bottom_y").GetSingle();
            float width = wf.GetProperty("width").GetSingle();
            root.AddChild(BuildOne(world, x, topZ, topY, bottomY, width));
        }
        return root;
    }

    private static Node3D BuildOne(WorldData w, float x, float topZ, float topY, float bottomY, float width)
    {
        var node = new Node3D { Name = $"Waterfall_{x:0}" };

        // 1) 물줄기 띠: 높이마다 절벽 면이 있는 z 를 찾아 그 앞 0.35m 에 붙인다
        const int Segs = 28;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();
        float baseZ = topZ;
        for (int i = 0; i <= Segs; i++)
        {
            float t = i / (float)Segs;
            float y = Mathf.Lerp(topY + 0.08f, bottomY - 0.1f, t);
            float z = FaceZ(w, x, topZ, y) + 0.35f + t * 0.4f;
            if (i == Segs) baseZ = z;
            verts.Add(new Vector3(x - width * 0.5f, y, z));
            verts.Add(new Vector3(x + width * 0.5f, y, z));
            uvs.Add(new Vector2(0f, t));
            uvs.Add(new Vector2(1f, t));
            if (i < Segs)
            {
                int b = i * 2;
                idx.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var normals = new Vector3[verts.Count];
        System.Array.Fill(normals, new Vector3(0f, 0.3f, 1f).Normalized());
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/waterfall.gdshader") };
        mat.SetShaderParameter("length_m", topY - bottomY);
        mat.SetShaderParameter("width_m", width);
        node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var basePos = new Vector3(x, bottomY + 0.1f, baseZ + 0.4f);
        var soft = GD.Load<Shader>("res://shaders/particle_soft.gdshader");

        // 2) 물안개: 크고 흐린 흰 덩어리가 천천히 피어오른다
        node.AddChild(Particles(soft, basePos, amount: 40, lifetime: 3.2f,
            box: new Vector3(width * 0.6f, 0.2f, 0.6f), velocity: new Vector2(0.4f, 1.2f),
            gravity: new Vector3(0.1f, 0.25f, 0.15f), size: new Vector2(1.2f, 2.4f),
            color: new Color(0.92f, 0.97f, 1f, 0.10f), pxSize: 8f, softness: 1f, intensity: 0.8f, spread: 70f));

        // 3) 물보라: 작은 흰 방울이 튀어 오르다 떨어진다
        node.AddChild(Particles(soft, basePos, amount: 60, lifetime: 0.9f,
            box: new Vector3(width * 0.5f, 0.1f, 0.3f), velocity: new Vector2(2.0f, 4.2f),
            gravity: new Vector3(0f, -9f, 0f), size: new Vector2(0.10f, 0.18f),
            color: new Color(1f, 1f, 1f, 0.9f), pxSize: 3f, softness: 0f, intensity: 1.1f, spread: 35f));

        // 4) 반짝임: 물줄기 주변에서 번쩍이는 빛 알갱이 (글로우가 잡는다)
        var glintPos = new Vector3(x, (topY + bottomY) * 0.5f, baseZ + 0.2f);
        node.AddChild(Particles(soft, glintPos, amount: 26, lifetime: 0.5f,
            box: new Vector3(width * 0.6f, (topY - bottomY) * 0.5f, 0.4f), velocity: new Vector2(0f, 0.2f),
            gravity: Vector3.Zero, size: new Vector2(0.09f, 0.14f),
            color: new Color(1f, 1f, 0.95f, 1f), pxSize: 3f, softness: 0f, intensity: 2.6f, spread: 180f));

        return node;
    }

    /// <summary>x 에서 절벽을 따라 내려가다 높이 y 가 되는 첫 z.</summary>
    private static float FaceZ(WorldData w, float x, float topZ, float y)
    {
        for (float z = topZ - 2f; z < topZ + 24f; z += 0.05f)
        {
            if (w.HeightAt(x, z) <= y)
                return z;
        }
        return topZ;
    }

    private static GpuParticles3D Particles(Shader shader, Vector3 pos, int amount, float lifetime, Vector3 box,
        Vector2 velocity, Vector3 gravity, Vector2 size, Color color, float pxSize, float softness, float intensity, float spread)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = box,
            Direction = new Vector3(0f, 1f, 0.3f),
            Spread = spread,
            InitialVelocityMin = velocity.X,
            InitialVelocityMax = velocity.Y,
            Gravity = gravity,
            ScaleMin = size.X,
            ScaleMax = size.Y,
            Color = color,
        };
        // 끝으로 갈수록 투명해진다
        var fade = new Gradient();
        fade.SetColor(0, new Color(1, 1, 1, 0));
        fade.SetOffset(0, 0f);
        fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.SetOffset(1, 1f);
        fade.AddPoint(0.2f, new Color(1, 1, 1, 1));
        fade.AddPoint(0.7f, new Color(1, 1, 1, 0.8f));
        pm.ColorRamp = new GradientTexture1D { Gradient = fade };

        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("px_size", pxSize);
        mat.SetShaderParameter("softness", softness);
        mat.SetShaderParameter("intensity", intensity);
        var quad = new QuadMesh { Size = Vector2.One, Material = mat };

        return new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = lifetime,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            VisibilityAabb = new Aabb(new Vector3(-8, -4, -8), new Vector3(16, 16, 16)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Preprocess = lifetime,
        };
    }
}
