using Godot;

namespace PixelMmo.Runtime;

public static class RpgUi
{
    public static readonly Color Ink = new("201a16"), Wood = new("30251f"), Gold = new("bc9656"), Paper = new("eadbb6"), Muted = new("b8aa8d");
    public static StyleBoxFlat Frame(bool light = false, int margin = 16)
    {
        var box = new StyleBoxFlat { BgColor = light ? new Color("40332a") : new Color(.10f,.075f,.058f,.96f),
            BorderColor = Gold, ShadowColor = new Color(0,0,0,.45f), ShadowSize = 7,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin };
        box.SetBorderWidthAll(2); box.SetCornerRadiusAll(3);
        return box;
    }
    public static Label Text(string text, int size = 16, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",color ?? Paper);
        return label;
    }
    public static Button Button(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0,38), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.AddThemeStyleboxOverride("normal",Frame(false,10));
        button.AddThemeStyleboxOverride("hover",Frame(true,10));
        button.AddThemeStyleboxOverride("pressed",Frame(true,10));
        var focus=Frame(false,10); focus.BgColor=Colors.Transparent; focus.BorderColor=Paper;
        button.AddThemeStyleboxOverride("focus",focus);
        button.AddThemeColorOverride("font_color",Paper); button.AddThemeColorOverride("font_hover_color",Colors.White);
        button.AddThemeFontSizeOverride("font_size",15);
        return button;
    }
    public static ProgressBar Bar(Color color, float height = 20)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0,height), MouseFilter = Control.MouseFilterEnum.Ignore };
        var back=Frame(false,0); back.ShadowSize=0; back.BorderColor=new Color("706042"); back.SetBorderWidthAll(1);
        var fill=new StyleBoxFlat { BgColor=color }; fill.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("background",back); bar.AddThemeStyleboxOverride("fill",fill);
        return bar;
    }
}
