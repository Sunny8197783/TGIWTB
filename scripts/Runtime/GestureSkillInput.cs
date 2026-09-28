using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>Right-drag mana gestures use existing input actions, including job-specific skill resolution.</summary>
public partial class GestureSkillInput : CanvasLayer
{
    private readonly List<Vector2> _points = new();
    private Dictionary<string, string> _bindings;
    private Line2D _stroke;
    private Label _hint;
    private bool _drawing;
    private string _pressed;
    private float _releaseAfter;

    public override void _Ready()
    {
        _bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            FileAccess.GetFileAsString("res://data/controls/gestures.json"));
        foreach (var (gesture, action) in _bindings)
            if (gesture is not ("line" or "circle" or "zigzag") || !InputMap.HasAction(action))
                throw new System.InvalidOperationException("Invalid gesture binding: " + gesture);
        Layer = 20;
        _stroke = new Line2D { Width = 3f, DefaultColor = new Color(.4f, .93f, 1f),
            BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round };
        AddChild(_stroke);
        _hint = new Label { Text = "WASD 이동  ·  Space 회피  ·  F 대화  ·  P 캐릭터  ·  B 스킬",
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        _hint.AddThemeColorOverride("font_color",RpgUi.Muted);
        _hint.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hint.AddThemeConstantOverride("shadow_offset_x", 1); _hint.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_hint);
    }

    public override void _Process(double delta)
        => _hint.Position = new Vector2(16f, GetViewport().GetVisibleRect().Size.Y - 18f);

    public override void _PhysicsProcess(double delta)
    {
        if (_pressed == null) return;
        _releaseAfter -= (float)delta;
        if (_releaseAfter <= 0f) { Input.ActionRelease(_pressed); _pressed = null; }
    }

    public override void _ExitTree()
    {
        if (_pressed != null) Input.ActionRelease(_pressed);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (PlayerHud.Instance?.MenuOpen == true) { _drawing=false; _stroke.ClearPoints(); return; }
        if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Right)
        {
            _drawing = button.Pressed;
            if (_drawing)
            {
                _points.Clear(); _stroke.ClearPoints();
                _points.Add(button.Position); _stroke.AddPoint(button.Position);
            }
            else
            {
                string gesture = Recognize(_points);
                if (_bindings.TryGetValue(gesture, out string action))
                {
                    if (_pressed != null) Input.ActionRelease(_pressed);
                    _pressed = action; _releaseAfter = .06f; // ~4 physics frames at 60 fps.
                    Input.ActionPress(action);
                    _stroke.DefaultColor = new Color(.8f, 1f, .45f);
                }
                else _stroke.DefaultColor = new Color(1f, .55f, .4f);
                GetTree().CreateTimer(.35).Timeout += () =>
                { if (IsInstanceValid(_stroke) && !_drawing) { _stroke.ClearPoints(); _stroke.DefaultColor = new Color(.4f, .93f, 1f); } };
            }
            GetViewport().SetInputAsHandled();
        }
        else if (_drawing && input is InputEventMouseMotion motion && _points.Count < 256)
        {
            if (_points[^1].DistanceTo(motion.Position) >= 5f)
            { _points.Add(motion.Position); _stroke.AddPoint(motion.Position); }
            GetViewport().SetInputAsHandled();
        }
    }

    public static string Recognize(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 3) return "";
        float length = 0f;
        Vector2 min = points[0], max = points[0];
        int turns = 0, previousSign = 0;
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 step = points[i] - points[i - 1];
            length += step.Length(); min = min.Min(points[i]); max = max.Max(points[i]);
            if (Mathf.Abs(step.X) < 3f) continue;
            int sign = step.X > 0f ? 1 : -1;
            if (previousSign != 0 && sign != previousSign) turns++;
            previousSign = sign;
        }
        if (length < 60f) return "";
        float direct = points[0].DistanceTo(points[^1]);
        Vector2 size = max - min;
        if (direct / length > .91f) return "line";
        if (size.X > 30f && size.Y > 30f && direct < size.Length() * .28f
            && length > size.Length() * 1.8f && length < size.Length() * 3.4f) return "circle";
        // ponytail: three coarse gestures; resample templates if the gesture vocabulary grows.
        if (turns == 2 && size.X > 35f && size.Y > 35f && direct > size.Length() * .55f) return "zigzag";
        return "";
    }
}
