using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.World;

/// <summary>
/// 그림이 땅에 닿은 바닥 모양을 읽는다 — 충돌을 '보이는 모양 그대로'로 (PropBuilder.AddCollision).
///
/// 판은 남쪽(카메라)을 보고 서 있고 카메라는 38° 내려다본다. 그림 맨 아래 불투명 픽셀이 바닥의 맨 앞(판 자리)이고,
/// 열마다 아랫선이 py 픽셀 올라가 있으면 그 열의 바닥은 py / (32·sin38°) m 뒤(북쪽)에서 시작한다.
/// 아랫선이 조금만(TouchDepth 안) 올라간 열만 땅에 닿은 것으로 본다 — 나무는 밑동·뿌리만, 수관은 빠진다.
/// 이어진 열 묶음 하나가 조각 하나: 앞 윤곽은 그림대로, 뒤 윤곽은 앞을 뒤집어(둥근 밑동은 원, 집은 Footprint 깊이).
/// </summary>
public static class Footprint
{
    public sealed class Part
    {
        /// <summary>앞 윤곽 (x m — 그림 가운데 기준 오른쪽 +, d m — 판에서 뒤로)</summary>
        public Vector2[] Front;
        /// <summary>판에서 바닥 뒤끝까지 (m)</summary>
        public float Depth;
    }

    /// <summary>아랫선이 이만큼 안쪽(m)까지 올라간 열까지 땅에 닿았다고 본다. 건물은 계단·턱이 있어 조금 더</summary>
    private const float TouchDepth = 0.9f, BuildingTouch = 1.6f;
    /// <summary>이보다 좁은 틈은 잇는다 (px). 건물·울타리는 기둥 사이를 막아야 해서 1m</summary>
    private const int GapPx = 2, BuildingGapPx = 32;
    private const int MinWidthPx = 3;
    /// <summary>아래서부터 이어 칠해진 길이가 그림에서 가장 긴 열의 이 비율·이 px 넘는 열만 막는다 —
    /// 도리이·고갯문 밑 돌계단, 집 앞 계단, 낮은 뿌리처럼 기둥보다 훨씬 낮은 것은 밟고 지나간다</summary>
    private const float MinRunRatio = 0.45f;
    private const int MinRunPx = 20;
    private const int Alpha = 127;

    private static float DepthPerPx => 1f / (Px.PerMeter * Mathf.Sin(Mathf.DegToRad(Px.PitchDeg)));

    public static List<Part> Read(Image src, PropKind kind)
    {
        var img = (Image)src.Duplicate();
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        byte[] data = img.GetData();
        var bottom = new int[w];
        var run = new int[w];
        int lowest = -1;
        for (int x = 0; x < w; x++)
        {
            bottom[x] = -1;
            for (int y = h - 1; y >= 0; y--)
            {
                if (data[(y * w + x) * 4 + 3] > Alpha)
                {
                    bottom[x] = y;
                    break;
                }
            }
            for (int y = bottom[x]; y >= 0 && data[(y * w + x) * 4 + 3] > Alpha; y--)
                run[x]++;
            lowest = Math.Max(lowest, bottom[x]);
        }
        return Parts(bottom, run, lowest, w, kind);
    }

    /// <summary>열마다 아랫선(그림 y, 없으면 -1)과 거기서 위로 이어 칠한 길이(px)에서 바닥 조각들. 그림 없이 검사할 수 있게 따로 뺐다.</summary>
    public static List<Part> Parts(int[] bottom, int[] run, int lowest, int w, PropKind kind)
    {
        bool building = kind.Footprint != Vector2.Zero;
        float touch = building ? BuildingTouch : TouchDepth;
        int gap = building ? BuildingGapPx : GapPx;
        float k = DepthPerPx;
        var parts = new List<Part>();
        if (lowest < 0)
            return parts;
        int maxRun = 0;
        foreach (int r in run)
            maxRun = Math.Max(maxRun, r);
        int minRun = Math.Min(MinRunPx, maxRun);
        bool Touches(int x) => bottom[x] >= 0 && run[x] >= Math.Max(minRun, maxRun * MinRunRatio) && (lowest - bottom[x]) * k <= touch;

        int start = -1, lastIn = -1;
        for (int x = 0; x <= w; x++)
        {
            bool on = x < w && Touches(x);
            if (on)
            {
                if (start < 0)
                    start = x;
                lastIn = x;
                continue;
            }
            // 틈: 다음 닿는 열이 gap 안이면 이어 간다
            if (start >= 0 && x < w && x - lastIn <= gap && NextTouch(x, w, gap, Touches))
                continue;
            if (start >= 0)
            {
                if (lastIn - start + 1 >= MinWidthPx)
                    parts.Add(MakePart(bottom, lowest, start, lastIn, w, kind, Touches));
                start = -1;
            }
        }
        // 땅에 닿은 데가 없다 (떠 있는 그림) — 예전처럼 줄기 원 / 건물 상자
        if (parts.Count == 0)
        {
            float half = building ? kind.Footprint.X * 0.5f : Mathf.Max(kind.Trunk, 0.2f);
            parts.Add(new Part { Front = new[] { new Vector2(-half, 0f), new Vector2(half, 0f) }, Depth = building ? kind.Footprint.Y : half * 2f });
        }
        return parts;
    }

    private static bool NextTouch(int x, int w, int gap, Func<int, bool> touches)
    {
        for (int i = x; i < Math.Min(w, x + gap + 1); i++)
            if (touches(i))
                return true;
        return false;
    }

    private static Part MakePart(int[] bottom, int lowest, int x0, int x1, int w, PropKind kind, Func<int, bool> touches)
    {
        float k = DepthPerPx;
        var pts = new List<Vector2>();
        float maxD = 0f;
        float lastD = 0f;
        for (int x = x0; x <= x1; x++)
        {
            // 틈 열(건물 기둥 사이)은 양옆 값을 이어 쓴다
            float d = touches(x) ? (lowest - bottom[x]) * k : lastD;
            lastD = d;
            maxD = Mathf.Max(maxD, d);
            if (x == x0 || x == x1 || (x - x0) % 2 == 0)
                pts.Add(new Vector2((x + 0.5f - w * 0.5f) / Px.PerMeter, d));
        }
        float width = (x1 - x0 + 1) / Px.PerMeter;
        float depth = kind.Footprint != Vector2.Zero
            ? Mathf.Max(kind.Footprint.Y, maxD * 2f)
            : Mathf.Clamp(maxD * 2f, width * 0.5f, width * 1.2f);
        return new Part { Front = pts.ToArray(), Depth = Mathf.Max(depth, maxD + 0.2f) };
    }

    /// <summary>바닥 조각 → 볼록 기둥 (판 자리 원점, 높이 height). 뒤 윤곽은 앞을 거울로: 뒤 = Depth - 앞.</summary>
    public static Shape3D Prism(Part p, PropKind kind, bool flip, float height)
    {
        var pts = new Vector3[p.Front.Length * 4];
        for (int i = 0; i < p.Front.Length; i++)
        {
            float x = flip ? -p.Front[i].X : p.Front[i].X;
            float d = p.Front[i].Y;
            float back = Mathf.Max(p.Depth - d, d + 0.1f);
            pts[i * 4] = new Vector3(x, 0f, -d);
            pts[i * 4 + 1] = new Vector3(x, height, -d);
            pts[i * 4 + 2] = new Vector3(x, 0f, -back);
            pts[i * 4 + 3] = new Vector3(x, height, -back);
        }
        return new ConvexPolygonShape3D { Points = pts };
    }

    /// <summary>자기 점검: 두 기둥(아치) → 조각 둘, 둥근 덤불 → 조각 하나에 깊이 ≈ 폭, 공중 수관 열은 빠진다.</summary>
    public static void SelfCheck()
    {
        var tree = new PropKind { Id = "t", Trunk = 0.4f };
        // 폭 40: 가운데 10열만 밑동(아랫선 99), 나머지는 수관(아랫선 40 — 땅에서 59px 위)
        var b = new int[40];
        for (int x = 0; x < 40; x++)
            b[x] = x >= 15 && x < 25 ? 99 : 40;
        var p = Parts(b, Full(40), 99, 40, tree);
        if (p.Count != 1 || p[0].Front.Length < 2 || Mathf.Abs(p[0].Front[^1].X - p[0].Front[0].X - 9f / 32f) > 0.01f)
            throw new Exception($"Footprint: 밑동만 잡혀야 한다 ({p.Count})");
        // 아치: 양끝 8열씩만 땅에 닿는다
        var arch = new int[64];
        for (int x = 0; x < 64; x++)
            arch[x] = x < 8 || x >= 56 ? 99 : 30;
        if (Parts(arch, Full(64), 99, 64, tree).Count != 2)
            throw new Exception("Footprint: 아치 기둥 둘");
        // 울타리(건물 종류): 기둥 사이 20px 틈은 이어서 한 조각
        var fence = new PropKind { Id = "f", Footprint = new Vector2(1.5f, 0.3f) };
        var fb = new int[48];
        for (int x = 0; x < 48; x++)
            fb[x] = x % 22 < 3 ? 47 : 20;
        if (Parts(fb, Full(48), 47, 48, fence).Count != 1)
            throw new Exception("Footprint: 울타리는 한 조각");
        // 도리이: 기둥(긴 열) 둘 + 그 사이 낮은 돌계단(짧은 열) → 기둥 둘만
        var steps = Full(64);
        for (int x = 8; x < 56; x++)
            steps[x] = 38;
        var tb = new int[64];
        System.Array.Fill(tb, 99);
        if (Parts(tb, steps, 99, 64, tree).Count != 2)
            throw new Exception("Footprint: 낮은 계단은 밟고 지나간다");
    }

    private static int[] Full(int w)
    {
        var r = new int[w];
        System.Array.Fill(r, 100);
        return r;
    }
}
