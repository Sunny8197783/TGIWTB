using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 다리: 판자 바닥 + 양쪽 난간. 자리·방향·길이·높이는 지도 생성기가 정한다(WorldData.Bridges).
/// 판자 무늬는 1m = 32픽셀로 그려 지형·스프라이트와 픽셀 크기를 맞춘다.
/// </summary>
public static class BridgeBuilder
{
    private const float Thickness = 0.3f;
    private const float PostEvery = 1.6f;
    private const float RailHeight = 0.55f;
    private static readonly Color Wood = new(0.36f, 0.24f, 0.15f);

    public static Node3D Build(WorldData world)
    {
        var root = new Node3D { Name = "Bridges" };
        var deckMat = new StandardMaterial3D
        {
            AlbedoTexture = PlankTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            TextureRepeat = true,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        var woodMat = new StandardMaterial3D { AlbedoColor = Wood, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };
        var post = new BoxMesh { Size = new Vector3(0.14f, RailHeight, 0.14f), Material = woodMat };
        foreach (var b in world.Bridges)
        {
            float len = b.HalfLength * 2f, wid = b.HalfWidthM * 2f;
            float yaw = -Mathf.Atan2(b.Axis.Y, b.Axis.X); // 로컬 +X 를 다리 방향으로
            var node = new Node3D
            {
                Name = $"Bridge_{b.Center.X:0}_{b.Center.Y:0}",
                Position = new Vector3(b.Center.X, b.Deck, b.Center.Y),
                Rotation = new Vector3(0f, yaw, 0f),
            };
            root.AddChild(node);
            node.AddChild(new MeshInstance3D { Mesh = Slab(len, wid, deckMat, woodMat) });
            // 난간: 기둥 + 손잡이
            var rail = new BoxMesh { Size = new Vector3(len, 0.08f, 0.1f), Material = woodMat };
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * (b.HalfWidthM - 0.07f);
                node.AddChild(new MeshInstance3D { Mesh = rail, Position = new Vector3(0f, RailHeight, z) });
                int posts = Math.Max(2, (int)(len / PostEvery) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float x = Mathf.Lerp(-b.HalfLength + 0.1f, b.HalfLength - 0.1f, i / (float)(posts - 1));
                    node.AddChild(new MeshInstance3D { Mesh = post, Position = new Vector3(x, RailHeight * 0.5f, z) });
                }
            }
        }
        return root;
    }

    /// <summary>윗면(판자 무늬, UV = 미터)과 옆면(짙은 나무)으로 된 얇은 판.</summary>
    private static ArrayMesh Slab(float len, float wid, Material top, Material side)
    {
        float hx = len * 0.5f, hz = wid * 0.5f;
        var mesh = new ArrayMesh();
        AddQuad(mesh, top, new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz),
            Vector3.Up, new Vector2(len, wid));
        var sides = new List<(Vector3, Vector3, Vector3, Vector3, Vector3)>
        {
            (new(-hx, 0, hz), new(hx, 0, hz), new(hx, -Thickness, hz), new(-hx, -Thickness, hz), Vector3.Back),
            (new(hx, 0, -hz), new(-hx, 0, -hz), new(-hx, -Thickness, -hz), new(hx, -Thickness, -hz), Vector3.Forward),
            (new(hx, 0, hz), new(hx, 0, -hz), new(hx, -Thickness, -hz), new(hx, -Thickness, hz), Vector3.Right),
            (new(-hx, 0, -hz), new(-hx, 0, hz), new(-hx, -Thickness, hz), new(-hx, -Thickness, -hz), Vector3.Left),
        };
        foreach (var (a, bb, c, d, n) in sides)
            AddQuad(mesh, side, a, bb, c, d, n, Vector2.One);
        return mesh;
    }

    private static void AddQuad(ArrayMesh mesh, Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uvScale)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { a, b, c, d };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { n, n, n, n };
        arrays[(int)Mesh.ArrayType.TexUV] = new[]
        {
            new Vector2(0, 0), new Vector2(uvScale.X, 0), new Vector2(uvScale.X, uvScale.Y), new Vector2(0, uvScale.Y),
        };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
    }

    /// <summary>1m x 1m = 32x32 픽셀 판자. 다리를 건너는 방향(U)으로 판자가 가로놓인다.</summary>
    private static ImageTexture PlankTexture()
    {
        const int N = (int)Px.PerMeter;
        const int Board = 6;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var rng = new Random(9);
        var shades = new float[N / Board + 1];
        for (int i = 0; i < shades.Length; i++)
            shades[i] = 0.85f + (float)rng.NextDouble() * 0.3f;
        for (int x = 0; x < N; x++)
        {
            int board = x / Board;
            bool gap = x % Board == Board - 1;
            for (int y = 0; y < N; y++)
            {
                var c = new Color(0.62f, 0.44f, 0.28f) * shades[board];
                if ((x * 7 + y * 3) % 11 == 0) c *= 0.9f;         // 나뭇결
                if (gap) c = new Color(0.22f, 0.14f, 0.09f);        // 판자 사이 틈
                if (!gap && x % Board == 1 && (y == 3 || y == N - 4)) c = new Color(0.3f, 0.3f, 0.32f); // 못
                c.A = 1f;
                img.SetPixel(x, y, c);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
