using Godot;
using PixelMmo.Core;
using PixelMmo.Data;

namespace PixelMmo.Render;

/// <summary>
/// 모습 고르기 (M6). 처음 켤 때, 또는 C(패드 Back)로 연다.
/// 미리보기는 따로 만들지 않고 월드의 주인공 그대로 — 고르는 즉시 바뀌고 제자리에서 천천히 돈다.
/// ↑↓ 줄 고르기, ←→ 바꾸기, 공격(J)·Enter 확인, C·Esc 취소(처음 켤 때는 취소 대신 확인).
/// </summary>
public partial class CharacterCreator : Control
{
    private IPlayerContext _player;
    private int _row;                 // 0 체형, 1 옷 색, 2 확인
    private int _base, _accent;
    private string _prevBase;
    private int _prevAccent;
    private bool _first;              // 처음 켬: 닫으면 그대로 확정
    private float _age;

    private const int Rows = 3;
    private static readonly Rect2 Panel = new(18, 96, 176, 132);   // 저해상도 좌표 (640x360)
    private static readonly Color Back = new(0.06f, 0.05f, 0.09f, 0.86f);
    private static readonly Color Frame = new(0.95f, 0.86f, 0.62f);
    private static readonly Color Dim = new(0.72f, 0.68f, 0.62f);
    private static readonly Color Pick = new(1f, 0.93f, 0.7f);

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Name = "CharacterCreator";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    public void Bind(IPlayerContext player) => _player = player;

    public void Open(bool first = false)
    {
        var look = AppearanceDef.Instance;
        _prevBase = _player.LookBase;
        _prevAccent = _player.LookAccent;
        _base = Mathf.Max(0, look.Bases.FindIndex(b => b.Id == _prevBase));
        _accent = _prevAccent;
        _row = 0;
        _first = first;
        _age = 0f;
        Visible = true;
        GameRoot.Instance.MenuOpen = true;
        Sfx.Play("select", -8f, 1f, 0f, "UI");
    }

    private void Close(bool keep)
    {
        if (!keep)
            _player.SetLook(_prevBase, _prevAccent);
        else
            _player.Save();
        Visible = false;
        GameRoot.Instance.MenuOpen = false;
        Sfx.Play(keep ? "confirmation" : "click", -6f, 1f, 0f, "UI");
    }

    public override void _Process(double delta)
    {
        if (_player == null)
            return;
        if (!Visible)
        {
            if (Input.IsActionJustPressed(Controls.Look) && _player.IsAlive)
                Open();
            return;
        }
        _age += (float)delta;
        var look = AppearanceDef.Instance;
        if (Input.IsActionJustPressed(Controls.Up))
            Move(ref _row, -1, Rows);
        if (Input.IsActionJustPressed(Controls.Down))
            Move(ref _row, 1, Rows);
        int step = Input.IsActionJustPressed(Controls.Left) ? -1 : Input.IsActionJustPressed(Controls.Right) ? 1 : 0;
        if (step != 0 && _row == 0)
            Move(ref _base, step, look.Bases.Count);
        if (step != 0 && _row == 1)
            Move(ref _accent, step, look.Accents.Count);
        if (step != 0 && _row < 2)
            _player.SetLook(look.Bases[_base].Id, _accent);

        bool accept = Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed("ui_accept");
        if (accept && _row == 2)
            Close(keep: true);
        else if (accept)
            Move(ref _row, 1, Rows);
        else if (Input.IsActionJustPressed(Controls.Look) || Input.IsActionJustPressed("ui_cancel"))
            Close(keep: _first);
        QueueRedraw();
    }

    private static void Move(ref int value, int step, int count)
    {
        value = (int)Mathf.PosMod(value + step, count);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    public override void _Draw()
    {
        if (!Visible)
            return;
        var look = AppearanceDef.Instance;
        float px = GameRoot.Instance.View.PixelScale;
        // 톡 떨어지며 나타난다
        float drop = Mathf.Round(8f * (1f - Mathf.Min(_age / 0.18f, 1f)));
        DrawSetTransform(new Vector2(0f, -drop * px), 0f, Vector2.One * px);

        DrawRect(Panel, Back);
        DrawRect(Panel, Frame, false, 1f);
        DrawRect(new Rect2(Panel.Position + new Vector2(2, 2), Panel.Size - new Vector2(4, 4)), new Color(Frame, 0.35f), false, 1f);
        var x = Panel.Position.X;
        var y = Panel.Position.Y;
        PixelText.DrawCentered(this, "모습 고르기", new Vector2(x + Panel.Size.X * 0.5f, y + 18), Frame);

        var accent = look.Accents[_accent];
        Row(0, y + 44, "체형", look.Bases[_base].Name, null);
        Row(1, y + 66, "옷 색", accent.Name, accent.IsIdentity ? null : Swatch(accent));

        // 확인 단추
        var button = new Rect2(x + 48, y + 80, 80, 18);
        bool on = _row == 2;
        DrawRect(button, on ? new Color(Pick, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
        DrawRect(button, on ? Colors.White : Dim, false, 1f);
        PixelText.DrawCentered(this, "확인", new Vector2(button.GetCenter().X, button.Position.Y + 13), on ? PixelText.Edge : Dim);

        PixelText.Draw(this, "↑↓ 고르기  ←→ 바꾸기", new Vector2(x + 12, y + 118), new Color(Dim, 0.9f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>한 줄: 이름, 그리고 ◀ 값 ▶. 고른 줄은 밝은 띠.</summary>
    private void Row(int index, float baseline, string label, string value, Color? swatch)
    {
        float x = Panel.Position.X;
        bool on = _row == index;
        if (on)
            DrawRect(new Rect2(x + 4, baseline - 12, Panel.Size.X - 8, 16), new Color(Pick, 0.16f));
        PixelText.Draw(this, label, new Vector2(x + 12, baseline), on ? Pick : Dim);
        float center = x + 118;
        PixelText.DrawCentered(this, value, new Vector2(center, baseline), on ? Colors.White : Dim);
        if (on)
        {
            // 좌우로 살짝 숨 쉬는 화살표
            float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 1f;
            PixelText.Draw(this, "◀", new Vector2(center - 40 - bob, baseline), Pick);
            PixelText.Draw(this, "▶", new Vector2(center + 30 + bob, baseline), Pick);
        }
        if (swatch is Color c)
        {
            var r = new Rect2(x + 12 + PixelText.Width(label) + 5, baseline - 8, 7, 7);
            DrawRect(r, c);
            DrawRect(r, PixelText.Edge, false, 1f);
        }
    }

    /// <summary>옷 색 견본: 원래 옷 색(바탕 범위 가운데)에 그 색 규칙을 건 색.</summary>
    private Color Swatch(AppearanceDef.AccentDef a)
    {
        var b = AppearanceDef.Instance.Bases[_base];
        float hue = a.Hue ?? (b.AccentHue is { Length: 2 } ? (b.AccentHue[0] + b.AccentHue[1]) * 0.5f : 0f);
        return Color.FromHsv(hue / 360f, Mathf.Clamp(0.55f * a.Sat, 0f, 1f), Mathf.Clamp(0.7f * a.Val, 0f, 1f));
    }
}
