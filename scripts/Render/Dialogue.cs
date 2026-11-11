using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 대화창 하나를 모든 곳이 같이 쓴다 (마을 사람·나래·이야기 연출·이름 짓기).
///  - 화면 아래 판에 한 글자씩 찍힌다. F·공격으로 넘기고(찍는 중이면 다 찍기), 마지막 줄에서 닫힌다.
///  - 말하는 이의 원화 윗몸이 왼쪽에 초상으로 선다. 말하는 이가 없으면(서술) 가운데 맞춘 옅은 글.
///  - 고르기: 마지막 줄 뒤에 ↑↓ 로 고른다. 이름 짓기: 글자를 직접 넣는다 (한글 입력).
/// 떠 있는 동안 주인공은 멈춘다 (GameRoot.MenuOpen).
/// </summary>
public partial class Dialogue : Control
{
    public readonly record struct Line(string Speaker, string Title, string Text, string Art = null);

    public static Dialogue Instance { get; private set; }
    public static bool Open => Instance != null && Instance._lines != null;

    private const float CharsPerSecond = 42f;
    private const int MaxName = 8;

    private List<Line> _lines;
    private int _index;
    private float _shown;
    /// <summary>개발용 자동 넘김 (초). 0 = 끔</summary>
    public static float AutoAdvance { get; set; }
    private float _autoWait;
    private float _age;
    private string[] _choices;
    private int _pick;
    private Action<int> _picked;
    private Action _done;
    private LineEdit _input;
    private Action<string> _named;
    private readonly Dictionary<string, Texture2D> _portraits = new();

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Name = "Dialogue";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>대화를 띄운다. done = 닫힌 뒤. choices 가 있으면 마지막 줄에서 고르고 picked(번호) 를 부른다</summary>
    public static void Show(IEnumerable<Line> lines, Action done = null, string[] choices = null, Action<int> picked = null)
    {
        var d = Instance;
        if (d == null)
        {
            done?.Invoke();
            return;
        }
        d._lines = new List<Line>(lines);
        if (d._lines.Count == 0)
        {
            d._lines = null;
            done?.Invoke();
            return;
        }
        d._index = 0;
        d._shown = 0f;
        d._age = 0f;
        d._choices = choices;
        d._pick = 0;
        d._picked = picked;
        d._done = done;
        GameRoot.Instance.MenuOpen = true;
        Sfx.Play("select", -8f, 0.9f, 0f, "UI");
    }

    /// <summary>이름 짓기: 한 줄 물음 + 글자 넣기. 빈 채로 확인하면 fallback</summary>
    public static void AskName(Line prompt, string fallback, Action<string> named)
    {
        Show(new[] { prompt });
        var d = Instance;
        if (d == null)
        {
            named(fallback);
            return;
        }
        d._named = named;
        d._input = new LineEdit
        {
            MaxLength = MaxName, PlaceholderText = fallback, Alignment = HorizontalAlignment.Center,
            CaretBlink = true, ContextMenuEnabled = false,
        };
        float px = GameRoot.Instance.View.PixelScale;
        d._input.AddThemeFontOverride("font", PixelText.Font);
        d._input.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(PixelText.Size * px));
        d._input.AddThemeColorOverride("font_color", Ui.Text);
        var box = new StyleBoxFlat { BgColor = Ui.Ink, BorderColor = Ui.Gold };
        box.SetBorderWidthAll(Mathf.RoundToInt(px));
        box.SetContentMarginAll(4f * px);
        d._input.AddThemeStyleboxOverride("normal", box);
        d._input.AddThemeStyleboxOverride("focus", box);
        d.AddChild(d._input);
        Vector2 screen = d.GetViewportRect().Size;
        var size = new Vector2(150f, 22f) * px;
        d._input.Size = size;
        d._input.Position = new Vector2((screen.X - size.X) * 0.5f, screen.Y - 178f * px);
        d._input.GrabFocus();
        d._input.TextSubmitted += d.SubmitName;
    }

    private void SubmitName(string text)
    {
        string name = string.IsNullOrWhiteSpace(text) ? _input.PlaceholderText : text.Trim();
        var cb = _named;
        _named = null;
        _input.QueueFree();
        _input = null;
        Close();
        Sfx.Play("confirmation", -6f, 1f, 0f, "UI");
        cb?.Invoke(name);
    }

    private void Close()
    {
        var done = _done;
        _lines = null;
        _done = null;
        _choices = null;
        GameRoot.Instance.MenuOpen = false;
        QueueRedraw();
        done?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (_lines == null)
            return;
        float dt = (float)delta;
        _age += dt;
        string text = _lines[_index].Text ?? "";
        float before = _shown;
        _shown = Mathf.Min(text.Length, _shown + CharsPerSecond * dt);
        if ((int)_shown != (int)before && (int)_shown % 3 == 0 && (int)_shown > 0 && text[(int)_shown - 1] != ' ')
            Sfx.Play("click", -21f, _lines[_index].Speaker == null ? 1.1f : 1.6f, 0.15f, "UI");
        QueueRedraw();
        if (_input != null)
            return;   // 이름을 넣는 중 — Enter 가 끝낸다
        bool done = _shown >= text.Length;
        bool last = _index == _lines.Count - 1;
        if (_choices != null && last && done)
        {
            if (Input.IsActionJustPressed(Controls.Up))
                Step(-1);
            if (Input.IsActionJustPressed(Controls.Down))
                Step(1);
        }
        bool next = Input.IsActionJustPressed(Controls.Interact) || Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed("ui_accept");
        // 개발: --autotalk=초 — 줄이 다 찍히고 그만큼 지나면 저절로 넘긴다 (연출을 캡처로 끝까지 돌려 볼 때)
        if (AutoAdvance > 0f && done && _choices == null)
        {
            _autoWait += dt;
            if (_autoWait >= AutoAdvance)
            {
                _autoWait = 0f;
                next = true;
            }
        }
        if (!next || _age < 0.12f)
            return;
        if (!done)
        {
            _shown = text.Length;
            return;
        }
        if (!last)
        {
            _index++;
            _shown = 0f;
            _age = 0.2f;
            return;
        }
        var picked = _picked;
        int pick = _pick;
        bool hadChoices = _choices != null;
        Close();
        if (hadChoices)
            picked?.Invoke(pick);
    }

    private void Step(int d)
    {
        _pick = (int)Mathf.PosMod(_pick + d, _choices.Length);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    private Texture2D Portrait(string art)
    {
        if (art == null)
            return null;
        if (!_portraits.TryGetValue(art, out var tex))
            _portraits[art] = tex = ResourceLoader.Exists(art + "/rot.png") ? GD.Load<Texture2D>(art + "/rot.png") : null;
        return tex;
    }

    public override void _Draw()
    {
        if (_lines == null)
            return;
        float px = GameRoot.Instance.View.PixelScale;
        Vector2 low = Ui.LowSize(this, px);
        var line = _lines[_index];
        float rise = Mathf.Round(6f * (1f - Mathf.Min(_age / 0.15f, 1f)));
        Ui.Begin(this, px);
        var box = new Rect2(Mathf.Round(low.X * 0.5f - 250), low.Y - 104 + rise, 500, 84);
        bool narration = line.Speaker == null;
        Ui.Frame(this, box, narration ? Ui.HushGrey : Ui.Gold);
        float textX = box.Position.X + 16;
        float textW = box.Size.X - 32;
        var portrait = Portrait(line.Art);
        if (portrait != null)
        {
            // 원화(남쪽을 본 칸)의 윗몸 64x64 를 그대로 — 1텍셀 = 1픽셀
            var pr = new Rect2(box.Position + new Vector2(10, 10), new Vector2(64, 64));
            DrawRect(pr, new Color(0.14f, 0.13f, 0.2f));
            int cell = portrait.GetHeight() / 8;
            DrawTextureRectRegion(portrait, pr, new Rect2(cell * 0.5f - 32, cell * 0.36f, 64, 64));
            DrawRect(pr, Ui.GoldDim, false, 1f);
            textX += 74;
            textW -= 74;
        }
        if (!narration)
        {
            string name = string.IsNullOrEmpty(line.Title) ? line.Speaker : $"{line.Speaker}  ·  {line.Title}";
            var tag = new Rect2(box.Position + new Vector2(10, -10), new Vector2(PixelText.Width(name) + 16, 17));
            DrawRect(new Rect2(tag.Position - Vector2.One, tag.Size + Vector2.One * 2), Ui.Ink);
            DrawRect(tag, Ui.PanelLight);
            DrawRect(tag, Ui.Gold, false, 1f);
            PixelText.Draw(this, name, tag.Position + new Vector2(8, 13), Ui.Gold);
        }
        string text = (line.Text ?? "")[..(int)_shown];
        var rows = Ui.Wrap(text, textW);
        for (int i = 0; i < rows.Count && i < 4; i++)
        {
            var pos = new Vector2(textX, box.Position.Y + 26 + i * 15);
            if (narration)
                PixelText.DrawCentered(this, rows[i], new Vector2(box.GetCenter().X, pos.Y + 6), Ui.HushGrey);
            else
                PixelText.Draw(this, rows[i], pos, Ui.Text);
        }
        bool done = _shown >= (line.Text ?? "").Length;
        bool last = _index == _lines.Count - 1;
        if (_input != null)
            Ui.Hints(this, new Vector2(box.GetCenter().X, box.Position.Y - 46), ("Enter", "이 이름으로"));
        else if (_choices != null && last && done)
        {
            float w = 0f;
            foreach (string c in _choices)
                w = Mathf.Max(w, PixelText.Width(c) + 28f);
            for (int i = 0; i < _choices.Length; i++)
            {
                var r = new Rect2(box.End.X - w - 8, box.Position.Y - 8 - (_choices.Length - i) * 21, w, 18);
                Ui.Button(this, r, _choices[i], _pick == i);
            }
        }
        else if (done)
        {
            // 다음 줄 표시 ▼ (숨 쉬듯)
            float bob = Mathf.PosMod(_age, 0.7f) < 0.35f ? 0f : 1f;
            PixelText.Draw(this, last ? "■" : "▼", box.End - new Vector2(18, 8 - bob), Ui.Gold);
        }
        Ui.End(this);
    }
}
