using Godot;

namespace PixelMmo.Render;

/// <summary>
/// 한글 픽셀 글자. 3x5 숫자로는 한글을 못 찍으니 윈도우 굴림의 12px 비트맵 글자를 안티앨리어싱 없이 쓴다.
/// 부르는 쪽이 DrawSetTransform 으로 픽셀 배율을 걸고 저해상도 좌표로 그린다 (TextureFilter = Nearest).
/// ponytail: 시스템 글꼴 의존. 다른 OS 로 가면 OFL 픽셀 한글 글꼴(갈무리 등)을 넣는다.
/// </summary>
public static class PixelText
{
    public const int Size = 12;
    public static readonly Color Edge = new(0.08f, 0.06f, 0.1f);

    private static Font _font;
    public static Font Font => _font ??= new SystemFont
    {
        FontNames = new[] { "Gulim", "Dotum", "Malgun Gothic" },
        Antialiasing = TextServer.FontAntialiasing.None,
        SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
    };

    public static float Width(string s) => Font.GetStringSize(s, HorizontalAlignment.Left, -1, Size).X;

    /// <summary>pos = 왼쪽 기준선. 사방 1픽셀 테두리.</summary>
    public static void Draw(CanvasItem ci, string s, Vector2 pos, Color c, float alpha = 1f)
    {
        pos = pos.Round();
        var edge = new Color(Edge, alpha);
        foreach (var d in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
            ci.DrawString(Font, pos + d, s, HorizontalAlignment.Left, -1, Size, edge);
        ci.DrawString(Font, pos, s, HorizontalAlignment.Left, -1, Size, new Color(c, alpha));
    }

    public static void DrawCentered(CanvasItem ci, string s, Vector2 center, Color c, float alpha = 1f) =>
        Draw(ci, s, new Vector2(Mathf.Round(center.X - Width(s) * 0.5f), center.Y), c, alpha);
}
