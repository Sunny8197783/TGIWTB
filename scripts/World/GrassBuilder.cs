using System;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 풀포기: 풀밭 위에 작은 잎 다발을 촘촘히 세운다. 평평한 땅이 '들판'으로 보이게 하는 가장 싼 방법.
/// 32m 덩어리로 나누고 가까운 덩어리만 그린다(VisibilityRange) — 섬 전체를 그리면 이 노트북이 못 버틴다.
/// </summary>
public static class GrassBuilder
{
    private const int Chunk = 32;
    private const float Density = 2.2f;      // 1m² 당 포기 수 (풀밭 기준)
    private const float VisibleRange = 46f;

    // 지면 종류별 풀 색 (mapgen 번호). 없는 종류는 풀을 심지 않는다.
    private static readonly Color?[] Tints =
    {
        new Color(0.36f, 0.68f, 0.24f), // GRASS
        new Color(0.22f, 0.44f, 0.22f), // FOREST
        new Color(0.46f, 0.74f, 0.26f), // MEADOW
        null, null, null,               // DIRT COBBLE SAND
        new Color(0.42f, 0.64f, 0.28f), // SAKURA
        new Color(0.56f, 0.56f, 0.24f), // AUTUMN
        null, null,
    };

    public static Node3D Build(WorldData w)
    {
        var root = new Node3D { Name = "Grass" };
        var textures = MakeTufts();
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sprite.gdshader") };
        mat.SetShaderParameter("albedo_tex", textures);
        mat.SetShaderParameter("frame_count", new Vector2(4, 1));
        mat.SetShaderParameter("sway", 0.05f);
        mat.SetShaderParameter("instance_tint", true);

        var quad = new QuadMesh
        {
            Size = new Vector2(10f / Px.PerMeter, 8f / Px.PerMeter * Px.UprightStretch),
            CenterOffset = new Vector3(0f, 4f / Px.PerMeter * Px.UprightStretch, 0f),
        };
        var rng = new Random(77);

        for (int cz = 0; cz < w.Height; cz += Chunk)
        {
            for (int cx = 0; cx < w.Width; cx += Chunk)
            {
                var spots = new System.Collections.Generic.List<(Vector3 pos, Color tint, float frame)>();
                for (int z = cz; z < Math.Min(cz + Chunk, w.Height); z++)
                {
                    for (int x = cx; x < Math.Min(cx + Chunk, w.Width); x++)
                    {
                        byte g = w.Ground[z * w.Width + x];
                        if (g >= Tints.Length || Tints[g] is not Color baseTint)
                            continue;
                        if (w.WaterAt(x + 0.5f, z + 0.5f) > WorldData.NoWater + 1f)
                            continue;
                        float h00 = w.VertexHeight(x, z), h11 = w.VertexHeight(x + 1, z + 1);
                        if (Math.Abs(h11 - h00) > 0.6f)
                            continue; // 절벽에는 없다
                        // 들판마다 덤성덤성한 곳과 촘촘한 곳이 있어야 자연스럽다
                        float patch = 0.5f + 0.5f * MathF.Sin(x * 0.21f + MathF.Cos(z * 0.17f) * 2f) * MathF.Cos(z * 0.13f);
                        int n = (int)(Density * (0.3f + patch) + rng.NextSingle());
                        for (int i = 0; i < n; i++)
                        {
                            float px = x + rng.NextSingle(), pz = z + rng.NextSingle();
                            float shade = 0.85f + rng.NextSingle() * 0.3f;
                            var tint = new Color(baseTint.R * shade, baseTint.G * shade, baseTint.B * shade);
                            spots.Add((new Vector3(px, w.HeightAt(px, pz) - 0.02f, pz), tint, rng.Next(4)));
                        }
                    }
                }
                if (spots.Count == 0)
                    continue;

                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    UseCustomData = true,
                    Mesh = quad,
                    InstanceCount = spots.Count,
                };
                for (int i = 0; i < spots.Count; i++)
                {
                    var (pos, tint, frame) = spots[i];
                    bool flip = rng.Next(2) == 0;
                    mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(flip ? -1 : 1, 1, 1)), pos));
                    mm.SetInstanceColor(i, tint);
                    // x: 흔들림 위상, y: 흔들림 배율, z: 그림 번호 (셰이더 frame 대신 인스턴스별)
                    mm.SetInstanceCustomData(i, new Color(rng.NextSingle(), 1f, frame, 0f));
                }
                root.AddChild(new MultiMeshInstance3D
                {
                    Name = $"Grass_{cx}_{cz}",
                    Multimesh = mm,
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = VisibleRange,
                    VisibilityRangeEndMargin = 4f,
                });
            }
        }
        return root;
    }

    /// <summary>10x8 픽셀 풀포기 네 가지를 가로로 이은 흰 그림. 색은 인스턴스 색으로 입힌다.</summary>
    private static ImageTexture MakeTufts()
    {
        const int W = 10, H = 8;
        var img = Image.CreateEmpty(W * 4, H, false, Image.Format.Rgba8);
        var rng = new Random(5);
        for (int v = 0; v < 4; v++)
        {
            int blades = 3 + v % 2;
            for (int b = 0; b < blades; b++)
            {
                int x0 = v * W + 1 + rng.Next(W - 2);
                int height = 4 + rng.Next(H - 4);
                int lean = rng.Next(3) - 1;
                for (int y = 0; y < height; y++)
                {
                    int x = x0 + (y > height / 2 ? lean : 0);
                    x = Math.Clamp(x, v * W, v * W + W - 1);
                    // 아래는 어둡고 끝은 밝다
                    float k = 0.62f + 0.5f * y / height;
                    img.SetPixel(x, H - 1 - y, new Color(k, k, k * 0.9f, 1f));
                }
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
