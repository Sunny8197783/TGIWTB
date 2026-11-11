using System.Collections.Generic;
using Godot;

namespace PixelMmo.Render;

/// <summary>
/// 화면 차림의 공통 부품 — 판·단추·막대·키 딱지. 모든 창이 같은 손으로 그린 것처럼 보이게 여기 것만 쓴다.
/// 좌표는 저해상도(640x360) 기준: Begin 으로 픽셀 배율을 걸고 그린 뒤 End. 글자는 PixelText.
/// 색: 먹빛 판 위에 바랜 금빛 테두리 (세계수의 잎에 적힌 이름 — 양피지와 금박).
/// </summary>
public static class Ui
{
    public static readonly Color Ink = new(0.05f, 0.04f, 0.08f);
    public static readonly Color Panel = new(0.08f, 0.07f, 0.12f, 0.93f);
    public static readonly Color PanelLight = new(0.16f, 0.14f, 0.22f, 0.95f);
    public static readonly Color Gold = new(0.96f, 0.86f, 0.6f);
    public static readonly Color GoldDim = new(0.6f, 0.52f, 0.36f);
    public static readonly Color Text = new(0.96f, 0.94f, 0.88f);
    public static readonly Color Dim = new(0.68f, 0.64f, 0.6f);
    public static readonly Color Faint = new(0.42f, 0.4f, 0.42f);
    public static readonly Color Red = new(0.92f, 0.32f, 0.3f);
    public static readonly Color Green = new(0.55f, 0.85f, 0.45f);
    public static readonly Color Blue = new(0.5f, 0.78f, 1f);
    public static readonly Color HushGrey = new(0.8f, 0.82f, 0.9f);

    public static void Begin(CanvasItem ci, float px, Vector2 offset = default) =>
        ci.DrawSetTransform(offset * px, 0f, Vector2.One * px);

    public static void End(CanvasItem ci) => ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

    public static Vector2 LowSize(Control c, float px) => c.GetViewportRect().Size / px;

    /// <summary>판: 먹빛 바탕 + 금빛 한 줄 + 안쪽 옅은 줄, 네 귀에 작은 금 못</summary>
    public static void Frame(CanvasItem ci, Rect2 r, Color? accent = null, float alpha = 1f)
    {
        Color gold = accent ?? Gold;
        r = new Rect2(r.Position.Round(), r.Size.Round());
        ci.DrawRect(new Rect2(r.Position - Vector2.One, r.Size + Vector2.One * 2), new Color(Ink, alpha));
        ci.DrawRect(r, new Color(Panel, Panel.A * alpha));
        ci.DrawRect(r, new Color(gold, alpha), false, 1f);
        ci.DrawRect(new Rect2(r.Position + new Vector2(2, 2), r.Size - new Vector2(4, 4)), new Color(gold, 0.22f * alpha), false, 1f);
        foreach (var c in new[] { r.Position, new Vector2(r.End.X - 3, r.Position.Y), new Vector2(r.Position.X, r.End.Y - 3), r.End - new Vector2(3, 3) })
        {
            ci.DrawRect(new Rect2(c, new Vector2(3, 3)), new Color(Ink, alpha));
            ci.DrawRect(new Rect2(c + Vector2.One, Vector2.One), new Color(gold, alpha));
        }
    }

    /// <summary>판 머리의 제목 띠</summary>
    public static void Title(CanvasItem ci, Rect2 panel, string title, Color? accent = null)
    {
        Color gold = accent ?? Gold;
        float w = PixelText.Width(title) + 26f;
        var tag = new Rect2(Mathf.Round(panel.GetCenter().X - w * 0.5f), panel.Position.Y - 8, Mathf.Round(w), 17);
        ci.DrawRect(new Rect2(tag.Position - Vector2.One, tag.Size + Vector2.One * 2), Ink);
        ci.DrawRect(tag, PanelLight);
        ci.DrawRect(tag, gold, false, 1f);
        PixelText.DrawCentered(ci, title, new Vector2(tag.GetCenter().X, tag.Position.Y + 13), gold);
    }

    public static void Button(CanvasItem ci, Rect2 r, string label, bool on, bool disabled = false)
    {
        r = new Rect2(r.Position.Round(), r.Size.Round());
        ci.DrawRect(new Rect2(r.Position - Vector2.One, r.Size + Vector2.One * 2), Ink);
        ci.DrawRect(r, on ? new Color(Gold, 0.92f) : new Color(1f, 1f, 1f, 0.06f));
        ci.DrawRect(r, on ? Colors.White : disabled ? Faint : GoldDim, false, 1f);
        PixelText.DrawCentered(ci, label, new Vector2(r.GetCenter().X, r.Position.Y + Mathf.Round(r.Size.Y * 0.5f) + 4),
            on ? Ink : disabled ? Faint : Text);
    }

    /// <summary>고른 줄의 밝은 띠</summary>
    public static void Highlight(CanvasItem ci, Rect2 r) => ci.DrawRect(r, new Color(Gold, 0.16f));

    /// <summary>키 딱지 (F · Space). 그린 너비를 돌려준다</summary>
    public static float KeyCap(CanvasItem ci, Vector2 pos, string key, float alpha = 1f)
    {
        float w = Mathf.Max(13f, PixelText.Width(key) + 7f);
        var r = new Rect2(pos.Round() + new Vector2(0, -11), new Vector2(Mathf.Round(w), 14));
        ci.DrawRect(new Rect2(r.Position - Vector2.One, r.Size + new Vector2(2, 3)), new Color(Ink, alpha));
        ci.DrawRect(r, new Color(0.9f, 0.88f, 0.82f, alpha));
        ci.DrawRect(new Rect2(r.Position + new Vector2(0, r.Size.Y - 2), new Vector2(r.Size.X, 2)), new Color(0.6f, 0.58f, 0.55f, alpha));
        PixelText.DrawPlain(ci, key, new Vector2(r.Position.X + Mathf.Round((w - PixelText.Width(key)) * 0.5f), pos.Y), new Color(Ink, alpha));
        return r.Size.X;
    }

    /// <summary>아래쪽 조작 안내: [키] 설명 [키] 설명 … 가운데 x 를 기준으로 가운데 맞춤</summary>
    public static void Hints(CanvasItem ci, Vector2 center, params (string key, string label)[] items)
    {
        float total = 0f;
        foreach (var (key, label) in items)
            total += Mathf.Max(13f, PixelText.Width(key) + 7f) + 4f + PixelText.Width(label) + 12f;
        float x = Mathf.Round(center.X - (total - 12f) * 0.5f);
        foreach (var (key, label) in items)
        {
            x += KeyCap(ci, new Vector2(x, center.Y), key) + 4f;
            PixelText.Draw(ci, label, new Vector2(x, center.Y), Dim);
            x += PixelText.Width(label) + 12f;
        }
    }

    /// <summary>막대: 테두리 + 바탕 + 채움(위쪽 밝은 줄). chip ≥ 0 이면 늦게 따라오는 잔상</summary>
    public static void Bar(CanvasItem ci, Rect2 r, float ratio, Color hi, Color lo, float chip = -1f)
    {
        r = new Rect2(r.Position.Round(), r.Size.Round());
        ci.DrawRect(new Rect2(r.Position - Vector2.One, r.Size + Vector2.One * 2), Ink);
        ci.DrawRect(r, new Color(0.2f, 0.16f, 0.2f));
        int w = (int)r.Size.X, h = (int)r.Size.Y;
        int fill = Mathf.RoundToInt(w * Mathf.Clamp(ratio, 0f, 1f));
        int chipW = Mathf.RoundToInt(w * Mathf.Clamp(chip, 0f, 1f));
        if (chipW > fill)
            ci.DrawRect(new Rect2(r.Position + new Vector2(fill, 0), new Vector2(chipW - fill, h)), new Color(1f, 0.93f, 0.8f));
        if (fill <= 0)
            return;
        ci.DrawRect(new Rect2(r.Position, new Vector2(fill, h)), lo);
        ci.DrawRect(new Rect2(r.Position, new Vector2(fill, Mathf.Max(1, h / 2))), hi);
        if (h >= 4)
            ci.DrawRect(new Rect2(r.Position, new Vector2(fill, 1)), hi.Lerp(Colors.White, 0.45f));
    }

    /// <summary>낱말 단위로 줄을 접는다</summary>
    public static List<string> Wrap(string s, float width)
    {
        var rows = new List<string>();
        foreach (string para in (s ?? "").Split('\n'))
        {
            string cur = "";
            foreach (string word in para.Split(' '))
            {
                string tryLine = cur.Length == 0 ? word : cur + " " + word;
                if (PixelText.Width(tryLine) > width && cur.Length > 0)
                {
                    rows.Add(cur);
                    cur = word;
                }
                else
                    cur = tryLine;
            }
            rows.Add(cur);
        }
        return rows;
    }

    /// <summary>이름의 한 글자를 새긴 문장(紋章): 마름모 판 위에 글자</summary>
    public static void Emblem(CanvasItem ci, Vector2 center, string glyph, Color color, bool lit = true)
    {
        center = center.Round();
        for (int y = -9; y <= 9; y++)
        {
            int half = 9 - Mathf.Abs(y);
            ci.DrawRect(new Rect2(center + new Vector2(-half - 1, y), new Vector2(half * 2 + 2, 1)), Ink);
        }
        for (int y = -8; y <= 8; y++)
        {
            int half = 8 - Mathf.Abs(y);
            ci.DrawRect(new Rect2(center + new Vector2(-half, y), new Vector2(half * 2, 1)),
                lit ? color.Darkened(y < 0 ? 0.45f : 0.6f) : new Color(0.16f, 0.15f, 0.2f));
        }
        PixelText.DrawCentered(ci, glyph, center + new Vector2(0, 5), lit ? color.Lightened(0.5f) : Faint);
    }
}
