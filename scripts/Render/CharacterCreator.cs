using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;

namespace PixelMmo.Render;

/// <summary>
/// 모습 고르기 (M9 개편). 처음 켤 때, 또는 C(패드 Back)로 연다. 갈래가 둘:
///  · 기성 모습 — 미리 그려 둔 바탕 원화 + 옷 색. 바로 바뀐다 (미리보기는 월드의 주인공이 제자리에서 돈다).
///  · 빚기 — 체형·머리·얼굴·옷·장신구를 하나하나 고르고 자유 묘사를 더해, 세상에 하나뿐인 원화를 PixelLab 으로 빚는다 (Core/Forge).
///      빚기(생성 2회, 2~3분) → 마음에 들 때까지 다시 → '이 모습으로' 를 누르면 동작을 뒤에서 만든다(약 30분). 그동안은 기성 모습으로 놀고,
///      다 되면 저절로 갈아입는다. 항목과 묘사 조각은 data/player/forge.json.
/// ↑↓ 줄 고르기, ←→ 바꾸기, 공격(J)·Enter 확인, C·Esc 닫기.
/// </summary>
public partial class CharacterCreator : Control
{
    private sealed class Row
    {
        public string Id, Name;
        public List<(string label, string text)> Options = new();
        public int Pick;
    }

    private IPlayerContext _player;
    private int _tab;                 // 0 기성 모습, 1 빚기
    private int _row;
    private int _base, _accent;
    private string _prevBase;
    private int _prevAccent;
    private bool _first;              // 처음 켬: 닫으면 그대로 확정
    private float _age;
    private readonly List<Row> _rows = new();
    private string _promptFormat;
    private string _free = "";
    private LineEdit _input;
    private string _previewId;        // 방금 빚은 원화
    private Texture2D _preview;
    private string _chosenId;         // 동작을 만들고 있는(또는 다 만든) 원화
    private readonly RandomNumberGenerator _rng = new();

    private const int VisibleRows = 9;
    private static readonly Rect2 Panel = new(14, 30, 372, 304);   // 저해상도 좌표 (640x360)
    private const float RowH = 19f;
    private const int CreateCost = 2;

    public bool IsOpen => Visible;
    /// <summary>닫혔다 (확정이든 취소든) — 첫 화면 뒤의 여는 글이 이어진다</summary>
    public event System.Action Closed;

    public override void _Ready()
    {
        Name = "CharacterCreator";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        LoadRows();
        Forge.Instance.Done += OnForgeDone;
    }

    public override void _ExitTree() => Forge.Instance.Done -= OnForgeDone;

    private void LoadRows()
    {
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/player/forge.json"),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        _promptFormat = doc.RootElement.GetProperty("prompt").GetString();
        foreach (var r in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            var row = new Row { Id = r.GetProperty("id").GetString(), Name = r.GetProperty("name").GetString() };
            foreach (var o in r.GetProperty("options").EnumerateArray())
                row.Options.Add((o[0].GetString(), o[1].GetString()));
            _rows.Add(row);
        }
    }

    public void Bind(IPlayerContext player)
    {
        _player = player;
        // 지난번에 만들다 만 동작이 있으면 이어서
        _chosenId = FindChosen();
        if (_chosenId != null && !Forge.Ready(_chosenId) && Forge.Instance.HasKey && !Dev.DevCapture.Requested())
            _ = Forge.Instance.Animate(_chosenId);
    }

    /// <summary>'이 모습으로' 정해 둔 빚은 모습 (가장 최근 것)</summary>
    private static string FindChosen()
    {
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(Forge.Root)))
            return null;
        string best = null;
        ulong bestTime = 0;
        foreach (string dir in DirAccess.GetDirectoriesAt(Forge.Root))
        {
            string mark = $"{Forge.Root}/{dir}/chosen";
            if (!FileAccess.FileExists(mark))
                continue;
            ulong t = FileAccess.GetModifiedTime(mark);
            if (t >= bestTime)
            {
                bestTime = t;
                best = dir;
            }
        }
        return best;
    }

    public void Open(bool first = false, bool forgeTab = false)
    {
        var look = AppearanceDef.Instance;
        _prevBase = _player.LookBase;
        _prevAccent = _player.LookAccent;
        _base = Mathf.Max(0, look.Bases.FindIndex(b => b.Id == _prevBase));
        _accent = _prevAccent;
        _tab = forgeTab ? 1 : 0;
        _row = 0;
        _first = first;
        _age = 0f;
        Visible = true;
        GameRoot.Instance.MenuOpen = true;
        Sfx.Play("select", -8f, 1f, 0f, "UI");
        _ = Forge.Instance.RefreshBalance();
        // 전에 빚어 둔 모습이 있으면 미리보기에 세워 둔다 (빈 칸으로 열리지 않게)
        if (_preview == null && (_previewId ?? _chosenId) is string pid && Forge.HasPreview(pid))
        {
            _previewId = pid;
            var img = Image.LoadFromFile(Forge.Folder(pid) + "/rot.png");
            _preview = img != null ? ImageTexture.CreateFromImage(img) : null;
        }
    }

    private void Close(bool keep)
    {
        EndInput(false);
        if (!keep)
            _player.SetLook(_prevBase, _prevAccent);
        else
            _player.Save();
        Visible = false;
        GameRoot.Instance.MenuOpen = false;
        Sfx.Play(keep ? "confirmation" : "click", -6f, 1f, 0f, "UI");
        Closed?.Invoke();
    }

    // ── 줄 구성 ─────────────────────────────────────────

    // 기성: 0 갈래, 1 바탕, 2 옷 색, 3 빚은 모습 입기(있을 때), 끝 = 확인
    private bool HasForged => _chosenId != null && Forge.Ready(_chosenId);
    private int PresetRows => HasForged ? 5 : 4;
    // 빚기: 0 갈래, 1..N 항목, N+1 자유 묘사, N+2 무작위, N+3 빚기, N+4 이 모습으로
    private int FreeRow => _rows.Count + 1;
    private int RandomRow => _rows.Count + 2;
    private int CreateRow => _rows.Count + 3;
    private int ChooseRow => _rows.Count + 4;
    private int ForgeRows => _rows.Count + 5;
    private int RowCount => _tab == 0 ? PresetRows : ForgeRows;

    private string Description()
    {
        string s = _promptFormat;
        foreach (var r in _rows)
            s = s.Replace("{" + r.Id + "}", r.Options[r.Pick].text);
        return s.Replace("{free}", string.IsNullOrWhiteSpace(_free) ? "" : _free.Trim() + ", ");
    }

    public override void _Process(double delta)
    {
        if (_player == null)
            return;
        if (!Visible)
        {
            if (Input.IsActionJustPressed(Controls.Look) && _player.IsAlive && !GameRoot.Instance.InputLocked && !Dialogue.Open)
                Open();
            return;
        }
        _age += (float)delta;
        QueueRedraw();
        if (_input != null)
            return;   // 자유 묘사를 적는 중 — Enter 가 끝낸다
        var look = AppearanceDef.Instance;
        if (Input.IsActionJustPressed(Controls.Up))
            Move(ref _row, -1, RowCount);
        if (Input.IsActionJustPressed(Controls.Down))
            Move(ref _row, 1, RowCount);
        int step = Input.IsActionJustPressed(Controls.Left) ? -1 : Input.IsActionJustPressed(Controls.Right) ? 1 : 0;
        bool accept = Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed("ui_accept");
        if (_row == 0 && (step != 0 || accept))
        {
            _tab = 1 - _tab;
            Sfx.Play("select", -10f, 1.2f, 0.05f, "UI");
        }
        else if (_tab == 0)
            PresetInput(look, step, accept);
        else
            ForgeInput(step, accept);
        if (Visible && (Input.IsActionJustPressed(Controls.Look) || Input.IsActionJustPressed("ui_cancel")) && _age > 0.15f)
            Close(keep: _first);
    }

    private void PresetInput(AppearanceDef look, int step, bool accept)
    {
        if (step != 0 && _row == 1)
            Move(ref _base, step, look.Bases.Count);
        if (step != 0 && _row == 2)
            Move(ref _accent, step, look.Accents.Count);
        if (step != 0 && _row is 1 or 2)
        {
            if (_player.LookForge != null)
                _player.SetForge(null);   // 기성 모습을 만지면 빚은 모습은 벗는다
            _player.SetLook(look.Bases[_base].Id, _accent);
        }
        if (HasForged && _row == 3 && (step != 0 || accept))
        {
            _player.SetForge(_player.LookForge == null ? _chosenId : null);
            Sfx.Play("confirmation", -8f, 1.1f, 0f, "UI");
        }
        else if (accept && _row == PresetRows - 1)
            Close(keep: true);
    }

    private void ForgeInput(int step, bool accept)
    {
        var forge = Forge.Instance;
        if (_row >= 1 && _row <= _rows.Count)
        {
            if (step != 0)
                Move(ref _rows[_row - 1].Pick, step, _rows[_row - 1].Options.Count);
            return;
        }
        if (!accept)
            return;
        if (_row == FreeRow)
            BeginInput();
        else if (_row == RandomRow)
        {
            foreach (var r in _rows)
                r.Pick = _rng.RandiRange(0, r.Options.Count - 1);
            Sfx.Play("select", -8f, 1.3f, 0.1f, "UI");
        }
        else if (_row == CreateRow)
        {
            if (!forge.HasKey || forge.Busy)
                Sfx.Play("error", -10f, 1f, 0f, "UI");
            else
                _ = Create();
        }
        else if (_row == ChooseRow)
        {
            if (_previewId == null || forge.Busy || !forge.HasKey)
                Sfx.Play("error", -10f, 1f, 0f, "UI");
            else
                Choose();
        }
    }

    /// <summary>개발 확인용: 지금 고른 항목으로 빚기를 한 번 돌린다</summary>
    public void ForgeTest() => _ = Create();

    /// <summary>개발 확인용: 이미 빚어 둔 원화(가장 최근 것)를 '이 모습으로' 정해 동작 9개를 끝까지 새긴다 (생성 약 72회)</summary>
    public void ForgeAnimTest()
    {
        string best = null;
        ulong bestTime = 0;
        if (DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(Forge.Root)))
        {
            foreach (string dir in DirAccess.GetDirectoriesAt(Forge.Root))
            {
                if (!Forge.HasPreview(dir))
                    continue;
                ulong t = FileAccess.GetModifiedTime(Forge.Folder(dir) + "/rot.png");
                if (t >= bestTime)
                {
                    bestTime = t;
                    best = dir;
                }
            }
        }
        if (best == null)
        {
            GD.PrintErr("[Forge] 빚어 둔 원화가 없다 — 먼저 --creator=forgetest");
            return;
        }
        _previewId = best;
        if (Forge.Ready(best))
        {
            _chosenId = best;
            OnForgeDone(best);
        }
        else
            Choose();
    }

    private async System.Threading.Tasks.Task Create()
    {
        Sfx.Play("confirmation", -6f, 0.9f, 0f, "UI");
        string id = await Forge.Instance.Create(Description());
        if (id == null || !IsInsideTree())
            return;
        _previewId = id;
        var img = Image.LoadFromFile(Forge.Folder(id) + "/rot.png");
        _preview = img != null ? ImageTexture.CreateFromImage(img) : null;
        Sfx.Play("impactBell_heavy", -8f, 1.4f, 0f, "UI");
    }

    private void Choose()
    {
        _chosenId = _previewId;
        using (var f = FileAccess.Open(Forge.Folder(_chosenId) + "/chosen", FileAccess.ModeFlags.Write))
            f?.StoreString("1");
        Sfx.Play("confirmation", -4f, 1f, 0f, "UI");
        Hud.Toast("빚은 모습의 동작을 새기기 시작했다 — 다 되면 저절로 갈아입는다");
        _ = Forge.Instance.Animate(_chosenId);
    }

    private void OnForgeDone(string id)
    {
        if (!IsInsideTree() || _player == null || id != _chosenId)
            return;
        _player.SetForge(id);
        _player.Save();
        Hud.Announce("빚은 모습이 완성되었다", "세상에 하나뿐인 몸으로 갈아입었다 — C 에서 되돌릴 수 있다", Ui.Gold);
        Sfx.Play("impactBell_heavy", -4f, 1.2f, 0f, "UI");
    }

    // ── 자유 묘사 ───────────────────────────────────────

    private void BeginInput()
    {
        float px = GameRoot.Instance.View.PixelScale;
        _input = new LineEdit { Text = _free, MaxLength = 80, PlaceholderText = "예: glowing blue eyes, a fox tail", ContextMenuEnabled = false };
        _input.AddThemeFontOverride("font", PixelText.Font);
        _input.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(PixelText.Size * px));
        _input.AddThemeColorOverride("font_color", Ui.Text);
        var box = new StyleBoxFlat { BgColor = Ui.Ink, BorderColor = Ui.Gold };
        box.SetBorderWidthAll(Mathf.RoundToInt(px));
        box.SetContentMarginAll(3f * px);
        _input.AddThemeStyleboxOverride("normal", box);
        _input.AddThemeStyleboxOverride("focus", box);
        AddChild(_input);
        _input.Position = new Vector2(Panel.Position.X + 12, Panel.End.Y - 46) * px;
        _input.Size = new Vector2(Panel.Size.X - 24, 20) * px;
        _input.GrabFocus();
        _input.CaretColumn = _free.Length;
        _input.TextSubmitted += _ => EndInput(true);
    }

    private void EndInput(bool keep)
    {
        if (_input == null)
            return;
        if (keep)
            _free = _input.Text;
        _input.QueueFree();
        _input = null;
        _age = 0f;
    }

    private static void Move(ref int value, int step, int count)
    {
        value = (int)Mathf.PosMod(value + step, count);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    // ── 그리기 ──────────────────────────────────────────

    public override void _Draw()
    {
        if (!Visible)
            return;
        float px = GameRoot.Instance.View.PixelScale;
        // 톡 떨어지며 나타난다
        float drop = Mathf.Round(8f * (1f - Mathf.Min(_age / 0.18f, 1f)));
        Ui.Begin(this, px, new Vector2(0f, _input == null ? -drop : 0f));
        Ui.Frame(this, Panel);
        Ui.Title(this, Panel, "모습");
        float x = Panel.Position.X, y = Panel.Position.Y;

        // 갈래
        string[] tabs = { "기성 모습", "빚기" };
        for (int i = 0; i < 2; i++)
        {
            var r = new Rect2(x + 14 + i * 96, y + 16, 90, 18);
            Ui.Button(this, r, tabs[i], _tab == i);
        }
        if (_row == 0)
        {
            float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 1f;
            PixelText.Draw(this, "◀ ▶", new Vector2(x + 212 + bob, y + 30), Ui.Gold);
        }
        if (_tab == 0)
            DrawPreset(x, y);
        else
            DrawForge(x, y);
        Ui.End(this);
    }

    private void DrawPreset(float x, float y)
    {
        var look = AppearanceDef.Instance;
        var accent = look.Accents[_accent];
        float ry = y + 62;
        Line(1, ry, "바탕", look.Bases[_base].Name);
        Line(2, ry + RowH, "옷 색", accent.Name);
        int last = PresetRows - 1;
        if (HasForged)
            Line(3, ry + RowH * 2, "빚은 모습", _player.LookForge != null ? "입고 있다" : "벗어 두었다");
        Ui.Button(this, new Rect2(x + 14, ry + RowH * (last - 1) + 8, 110, 20), "이 모습으로", _row == last);
        foreach (var (line, i) in Ui.Wrap("기성 모습은 바로 바뀐다. 가운데에서 도는 주인공을 보며 고른다.\n하나하나 직접 정하고 싶다면 '빚기' 로.", Panel.Size.X - 32).Select((l, i) => (l, i)))
            PixelText.Draw(this, line, new Vector2(x + 16, y + 190 + i * 14), Ui.Dim);
        Ui.Hints(this, new Vector2(Panel.GetCenter().X, Panel.End.Y - 10), ("W S", "고르기"), ("A D", "바꾸기"), ("J", "확인"), ("C", "닫기"));
    }

    private void DrawForge(float x, float y)
    {
        var forge = Forge.Instance;
        float ry = y + 56;
        // 항목: 고른 줄이 보이게 굴린다
        int top = Mathf.Clamp(_row - 1 - VisibleRows / 2, 0, Mathf.Max(0, _rows.Count - VisibleRows));
        for (int i = 0; i < VisibleRows && top + i < _rows.Count; i++)
        {
            var r = _rows[top + i];
            Line(top + i + 1, ry + i * RowH, r.Name, r.Options[r.Pick].label, 96f, 224f);
        }
        if (_rows.Count > VisibleRows)
        {
            // 굴림 표시
            float track = VisibleRows * RowH - 4;
            float knob = track * VisibleRows / _rows.Count;
            DrawRect(new Rect2(x + 234, ry - 12, 2, track), new Color(Ui.Gold, 0.2f));
            DrawRect(new Rect2(x + 234, ry - 12 + (track - knob) * top / Mathf.Max(1, _rows.Count - VisibleRows), 2, knob), Ui.Gold);
        }
        float by = ry + VisibleRows * RowH + 2;
        // 자유 묘사
        bool onFree = _row == FreeRow;
        if (onFree)
            Ui.Highlight(this, new Rect2(x + 8, by - 12, 222, 17));
        PixelText.Draw(this, "자유 묘사", new Vector2(x + 16, by), onFree ? Ui.Gold : Ui.Dim);
        string free = string.IsNullOrWhiteSpace(_free) ? "(영어로 덧붙인다 — J)" : _free.Length > 22 ? _free[..22] + "…" : _free;
        PixelText.Draw(this, free, new Vector2(x + 82, by), string.IsNullOrWhiteSpace(_free) ? Ui.Faint : Ui.Text);

        Ui.Button(this, new Rect2(x + 14, by + 10, 70, 18), "무작위", _row == RandomRow);
        bool canCreate = forge.HasKey && !forge.Busy;
        Ui.Button(this, new Rect2(x + 90, by + 10, 138, 18), _previewId == null ? $"빚기 (생성 {CreateCost}회)" : $"다시 빚기 (생성 {CreateCost}회)", _row == CreateRow, !canCreate);
        int animCost = forge.Anims.Count * Forge.Dirs.Length;
        bool canChoose = _previewId != null && canCreate && _previewId != _chosenId;
        Ui.Button(this, new Rect2(x + 14, by + 32, 214, 18), $"이 모습으로 (동작 생성 {animCost}회)", _row == ChooseRow, !canChoose);

        // 오른쪽: 미리보기 (8방향으로 천천히 돈다)
        var box = new Rect2(x + 244, y + 46, 116, 132);
        DrawRect(box, new Color(0.12f, 0.11f, 0.17f));
        DrawRect(box, Ui.GoldDim, false, 1f);
        if (_preview != null)
        {
            int dir = (int)(Time.GetTicksMsec() / 700 % 8);
            DrawTextureRectRegion(_preview, new Rect2(box.Position.X - 6, box.Position.Y + 4, 128, 128), new Rect2(0, dir * 128, 128, 128));
        }
        else
            foreach (var (line, i) in Ui.Wrap("빚은 모습이 여기 선다", 100).Select((l, i) => (l, i)))
                PixelText.DrawCentered(this, line, new Vector2(box.GetCenter().X, box.Position.Y + 60 + i * 14), Ui.Faint);

        // 상태
        float sy = box.End.Y + 16;
        float sw = Panel.End.X - box.Position.X - 8;
        string status;
        Color color = Ui.Dim;
        if (!forge.HasKey)
        {
            status = "PixelLab 열쇠가 없다 — user://pixellab_key.txt 에 넣는다";
            color = Ui.Red;
        }
        else if (forge.Error != null)
        {
            status = forge.Error;
            color = Ui.Red;
        }
        else if (forge.Busy)
        {
            status = forge.Status;
            color = Ui.Gold;
        }
        else if (_chosenId != null && Forge.Ready(_chosenId))
            status = "빚은 모습이 완성되어 있다 ('기성 모습' 에서 입고 벗는다)";
        else
            status = "항목을 고르고 '빚기'. 2~3분 걸린다. 마음에 들 때까지 다시 빚을 수 있다.";
        var rows = Ui.Wrap(status, sw);
        for (int i = 0; i < rows.Count && i < 4; i++)
            PixelText.Draw(this, rows[i], new Vector2(box.Position.X, sy + i * 13), color);
        if (forge.Busy && forge.Progress > 0f)
            Ui.Bar(this, new Rect2(box.Position.X, sy + rows.Count * 13 - 4, 116, 4), forge.Progress, Ui.Gold, Ui.GoldDim);
        if (forge.Balance >= 0)
            PixelText.Draw(this, $"남은 생성 {forge.Balance}회", new Vector2(box.Position.X, Panel.End.Y - 46), Ui.Faint);
        if (_input == null)
            Ui.Hints(this, new Vector2(Panel.GetCenter().X, Panel.End.Y - 10), ("W S", "고르기"), ("A D", "바꾸기"), ("J", "확인"), ("C", "닫기"));
        else
            Ui.Hints(this, new Vector2(Panel.GetCenter().X, Panel.End.Y - 10), ("Enter", "적기 끝"));
    }

    /// <summary>한 줄: 이름, 그리고 ◀ 값 ▶. 고른 줄은 밝은 띠.</summary>
    private void Line(int index, float baseline, string label, string value, float valueX = 150f, float width = 240f)
    {
        float x = Panel.Position.X;
        bool on = _row == index;
        if (on)
            Ui.Highlight(this, new Rect2(x + 8, baseline - 12, width - 2, 17));
        PixelText.Draw(this, label, new Vector2(x + 16, baseline), on ? Ui.Gold : Ui.Dim);
        float center = x + valueX + (width - valueX) * 0.5f - 6;
        PixelText.DrawCentered(this, value, new Vector2(center, baseline), on ? Colors.White : Ui.Text);
        if (!on)
            return;
        // 좌우로 살짝 숨 쉬는 화살표
        float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 1f;
        PixelText.Draw(this, "◀", new Vector2(x + valueX - 8 - bob, baseline), Ui.Gold);
        PixelText.Draw(this, "▶", new Vector2(x + width - 14 + bob, baseline), Ui.Gold);
    }
}
