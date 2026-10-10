using Godot;

namespace PixelMmo.World;

/// <summary>
/// 지도 북쪽 끝 너머의 먼 설산 (V 전망·전망 지점에서 지평선을 채운다). 16m 칸 거친 메시, 지형 셰이더 그대로 —
/// 높이 52m 위 완만한 면은 눈, 가파른 면은 바위. 거리 안개가 겹겹이 옅게 만든다.
/// 북서쪽(x &lt; 160)은 비워 둔다: 해가 북북서 바다로 지는 자리라 수평선이 트여야 한다.
/// </summary>
public static class BackdropBuilder
{
    private const float Cell = 16f;
    private const float X0 = 160f, X1 = 1500f;   // 서쪽 끝은 노을 만 앞에서 바다로 잦아든다
    private const float Depth = 720f;            // 지도 끝(z=0)에서 북쪽으로
    private const float Blend = 90f;             // 지도 끝 높이에서 산 높이로 옮겨 가는 거리

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Backdrop" };
        int nx = (int)((X1 - X0) / Cell), nz = (int)(Depth / Cell);
        var verts = new Vector3[(nx + 1) * (nz + 1)];
        var noise = new FastNoiseLite { Seed = 61, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.004f,
            FractalType = FastNoiseLite.FractalTypeEnum.Ridged, FractalOctaves = 5 };
        for (int iz = 0; iz <= nz; iz++)
        {
            float z = -iz * Cell;
            for (int ix = 0; ix <= nx; ix++)
            {
                float x = X0 + ix * Cell;
                float edge = world.VertexHeight(Mathf.Clamp((int)x, 0, world.Width), 0);
                float ridge = (noise.GetNoise2D(x, z) + 1f) * 0.5f;
                // 북쪽으로 갈수록 높아지다 끝에서 조금 낮아진다 (겹겹이 선 산줄기)
                float far = Mathf.SmoothStep(0f, 260f, -z) * (1f - 0.35f * Mathf.SmoothStep(450f, Depth, -z));
                float mountain = 70f + far * (90f + 190f * ridge);
                // 서쪽 끝은 바다로 잦아든다
                float west = Mathf.SmoothStep(X0, X0 + 220f, x);
                mountain = Mathf.Lerp(-6f, mountain, west);
                // 동쪽 지도 밖도 바다 쪽으로 낮아진다
                mountain *= 1f - 0.6f * Mathf.SmoothStep(world.Width, X1, x);
                float t = Mathf.SmoothStep(0f, Blend, -z);
                verts[iz * (nx + 1) + ix] = new Vector3(x, Mathf.Lerp(edge, mountain, t), z);
            }
        }
        var normals = new Vector3[verts.Length];
        var idx = new int[nx * nz * 6];
        int k = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                // 북쪽(z 감소)으로 자라는 격자 — 지형과 같은 감기 방향이 되도록 위아래를 뒤집어 잇는다
                int a = (iz + 1) * (nx + 1) + ix, b = a + 1, c = iz * (nx + 1) + ix, e = c + 1;
                idx[k++] = a; idx[k++] = b; idx[k++] = e;
                idx[k++] = a; idx[k++] = e; idx[k++] = c;
            }
        }
        for (int i = 0; i < idx.Length; i += 3)
        {
            var n = (verts[idx[i + 2]] - verts[idx[i]]).Cross(verts[idx[i + 1]] - verts[idx[i]]);
            normals[idx[i]] += n;
            normals[idx[i + 1]] += n;
            normals[idx[i + 2]] += n;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].Normalized();
            if (normals[i].Y < 0f)
                normals[i] = -normals[i];
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        root.AddChild(new MeshInstance3D
        {
            Name = "FarMountains",
            Mesh = mesh,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }
}
