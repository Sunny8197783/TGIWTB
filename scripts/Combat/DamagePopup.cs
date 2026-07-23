using Godot;

namespace PixelMmo.Combat;

/// <summary>데미지 숫자. 위로 PopupRise 만큼 올라가며 PopupLife 동안 페이드. (§C-6)</summary>
public partial class DamagePopup : Node2D
{
    private static readonly int FontSize = 8;
    private static readonly float Width = 40f;
    private static readonly Color NormalColor = new(1f, 0.95f, 0.75f);
    private static readonly Color HeavyColor = new(1f, 0.72f, 0.35f);
    private static readonly Color PlayerHurtColor = new(1f, 0.45f, 0.45f);

    private Label _label;
    private Vector2 _origin;
    private float _life;
    private float _rise;
    private float _elapsed;
    private bool _started;

    public void Setup(float amount, bool heavy, bool onPlayer)
    {
        _life = CombatTuning.PopupLife;
        _rise = CombatTuning.PopupRise;

        _label = new Label
        {
            Text = Mathf.RoundToInt(amount).ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Size = new Vector2(Width, FontSize * 2f),
            Position = new Vector2(-Width * 0.5f, -FontSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", heavy ? FontSize + 2 : FontSize);
        _label.AddThemeColorOverride("font_color",
            onPlayer ? PlayerHurtColor : heavy ? HeavyColor : NormalColor);
        AddChild(_label);
    }

    public override void _Ready()
    {
        ZIndex = 100;
    }

    public override void _Process(double delta)
    {
        // 시작 위치는 첫 프레임에 잡는다 — 생성 직후 좌표가 정해지는 순서에 의존하지 않으려고.
        if (!_started)
        {
            _started = true;
            _origin = Position;
        }

        _elapsed += (float)delta;
        float t = _life > 0f ? Mathf.Clamp(_elapsed / _life, 0f, 1f) : 1f;

        // ease-out — 처음에 빠르게 솟았다가 천천히 멈춘다.
        float eased = 1f - (1f - t) * (1f - t);
        Position = _origin + Vector2.Up * (_rise * eased);
        Modulate = new Color(1f, 1f, 1f, 1f - t);

        if (t >= 1f)
            QueueFree();
    }
}
