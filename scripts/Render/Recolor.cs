using System;
using Godot;
using PixelMmo.Data;

namespace PixelMmo.Render;

/// <summary>
/// 옷 색 바꾸기: 시트를 불러올 때 한 번, 옷 픽셀(색상 범위 + 최소 채도)의 색상만 옮긴다.
/// 명도는 그대로라 원화의 음영·외곽선 단계가 살아 있다. 판정은 tools/palette.py in_accent 와 같다.
/// 셰이더 대신 불러올 때 하는 까닭: 애니메이션 시트마다 조금씩 다른 음영 색이 섞여 있어 색 목록으로는 다 못 잡는다.
/// </summary>
public static class Recolor
{
    /// <summary>바꿀 것이 없으면 null (원본 텍스처를 그대로 쓴다).</summary>
    public static Func<Image, Image> For(AppearanceDef.BaseDef b, AppearanceDef.AccentDef a)
    {
        if (a == null || a.IsIdentity || b.AccentHue is not { Length: 2 })
            return null;
        float h0 = b.AccentHue[0], h1 = b.AccentHue[1], smin = b.AccentSat, vmin = b.AccentVal;
        float center = h0 <= h1 ? (h0 + h1) * 0.5f : Mathf.PosMod((h0 + h1 + 360f) * 0.5f, 360f);
        return img => Apply(img, h0, h1, smin, vmin, center, a);
    }

    private static Image Apply(Image img, float h0, float h1, float smin, float vmin, float center, AppearanceDef.AccentDef a)
    {
        img.Convert(Image.Format.Rgba8);
        byte[] px = img.GetData();
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] < 128)
                continue;
            var c = new Color(px[i] / 255f, px[i + 1] / 255f, px[i + 2] / 255f);
            float h = c.H * 360f;
            bool inside = c.S >= smin && c.V >= vmin && (h0 <= h1 ? h >= h0 && h <= h1 : h >= h0 || h <= h1);
            if (!inside)
                continue;
            // 원래 범위 안의 색상 차이는 살린다 (한 옷 안의 미묘한 색 흔들림)
            float nh = a.Hue is float target ? Mathf.PosMod(target + Mathf.Wrap(h - center, -180f, 180f), 360f) : h;
            var n = Color.FromHsv(nh / 360f, Mathf.Clamp(Mathf.Max(c.S * a.Sat, a.MinSat), 0f, 1f), Mathf.Clamp(c.V * a.Val, 0f, 1f));
            px[i] = (byte)Mathf.RoundToInt(n.R * 255f);
            px[i + 1] = (byte)Mathf.RoundToInt(n.G * 255f);
            px[i + 2] = (byte)Mathf.RoundToInt(n.B * 255f);
        }
        return Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, px);
    }
}
