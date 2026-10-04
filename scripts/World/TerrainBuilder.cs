using Godot;

namespace PixelMmo.World;

/// <summary>
/// 높이 격자 → 지형 메시 + 셰이더용 지도 텍스처.
/// 1km 지도라 두 겹으로 그린다: 가까이는 1m 칸 그대로(32m 덩어리), 멀리는 4m 칸 거친 판(128m 덩어리).
/// 거친 판은 조금 가라앉혀 두 겹이 겹치는 거리에서 촘촘한 쪽이 이긴다 (거친 판끼리는 같은 점을 지나 틈이 없다).
/// </summary>
public static class TerrainBuilder
{
    private const int Fine = 32;
    private const int Coarse = 128;
    private const int CoarseStep = 4;
    /// <summary>이 거리(m, 덩어리 가운데까지) 안에서는 거친 판을 감춘다.</summary>
    private const float CoarseBegin = 150f;
    /// <summary>거친 판이 숨은 곳은 모두 촘촘한 판이 덮어야 한다: 거친 판 가운데~모서리(90.5m) 만큼 더.</summary>
    private const float FineEnd = CoarseBegin + Coarse * 0.7072f + 2f;
    private const float CoarseSink = 0.4f;

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Terrain" };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };

        for (int cz = 0; cz < world.Height; cz += Fine)
        {
            for (int cx = 0; cx < world.Width; cx += Fine)
            {
                root.AddChild(new MeshInstance3D
                {
                    Name = $"Chunk_{cx}_{cz}",
                    Mesh = BuildGrid(world, cx, cz, System.Math.Min(Fine, world.Width - cx), System.Math.Min(Fine, world.Height - cz), 1, 0f),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = FineEnd,
                });
            }
        }
        for (int cz = 0; cz < world.Height; cz += Coarse)
        {
            for (int cx = 0; cx < world.Width; cx += Coarse)
            {
                root.AddChild(new MeshInstance3D
                {
                    Name = $"Far_{cx}_{cz}",
                    Mesh = BuildGrid(world, cx, cz, System.Math.Min(Coarse, world.Width - cx), System.Math.Min(Coarse, world.Height - cz), CoarseStep, CoarseSink),
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeBegin = CoarseBegin,
                });
            }
        }

        PublishMaps(world);
        return root;
    }

    /// <param name="step">꼭짓점 간격 (m)</param>
    /// <param name="sink">판 전체를 내리는 양 (m)</param>
    private static ArrayMesh BuildGrid(WorldData w, int x0, int z0, int cw, int ch, int step, float sink)
    {
        int nx = cw / step, nz = ch / step;
        int vw = nx + 1, vh = nz + 1;
        var verts = new Vector3[vw * vh];
        var normals = new Vector3[verts.Length];
        for (int z = 0; z < vh; z++)
        {
            for (int x = 0; x < vw; x++)
            {
                int gx = x0 + x * step, gz = z0 + z * step;
                verts[z * vw + x] = new Vector3(gx, w.VertexHeight(gx, gz) - sink, gz);
                // 중앙 차분 법선 — 덩어리 경계에서도 이웃 칸을 보므로 이음매가 없다
                float l = w.VertexHeight(gx - step, gz), r = w.VertexHeight(gx + step, gz);
                float d = w.VertexHeight(gx, gz - step), u = w.VertexHeight(gx, gz + step);
                normals[z * vw + x] = new Vector3(l - r, 2f * step, d - u).Normalized();
            }
        }

        var indices = new int[nx * nz * 6];
        int i = 0;
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int a = z * vw + x, b = a + 1, c = a + vw, e = c + 1;
                // 00-10-11 / 00-11-01 (WorldData.HeightAt 과 같은 분할). Godot 는 시계방향이 앞면.
                indices[i++] = a; indices[i++] = b; indices[i++] = e;
                indices[i++] = a; indices[i++] = e; indices[i++] = c;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>셰이더(지형·물·풀)가 공유하는 지도 텍스처를 전역 변수로 올린다.</summary>
    private static void PublishMaps(WorldData w)
    {
        int vw = w.Width + 1, vh = w.Height + 1;

        var hb = new byte[vw * vh * 4];
        System.Buffer.BlockCopy(w.Heights, 0, hb, 0, hb.Length);
        var heightImg = Image.CreateFromData(vw, vh, false, Image.Format.Rf, hb);
        heightImg.Convert(Image.Format.Rh);

        var wb = new byte[vw * vh * 4];
        System.Buffer.BlockCopy(w.Water, 0, wb, 0, wb.Length);
        var waterImg = Image.CreateFromData(vw, vh, false, Image.Format.Rf, wb);
        waterImg.Convert(Image.Format.Rh);

        var groundImg = Image.CreateFromData(w.Width, w.Height, false, Image.Format.R8, w.Ground);

        RenderingServer.GlobalShaderParameterSet("height_map", ImageTexture.CreateFromImage(heightImg));
        RenderingServer.GlobalShaderParameterSet("water_map", ImageTexture.CreateFromImage(waterImg));
        RenderingServer.GlobalShaderParameterSet("ground_map", ImageTexture.CreateFromImage(groundImg));
        RenderingServer.GlobalShaderParameterSet("map_size", new Vector2(w.Width, w.Height));
    }
}
