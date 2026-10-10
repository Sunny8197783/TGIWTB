using System.Collections.Generic;
using Godot;

namespace PixelMmo.World;

/// <summary>
/// 수면: 지도 안은 칸마다 평평한 사각형(호수·강·바다 연안, 64m 덩어리), 지도 바깥은 수평선까지 이어지는 큰 바다 테.
/// 수면이 둑 밑으로 조금 파고들게 만들어 두었으니(mapgen +0.35) 해안선은 지형이 가려서 자연스럽게 끊긴다.
/// </summary>
public static class WaterBuilder
{
    private const float OceanReach = 1600f;

    /// <summary>꼭짓점 색에 담는 깊이의 상한(m). water.gdshader 의 MAX_DEPTH 와 같다.</summary>
    public const float MaxDepth = 4f;

    public static Node3D Build(WorldData w)
    {
        var root = new Node3D { Name = "Water" };
        var shader = GD.Load<Shader>("res://shaders/water.gdshader");

        var local = new ShaderMaterial { Shader = shader };
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--debug-water") >= 0)
            local.SetShaderParameter("debug_depth", true);
        // 64m 덩어리로 나눠 화면 밖은 컬링. 깊은 바다 칸은 한 줄로 이어 붙여 사각형 하나로.
        for (int cz = 0; cz < w.Height; cz += Chunk)
        {
            for (int cx = 0; cx < w.Width; cx += Chunk)
            {
                var mesh = BuildChunk(w, cx, cz, System.Math.Min(cx + Chunk, w.Width), System.Math.Min(cz + Chunk, w.Height));
                if (mesh == null)
                    continue;
                root.AddChild(new MeshInstance3D
                {
                    Name = $"Water_{cx}_{cz}",
                    Mesh = mesh,
                    MaterialOverride = local,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }

        // 지도 바깥 바다: 지도 사각형을 둘러싼 네 장. 지도 안 바다와 겹치지 않게 경계에서 딱 끊는다.
        var ocean = new ShaderMaterial { Shader = shader };
        ocean.SetShaderParameter("assume_deep", true);
        float W = w.Width, H = w.Height, R = OceanReach;
        var ov = new List<Vector3>();
        var oi = new List<int>();
        void Quad(float x0, float z0, float x1, float z1)
        {
            int b = ov.Count;
            ov.Add(new Vector3(x0, 0, z0));
            ov.Add(new Vector3(x1, 0, z0));
            ov.Add(new Vector3(x0, 0, z1));
            ov.Add(new Vector3(x1, 0, z1));
            oi.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        Quad(-R, -R, W + R, 0);      // 북
        Quad(-R, H, W + R, H + R);   // 남
        Quad(-R, 0, 0, H);           // 서
        Quad(W, 0, W + R, H);        // 동
        root.AddChild(new MeshInstance3D
        {
            Name = "Ocean",
            Mesh = Flat(ov, oi, null),
            MaterialOverride = ocean,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    private const int Chunk = 64;

    private static float CellSurface(WorldData w, int x, int z)
    {
        float s = WorldData.NoWater;
        for (int dz = 0; dz <= 1; dz++)
            for (int dx = 0; dx <= 1; dx++)
                s = Mathf.Max(s, w.Water[(z + dz) * w.VertexWidth + x + dx]);
        return s;
    }

    /// <summary>네 꼭짓점 모두 MaxDepth 보다 깊다 — 색이 한 가지라 옆 칸과 합쳐도 된다.</summary>
    private static bool Deep(WorldData w, int x, int z, float s)
    {
        for (int dz = 0; dz <= 1; dz++)
            for (int dx = 0; dx <= 1; dx++)
                if (s - w.VertexHeight(x + dx, z + dz) < MaxDepth)
                    return false;
        return true;
    }

    private static ArrayMesh BuildChunk(WorldData w, int x0, int z0, int x1, int z1)
    {
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        void Quad(float ax, float az, float bx, float bz, float s, System.Func<int, int, float> depthAt)
        {
            int b = verts.Count;
            for (int dz = 0; dz <= 1; dz++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    float x = dx == 0 ? ax : bx, z = dz == 0 ? az : bz;
                    verts.Add(new Vector3(x, s, z));
                    // 깊이를 꼭짓점에 굽는다 (R = 깊이/MaxDepth). 셰이더가 텍스처로 지형을 다시 읽지 않아도 된다.
                    colors.Add(new Color(Mathf.Clamp(depthAt((int)x, (int)z) / MaxDepth, 0f, 1f), 0f, 0f));
                }
            }
            indices.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        for (int z = z0; z < z1; z++)
        {
            int x = x0;
            while (x < x1)
            {
                float s = CellSurface(w, x, z);
                if (s <= WorldData.NoWater + 1f)
                {
                    x++;
                    continue;
                }
                if (Deep(w, x, z, s))
                {
                    int run = x + 1;
                    while (run < x1 && CellSurface(w, run, z) == s && Deep(w, run, z, s))
                        run++;
                    Quad(x, z, run, z + 1, s, (_, _) => MaxDepth);
                    x = run;
                    continue;
                }
                float surf = s;
                Quad(x, z, x + 1, z + 1, s, (vx, vz) => surf - w.VertexHeight(vx, vz));
                x++;
            }
        }
        return verts.Count == 0 ? null : Flat(verts, indices, colors);
    }

    private static ArrayMesh Flat(List<Vector3> verts, List<int> indices, List<Color> colors)
    {
        var normals = new Vector3[verts.Count];
        System.Array.Fill(normals, Vector3.Up);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        if (colors != null)
            arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
