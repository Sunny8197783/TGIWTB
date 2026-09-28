using Godot;

namespace PixelMmo.World;

/// <summary>
/// 그림이 오기 전 자리 표시. 크기와 색만 맞으면 배치·구도 검토에는 충분하다.
/// 실제 그림(art/env)이 생기면 자동으로 그쪽을 쓴다.
/// </summary>
public static class Placeholder
{
    public static Image Make(PropKind k)
    {
        int w = k.Size.X, h = k.Size.Y;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        Color c = k.Color, dark = c.Darkened(0.35f), light = c.Lightened(0.25f);
        var trunk = new Color(0.42f, 0.28f, 0.18f);

        switch (k.Placeholder)
        {
            case PropKind.Shape.Tree:
                Rect(img, w / 2 - 5, h - 44, 10, 42, trunk);
                Blob(img, w / 2, h - 44 - (w / 2 - 12), w / 2 - 8, (w / 2 - 12), c, light, dark);
                break;
            case PropKind.Shape.Pine:
                Rect(img, w / 2 - 4, h - 22, 8, 20, trunk);
                for (int y = 4; y < h - 20; y++)
                {
                    float t = (y - 4f) / (h - 24f);
                    int half = (int)(4 + t * (w / 2f - 8)) - (y % 18 < 3 ? 3 : 0);
                    for (int x = w / 2 - half; x <= w / 2 + half; x++)
                        Put(img, x, y, x < w / 2 - half / 3 ? light : x > w / 2 + half / 2 ? dark : c);
                }
                break;
            case PropKind.Shape.Bush:
                Blob(img, w / 2, h - h / 2, w / 2 - 4, h / 2 - 4, c, light, dark);
                break;
            case PropKind.Shape.Flowers:
                for (int i = 0; i < 9; i++)
                {
                    int x = 4 + (i * 7919 % (w - 8)), y = h - 4 - (i * 104729 % (h - 8));
                    Rect(img, x, y, 2, h - y - 1, new Color(0.3f, 0.55f, 0.25f));
                    Rect(img, x - 1, y - 1, 4, 3, i % 3 == 0 ? light : c);
                }
                break;
            case PropKind.Shape.Rock:
                Blob(img, w / 2, h - h / 2 + 2, w / 2 - 3, h / 2 - 4, c, light, dark);
                break;
            case PropKind.Shape.Lamp:
                Rect(img, w / 2 - 2, 20, 4, h - 22, dark);
                Rect(img, w / 2 - 7, 8, 14, 14, new Color(1f, 0.85f, 0.45f));
                Rect(img, w / 2 - 9, 4, 18, 5, c);
                Rect(img, w / 2 - 8, h - 6, 16, 5, c);
                break;
            case PropKind.Shape.House:
                int wall = h * 45 / 100;
                Rect(img, 10, h - wall, w - 20, wall - 1, new Color(0.93f, 0.88f, 0.76f));
                for (int y = 8; y < h - wall; y++)
                {
                    float t = (y - 8f) / (h - wall - 8f);
                    int half = (int)(8 + t * (w / 2f - 4));
                    for (int x = w / 2 - half; x < w / 2 + half; x++)
                        Put(img, x, y, (y / 6) % 2 == 0 ? c : dark);
                }
                Rect(img, w / 2 - 10, h - 40, 20, 39, new Color(0.45f, 0.3f, 0.2f));
                Rect(img, 24, h - wall + 14, 22, 18, new Color(0.35f, 0.55f, 0.8f));
                Rect(img, w - 46, h - wall + 14, 22, 18, new Color(0.35f, 0.55f, 0.8f));
                break;
            case PropKind.Shape.Tower:
                Rect(img, w / 2 - w / 5, 30, w * 2 / 5, h - 32, c);
                Rect(img, w / 2 - w / 5, 30, w / 10, h - 32, light);
                Rect(img, w / 2 - w / 4, 10, w / 2, 22, new Color(0.8f, 0.25f, 0.2f));
                break;
            case PropKind.Shape.Flat:
                Blob(img, w / 2, h / 2, w / 2 - 2, h / 2 - 2, c, light, dark);
                break;
            default:
                Rect(img, 4, h / 3, w - 8, h * 2 / 3 - 2, c);
                Rect(img, 4, h / 3, w - 8, 4, light);
                break;
        }
        return img;
    }

    private static void Put(Image img, int x, int y, Color c)
    {
        if (x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight())
            img.SetPixel(x, y, c);
    }

    private static void Rect(Image img, int x0, int y0, int w, int h, Color c)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(img.GetHeight(), y0 + h); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(img.GetWidth(), x0 + w); x++)
                Put(img, x, y, c);
    }

    /// <summary>뭉게뭉게한 덩어리 — 원 여러 개를 겹치고 좌상단을 밝게.</summary>
    private static void Blob(Image img, int cx, int cy, int rx, int ry, Color c, Color light, Color dark)
    {
        var outline = c.Darkened(0.6f);
        for (int y = cy - ry - 2; y <= cy + ry + 2; y++)
        {
            for (int x = cx - rx - 2; x <= cx + rx + 2; x++)
            {
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight())
                    continue;
                float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
                float bump = 0.12f * Mathf.Sin(Mathf.Atan2(dy, dx) * 7f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - bump;
                if (d > 1.08f) continue;
                Color col = d > 0.96f ? outline : (dx + dy < -0.5f ? light : dx + dy > 0.6f ? dark : c);
                Put(img, x, y, col);
            }
        }
    }
}
