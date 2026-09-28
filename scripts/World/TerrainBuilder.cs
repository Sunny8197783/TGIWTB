using Godot;

namespace PixelMmo.World;

/// <summary>
/// 높이 격자 → 지형 메시(32x32 칸 덩어리로 나눠 화면 밖은 컬링) + 높이장 충돌 + 셰이더용 지도 텍스처.
/// </summary>
public static class TerrainBuilder
{
    private const int Chunk = 32;

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Terrain" };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };

        for (int cz = 0; cz < world.Height; cz += Chunk)
        {
            for (int cx = 0; cx < world.Width; cx += Chunk)
            {
                var mesh = BuildChunk(world, cx, cz, System.Math.Min(Chunk, world.Width - cx),
                    System.Math.Min(Chunk, world.Height - cz));
                var mi = new MeshInstance3D
                {
                    Name = $"Chunk_{cx}_{cz}",
                    Mesh = mesh,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                };
                root.AddChild(mi);
            }
        }

        root.AddChild(BuildCollision(world));
        PublishMaps(world);
        return root;
    }

    private static ArrayMesh BuildChunk(WorldData w, int x0, int z0, int cw, int ch)
    {
        int vw = cw + 1, vh = ch + 1;
        var verts = new Vector3[vw * vh];
        var normals = new Vector3[vw * vh];
        for (int z = 0; z < vh; z++)
        {
            for (int x = 0; x < vw; x++)
            {
                int gx = x0 + x, gz = z0 + z;
                verts[z * vw + x] = new Vector3(gx, w.VertexHeight(gx, gz), gz);
                // 중앙 차분 법선 — 덩어리 경계에서도 이웃 칸을 보므로 이음매가 없다
                float l = w.VertexHeight(gx - 1, gz), r = w.VertexHeight(gx + 1, gz);
                float d = w.VertexHeight(gx, gz - 1), u = w.VertexHeight(gx, gz + 1);
                normals[z * vw + x] = new Vector3(l - r, 2f, d - u).Normalized();
            }
        }

        var indices = new int[cw * ch * 6];
        int i = 0;
        for (int z = 0; z < ch; z++)
        {
            for (int x = 0; x < cw; x++)
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

    private static StaticBody3D BuildCollision(WorldData w)
    {
        var shape = new HeightMapShape3D
        {
            MapWidth = w.Width + 1,
            MapDepth = w.Height + 1,
            MapData = w.Heights,
        };
        var body = new StaticBody3D { Name = "TerrainCollision" };
        // HeightMapShape3D 는 원점이 가운데다
        body.AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(w.Width * 0.5f, 0f, w.Height * 0.5f) });
        return body;
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
