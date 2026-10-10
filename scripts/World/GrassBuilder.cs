using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 풀포기: 풀밭 위에 작은 잎 다발을 촘촘히 세운다. 평평한 땅이 '들판'으로 보이게 하는 가장 싼 방법.
/// 1km 지도 전체에 심으면 100만 포기가 넘어서 카메라 둘레 32m 덩어리만 그때그때 심고(한 프레임에 하나),
/// 멀어진 덩어리는 버린다. 꽃밭은 줄마다 색이 다른 꽃포기(잎 + 꽃송이 두 장)로 채운다.
/// </summary>
public partial class GrassField : Node3D
{
    private const int Chunk = 32;
    private const float VisibleRange = 46f;
    private const int BuildRadius = 2;     // 카메라 둘레 이 만큼의 덩어리 (칸)
    private const int KeepRadius = 4;      // 이보다 멀면 버린다
    private const int Floats = 20;         // 인스턴스 하나: 변환 12 + 색 4 + 사용자 4

    // 지면 종류별 (풀 색, 1m² 당 포기 수). mapgen 번호 순서. 색이 없으면 풀이 없다.
    private static readonly (Color? tint, float density)[] Kinds =
    {
        (new Color(0.36f, 0.68f, 0.24f), 2.2f), // GRASS
        (new Color(0.22f, 0.44f, 0.22f), 1.6f), // FOREST
        (new Color(0.46f, 0.74f, 0.26f), 2.4f), // MEADOW
        (null, 0f), (null, 0f), (null, 0f),     // DIRT COBBLE SAND
        (new Color(0.42f, 0.64f, 0.28f), 2.0f), // SAKURA
        (new Color(0.56f, 0.56f, 0.24f), 1.4f), // AUTUMN
        (null, 0f), (null, 0f),                 // GRAVEL PEBBLE
        (null, 0f),                             // SNOW
        (new Color(0.26f, 0.5f, 0.22f), 6.0f),  // TULIP (잎, 꽃송이는 따로)
        (new Color(0.56f, 0.42f, 0.82f), 6.0f), // LAVENDER
        (new Color(0.2f, 0.4f, 0.22f), 1.2f),   // MOSS
        (null, 0f), (null, 0f),                 // ROCKY STONE
        (new Color(0.66f, 0.6f, 0.24f), 0.8f),  // GINKGO
        (new Color(0.86f, 0.72f, 0.34f), 8.0f), // WHEAT
        (null, 0f),                             // ICE
        (new Color(0.72f, 0.66f, 0.44f), 0.7f), // FROSTGRASS (눈 사이 마른 풀)
        (new Color(0.66f, 0.6f, 0.26f), 2.0f),  // GOLDGRASS (가을 빈터)
    };
    private const byte Tulip = 11, Lavender = 12, Wheat = 17;
    private const float RowWidth = 1.6f;   // 꽃밭 한 줄 (m)
    private static readonly Color[] TulipColors =
    {
        new(0.95f, 0.25f, 0.3f), new(0.98f, 0.85f, 0.25f), new(0.98f, 0.96f, 0.9f),
        new(0.95f, 0.5f, 0.7f), new(0.65f, 0.35f, 0.85f), new(0.98f, 0.55f, 0.2f),
    };

    private readonly WorldData _w;
    private ShaderMaterial _mat;
    private QuadMesh _quad;
    private readonly Dictionary<(int, int), MultiMeshInstance3D> _live = new();
    private readonly HashSet<(int, int)> _empty = new();

    public GrassField(WorldData world)
    {
        Name = "Grass";
        _w = world;
    }

    public GrassField() { }

    public override void _Ready()
    {
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sprite.gdshader") };
        _mat.SetShaderParameter(Uniform.AlbedoTex, MakeTufts());
        _mat.SetShaderParameter(Uniform.FrameCount, new Vector2(Frames, 1));
        _mat.SetShaderParameter("sway", 0.05f);
        _mat.SetShaderParameter("instance_tint", true);
        _quad = new QuadMesh
        {
            Size = new Vector2(10f / Px.PerMeter, 8f / Px.PerMeter * Px.UprightStretch),
            CenterOffset = new Vector3(0f, 4f / Px.PerMeter * Px.UprightStretch, 0f),
        };
    }

    public override void _Process(double delta)
    {
        var t = GameRoot.Instance.Rig.Target;
        int ccx = (int)(t.X / Chunk), ccz = (int)(t.Z / Chunk);
        // 가장 가까운 빈 덩어리부터 심는다
        var best = NearestMissing(ccx, ccz, out int bestD);
        // 바로 둘레(한 칸)가 비었으면 (순간이동 직후) 한 프레임에 여럿, 아니면 하나씩
        int budget = bestD <= 2 ? 4 : 1;
        while (best is (int bx, int bz) && budget-- > 0)
        {
            Plant(bx, bz);
            best = NearestMissing(ccx, ccz, out bestD);
        }
        // 먼 덩어리 버리기
        List<(int, int)> drop = null;
        foreach (var key in _live.Keys)
            if (Math.Abs(key.Item1 - ccx) > KeepRadius || Math.Abs(key.Item2 - ccz) > KeepRadius)
                (drop ??= new List<(int, int)>()).Add(key);
        if (drop != null)
            foreach (var key in drop)
            {
                _live[key].QueueFree();
                _live.Remove(key);
            }
    }

    private (int, int)? NearestMissing(int ccx, int ccz, out int bestD)
    {
        (int, int)? best = null;
        bestD = int.MaxValue;
        for (int dz = -BuildRadius; dz <= BuildRadius; dz++)
        {
            for (int dx = -BuildRadius; dx <= BuildRadius; dx++)
            {
                var key = (ccx + dx, ccz + dz);
                if (key.Item1 < 0 || key.Item2 < 0 || key.Item1 * Chunk >= _w.Width || key.Item2 * Chunk >= _w.Height)
                    continue;
                if (_live.ContainsKey(key) || _empty.Contains(key))
                    continue;
                int d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = key;
                }
            }
        }
        return best;
    }

    /// <summary>덩어리 하나에 포기를 심는다. 같은 덩어리는 늘 같은 자리에 (좌표로 씨앗을 정한다).</summary>
    private void Plant(int cx, int cz)
    {
        var rng = new Random(cx * 7919 + cz * 104729);
        var data = new List<float>(4096);
        int count = 0;
        void Add(float px, float y, float pz, bool flip, float sy, Color tint, int frame)
        {
            data.Add(flip ? -1f : 1f); data.Add(0f); data.Add(0f); data.Add(px);
            data.Add(0f); data.Add(sy); data.Add(0f); data.Add(y);
            data.Add(0f); data.Add(0f); data.Add(1f); data.Add(pz);
            data.Add(tint.R); data.Add(tint.G); data.Add(tint.B); data.Add(1f);
            // x: 흔들림 위상, y: 흔들림 배율, z: 그림 번호
            data.Add(rng.NextSingle()); data.Add(1f); data.Add(frame); data.Add(0f);
            count++;
        }
        int x0 = cx * Chunk, z0 = cz * Chunk;
        for (int z = z0; z < Math.Min(z0 + Chunk, _w.Height); z++)
        {
            for (int x = x0; x < Math.Min(x0 + Chunk, _w.Width); x++)
            {
                byte g = _w.Ground[z * _w.Width + x];
                if (g >= Kinds.Length || Kinds[g].tint is not Color baseTint)
                    continue;
                if (_w.WaterAt(x + 0.5f, z + 0.5f) > WorldData.NoWater + 1f)
                    continue;
                float h00 = _w.VertexHeight(x, z), h11 = _w.VertexHeight(x + 1, z + 1);
                if (Math.Abs(h11 - h00) > 0.6f)
                    continue; // 절벽에는 없다
                bool field = g == Tulip || g == Lavender || g == Wheat;
                // 들판마다 덤성덤성한 곳과 촘촘한 곳이 있어야 자연스럽다 (밭은 고르게)
                float patch = field ? 0.7f : 0.5f + 0.5f * MathF.Sin(x * 0.21f + MathF.Cos(z * 0.17f) * 2f) * MathF.Cos(z * 0.13f);
                int n = (int)(Kinds[g].density * (0.3f + patch) + rng.NextSingle());
                for (int i = 0; i < n; i++)
                {
                    float px = x + rng.NextSingle(), pz = z + rng.NextSingle();
                    if (field)
                    {
                        // 밭고랑: 줄 사이 0.4m 는 비운다
                        float row = pz / RowWidth;
                        if (row - MathF.Floor(row) > 0.75f)
                            continue;
                    }
                    float shade = 0.85f + rng.NextSingle() * 0.3f;
                    var tint = new Color(baseTint.R * shade, baseTint.G * shade, baseTint.B * shade);
                    float y = _w.HeightAt(px, pz) - 0.02f;
                    bool flip = rng.Next(2) == 0;
                    float sy = g == Wheat ? 1.5f + rng.NextSingle() * 0.3f : 1f;
                    Add(px, y, pz, flip, sy, tint, rng.Next(4));
                    if (g == Tulip)
                    {
                        // 꽃송이: 줄마다 한 색, 약 44m 마다 색 순서가 바뀐다. 바뀌는 자리가 줄마다 어긋나
                        // 밭이 세로 경계로 반듯하게 잘려 보이지 않는다 (terrain.gdshader 의 tulip_color 와 같은 계산)
                        int rowI = (int)MathF.Floor(pz / RowWidth);
                        int block = (int)MathF.Floor((px + 14f * MathF.Sin(rowI * 0.9f)) / 44f) * 31 + (int)(pz / 40f) * 17;
                        var c = TulipColors[(int)((uint)(rowI * 2654435761u + (uint)block * 40503u) % (uint)TulipColors.Length)];
                        Add(px, y + 0.02f, pz + 0.01f, flip, 1f, c * (0.9f + rng.NextSingle() * 0.15f), 4 + rng.Next(2));
                    }
                }
            }
        }
        var key = (cx, cz);
        if (count == 0)
        {
            _empty.Add(key);
            return;
        }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = _quad,
            InstanceCount = count,
            Buffer = data.ToArray(),
        };
        var mi = new MultiMeshInstance3D
        {
            Name = $"Grass_{cx}_{cz}",
            Multimesh = mm,
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = VisibleRange,
            VisibilityRangeEndMargin = 4f,
        };
        AddChild(mi);
        _live[key] = mi;
    }

    private const int Frames = 6;

    /// <summary>10x8 픽셀 칸 여섯: 풀포기 넷(흰 잎, 색은 인스턴스 색) + 꽃송이 둘(위쪽 점 몇 개).</summary>
    private static ImageTexture MakeTufts()
    {
        const int W = 10, H = 8;
        var img = Image.CreateEmpty(W * Frames, H, false, Image.Format.Rgba8);
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
        for (int v = 4; v < Frames; v++)
        {
            // 잎 위로 솟은 꽃송이 2~3개: 가운데 밝고 테두리 한 단 어둡게
            int heads = 2 + (v % 2);
            for (int b = 0; b < heads; b++)
            {
                int hx = v * W + 2 + rng.Next(W - 4), hy = 1 + rng.Next(3);
                img.SetPixel(hx, hy, new Color(1f, 1f, 1f, 1f));
                img.SetPixel(hx + 1, hy, new Color(0.85f, 0.85f, 0.85f, 1f));
                img.SetPixel(hx, hy + 1, new Color(0.75f, 0.75f, 0.75f, 1f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
