using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// F3 튜닝 슬라이더. §C 의 모든 수치를 게임 안에서 실시간으로 조정한다.
/// 손맛 튜닝을 코드 재컴파일로 하면 안 되기 때문에 이게 있다. (§I)
/// </summary>
public partial class TuningPanel : CanvasLayer
{
    private static readonly int FontSize = 7;
    private static readonly int PanelWidth = 236;
    private static readonly Color PanelBg = new(0.04f, 0.05f, 0.07f, 0.88f);
    private static readonly Color GroupColor = new(1f, 0.85f, 0.45f);
    private static readonly Color ValueColor = new(0.85f, 0.92f, 0.85f);
    private static readonly Color ModifiedColor = new(0.55f, 1f, 0.65f);

    private VBoxContainer _rows;

    public override void _Ready()
    {
        Name = "TuningPanel";
        Layer = 11;
        Visible = false;
        Build();
    }

    public void Toggle()
    {
        Visible = !Visible;
        DebugFlags.ShowTuning = Visible;
    }

    private void Build()
    {
        var background = new ColorRect { Color = PanelBg };
        background.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        background.OffsetLeft = -PanelWidth;
        AddChild(background);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        scroll.OffsetLeft = -PanelWidth;
        scroll.OffsetTop = 2;
        scroll.OffsetBottom = -2;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        AddChild(scroll);

        _rows = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidth - 12, 0) };
        _rows.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_rows);

        AddHeader("F3 — 손맛 튜닝");

        string group = null;
        foreach (var entry in CombatTuning.All)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                AddHeader(group);
            }
            AddRow(entry);
        }

        var reset = new Button { Text = "전부 기본값으로", CustomMinimumSize = new Vector2(0, 14) };
        reset.AddThemeFontSizeOverride("font_size", FontSize);
        reset.Pressed += ResetAll;
        _rows.AddChild(reset);
    }

    private void AddHeader(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", GroupColor);
        _rows.AddChild(label);
    }

    private void AddRow(TuningEntry entry)
    {
        var caption = new Label();
        caption.AddThemeFontSizeOverride("font_size", FontSize);
        caption.AddThemeColorOverride("font_color", ValueColor);
        _rows.AddChild(caption);

        var slider = new HSlider
        {
            MinValue = entry.Min,
            MaxValue = entry.Max,
            Step = entry.Step,
            Value = entry.Value,
            CustomMinimumSize = new Vector2(0, 10),
        };
        _rows.AddChild(slider);

        void Sync()
        {
            caption.Text = $"  {entry.Describe()}";
            caption.AddThemeColorOverride("font_color", entry.IsModified ? ModifiedColor : ValueColor);
        }

        slider.ValueChanged += value =>
        {
            entry.Value = (float)value;
            Sync();
        };

        // Reset 이 값을 되돌렸을 때 슬라이더도 따라오게 한다.
        _resetHandlers.Add(() =>
        {
            slider.SetValueNoSignal(entry.Value);
            Sync();
        });

        Sync();
    }

    private readonly System.Collections.Generic.List<System.Action> _resetHandlers = new();

    private void ResetAll()
    {
        CombatTuning.ResetAll();
        foreach (var handler in _resetHandlers)
            handler();
        DebugLog.Add("튜닝을 전부 기본값으로 되돌렸다");
    }
}
