using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.World;

namespace PixelMmo.Render;

/// <summary>
/// 화면 차림 전부 — 첫 화면(타이틀), 멈춤, 이야기(Tab), 이름록(N), 지도(M), 조작법, 설정.
/// 한 번에 하나만 뜨고, Esc·회피로 한 단계 물러난다 (멈춤에서 연 것은 멈춤으로, 바로 연 것은 놀이로).
/// 놀이 중에 뜨면 세계가 멈춘다 (트리 일시정지). 첫 화면은 세계가 살아 움직이는 채로 그 위에 뜬다.
/// 그리기는 Render/Ui 의 부품만 쓴다 — 저해상도(640x360) 좌표.
/// </summary>
public partial class Menus : Control
{
    private enum Screen { None, Title, Confirm, Pause, Journal, Codex, Map, Help, Settings }

    /// <summary>'새로 시작'으로 장면을 다시 불러온 직후: 첫 화면을 건너뛴다</summary>
    public static bool SkipTitle { get; set; }
    public static Menus Instance { get; private set; }
    public bool Open => _screen != Screen.None;

    private Hero _hero;
    private CharacterCreator _creator;
    private Screen _screen = Screen.None;
    private Screen _from = Screen.None;     // 물러날 곳
    private int _pick;
    private float _age;
    private Texture2D _mapTex;
    private readonly bool _canPause = !Dev.DevCapture.Requested();   // 캡처는 트리를 멈추면 끝나지 않는다
    private float _pitch0, _dist0;
    private Vector3 _offset0;
    private List<JobDef> _codex = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly (float x, float speed, float sway, float phase)[] _leaves = new (float, float, float, float)[26];

    private static readonly string[] TitleItems = { "이어하기", "새로 시작", "설정", "끝내기" };
    private static readonly string[] PauseItems = { "계속하기", "이야기", "이름록", "지도", "조작법", "설정", "첫 화면으로" };
    private static readonly (string key, string label)[] HelpKeys =
    {
        ("WASD", "움직이기"), ("J · 좌클릭", "공격 (이어 누르면 연속기)"), ("K · 우클릭", "막기 — 맞는 순간 누르면 받아넘김"),
        ("Space", "회피 — 스치듯 피하면 세상이 느려진다"), ("Q E R", "기술"), ("H", "이슬 한 모금 (여신상에서 채운다)"),
        ("F", "말 걸기 · 기도 · 이름비"), ("V", "멀리 보기 (누른 채 방향키로 둘러보기)"), ("Tab", "이야기"),
        ("N", "이름록"), ("M", "지도"), ("C", "모습 바꾸기"), ("Esc", "멈춤"),
    };

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Name = "Menus";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        ProcessMode = ProcessModeEnum.Always;
        if (ResourceLoader.Exists("res://art/ui/map_world.png"))
            _mapTex = GD.Load<Texture2D>("res://art/ui/map_world.png");
        for (int i = 0; i < _leaves.Length; i++)
            _leaves[i] = (_rng.Randf(), _rng.RandfRange(14f, 30f), _rng.RandfRange(6f, 18f), _rng.Randf() * Mathf.Tau);
        Settings.Instance.Apply();
    }

    public void Bind(Hero hero, CharacterCreator creator)
    {
        _hero = hero;
        _creator = creator;
        creator.Closed += OnCreatorClosed;
    }

    /// <summary>게임을 켠 직후: 첫 화면을 띄우거나(평소), 바로 놀이로 (새로 시작 뒤·캡처)</summary>
    public void Boot(bool title)
    {
        if (title && !SkipTitle)
            OpenTitle();
        else
            Begin();
        SkipTitle = false;
    }

    // ── 첫 화면 ─────────────────────────────────────────

    private void OpenTitle()
    {
        var root = GameRoot.Instance;
        _screen = Screen.Title;
        _pick = HasSave ? 0 : 1;
        _age = 0f;
        root.MenuOpen = true;
        root.TitleOpen = true;
        _hero.Hidden = true;
        // 고개를 들어 멀리 — 세계가 배경이 된다
        var rig = root.Rig;
        _pitch0 = rig.Pitch;
        _dist0 = rig.Distance;
        _offset0 = rig.Offset;
        rig.Pitch = 9f;
        rig.Distance = 34f;
        rig.Offset = new Vector3(6f, 5f, -14f);
        rig.SnapNext();
    }

    private static bool HasSave => FileAccess.FileExists(SaveData.Path);

    private void CloseTitle()
    {
        var root = GameRoot.Instance;
        root.TitleOpen = false;
        root.MenuOpen = false;
        _hero.Hidden = false;
        root.Rig.Pitch = _pitch0;
        root.Rig.Distance = _dist0;
        root.Rig.Offset = _offset0;
        _screen = Screen.None;
        Begin();
    }

    /// <summary>놀이를 시작한다: 처음이면 모습부터, 그다음 여는 글</summary>
    private void Begin()
    {
        // 여는 글을 아직 못 봤으면 모습부터 (첫 화면에서 그냥 끄면 기록만 남는다 — FirstLaunch 로 가리면 모습 고르기를 건너뛴다)
        if (_hero.Var("intro") == 0)
            _creator.Open(first: true);
        else
            Intro();
    }

    private void OnCreatorClosed()
    {
        if (_hero.Var("intro") == 0)
            Intro();
    }

    private void Intro()
    {
        if (_hero.Var("intro") != 0)
            return;
        _hero.SetVar("intro", 1);
        Dialogue.Show(new[]
        {
            new Dialogue.Line(null, null, "세계수 이르미의 잎에는 만물의 이름이 적혀 있다."),
            new Dialogue.Line(null, null, "이름이 있는 것은 존재하고, 잎이 지면 — 잊힌다."),
            new Dialogue.Line(null, null, "십 년 전부터, 잎이 지기 시작했다."),
            new Dialogue.Line(null, null, "그리고 오늘. 아무것도 적히지 않은 잎 한 장이 떨어졌다."),
        });
    }

    private void NewGame()
    {
        // 기록을 지우고 장면을 처음부터 다시 짓는다 (세계·이야기·무명지가 모두 처음 상태로)
        Hero.SuppressSave = true;
        if (HasSave)
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SaveData.Path));
        SkipTitle = true;
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    // ── 열고 닫기 ───────────────────────────────────────

    private void Show(Screen s, Screen from)
    {
        _screen = s;
        _from = from;
        _pick = 0;
        _age = 0f;
        GameRoot.Instance.MenuOpen = true;
        if (_canPause && from != Screen.Title && s != Screen.Title)
            GetTree().Paused = true;
        if (s == Screen.Codex)
            BuildCodex();
        if (s == Screen.Journal)
        {
            var list = Stories();
            _pick = Mathf.Max(0, list.FindIndex(x => x == Story.Instance?.Tracked));
        }
        Sfx.Play("select", -8f, 1f, 0f, "UI");
    }

    private void Back()
    {
        Sfx.Play("click", -8f, 1f, 0f, "UI");
        if (_from is Screen.Pause or Screen.Title)
        {
            var to = _from;
            _screen = to;
            _from = Screen.None;
            _pick = 0;
            _age = 0.2f;
            return;
        }
        _screen = Screen.None;
        GameRoot.Instance.MenuOpen = false;
        GetTree().Paused = false;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_hero == null)
            return;
        if (_screen == Screen.None)
        {
            // 놀이 중 바로 여는 키
            var root = GameRoot.Instance;
            if (root.InputLocked || Dialogue.Open || !_hero.IsAlive || Story.Instance?.Busy == true)
                return;
            if (Input.IsActionJustPressed(Controls.Pause)) Show(Screen.Pause, Screen.None);
            else if (Input.IsActionJustPressed(Controls.Journal)) Show(Screen.Journal, Screen.None);
            else if (Input.IsActionJustPressed(Controls.Codex)) Show(Screen.Codex, Screen.None);
            else if (Input.IsActionJustPressed(Controls.Map)) Show(Screen.Map, Screen.None);
            return;
        }
        QueueRedraw();
        if (_age < 0.12f)
            return;   // 연 키가 곧바로 닫지 않게
        bool up = Input.IsActionJustPressed(Controls.Up), down = Input.IsActionJustPressed(Controls.Down);
        bool left = Input.IsActionJustPressed(Controls.Left), right = Input.IsActionJustPressed(Controls.Right);
        bool ok = Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed(Controls.Interact) || Input.IsActionJustPressed("ui_accept");
        bool back = Input.IsActionJustPressed(Controls.Pause) || Input.IsActionJustPressed(Controls.Dodge) && _screen != Screen.Title;
        switch (_screen)
        {
            case Screen.Title:
                Move(up, down, TitleItems.Length);
                if (_pick == 0 && !HasSave && (up || down))
                    _pick = up ? TitleItems.Length - 1 : 1;   // 이어할 기록이 없으면 건너뛴다
                if (ok)
                {
                    Sfx.Play("confirmation", -6f, 1f, 0f, "UI");
                    switch (_pick)
                    {
                        case 0: CloseTitle(); break;
                        case 1:
                            if (HasSave) { _screen = Screen.Confirm; _pick = 1; _age = 0f; }
                            else NewGameFromTitle();
                            break;
                        case 2: Show(Screen.Settings, Screen.Title); break;
                        case 3: GetTree().Quit(); break;
                    }
                }
                break;
            case Screen.Confirm:
                Move(up || left, down || right, 2);
                if (ok && _pick == 0) NewGame();
                else if (ok || back) { _screen = Screen.Title; _pick = 1; _age = 0f; }
                break;
            case Screen.Pause:
                Move(up, down, PauseItems.Length);
                if (back) { _from = Screen.None; Back(); }
                else if (ok)
                    switch (_pick)
                    {
                        case 0: _from = Screen.None; Back(); break;
                        case 1: Show(Screen.Journal, Screen.Pause); break;
                        case 2: Show(Screen.Codex, Screen.Pause); break;
                        case 3: Show(Screen.Map, Screen.Pause); break;
                        case 4: Show(Screen.Help, Screen.Pause); break;
                        case 5: Show(Screen.Settings, Screen.Pause); break;
                        case 6:
                            _hero.Save();
                            SkipTitle = false;
                            GetTree().Paused = false;
                            GetTree().ReloadCurrentScene();
                            break;
                    }
                break;
            case Screen.Journal:
                var stories = Stories();
                Move(up, down, Mathf.Max(1, stories.Count));
                if (ok && _pick < stories.Count && Story.Instance.Active(stories[_pick]))
                {
                    Story.Instance.Track(stories[_pick].Id);
                    Sfx.Play("confirmation", -8f, 1.2f, 0f, "UI");
                }
                if (back || Input.IsActionJustPressed(Controls.Journal)) Back();
                break;
            case Screen.Codex:
                CodexMove(left, right, up, down);
                if (back || Input.IsActionJustPressed(Controls.Codex)) Back();
                break;
            case Screen.Map:
                if (back || ok || Input.IsActionJustPressed(Controls.Map)) Back();
                break;
            case Screen.Help:
                if (back || ok) Back();
                break;
            case Screen.Settings:
                Move(up, down, 3);
                int step = left ? -1 : right ? 1 : 0;
                if (step != 0 || ok && _pick == 2)
                {
                    var s = Settings.Instance;
                    if (_pick == 0) s.Volume += step;
                    else if (_pick == 1) s.Shake += step;
                    else s.Fullscreen = !s.Fullscreen;
                    s.Apply();
                    s.Save();
                    Sfx.Play("select", -8f, 1.1f, 0f, "UI");
                    if (_pick == 1)
                        CombatFx.Shake(0.4f);   // 얼마나 흔들리는지 바로 보여 준다
                }
                if (back) Back();
                break;
        }
    }

    private void NewGameFromTitle()
    {
        if (_hero.FirstLaunch)
            CloseTitle();   // 기록이 없다 — 지금 세계 그대로 시작
        else
            NewGame();
    }

    private void Move(bool up, bool down, int count)
    {
        if (!up && !down)
            return;
        _pick = (int)Mathf.PosMod(_pick + (down ? 1 : -1), count);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    private List<StoryDef> Stories() =>
        Story.Instance == null ? new List<StoryDef>() : StoryDef.All.Where(s => Story.Instance.Opened(s)).ToList();

    // ── 이름록 ──────────────────────────────────────────

    private void BuildCodex()
    {
        // 히든 이름은 얻기 전에는 책에 없다 (규칙 4)
        _codex = JobDef.All.Values.Where(j => !j.Hidden || _hero.HeldJob(j.Id))
            .OrderBy(j => j.Tier).ThenBy(j => FamilyOrder(j)).ThenBy(j => j.Id).ToList();
        _pick = Mathf.Max(0, _codex.FindIndex(j => j.Id == _hero.JobId));
    }

    private static readonly string[] Families = { "novice", "warrior", "rogue", "archer", "mage" };

    private static int FamilyOrder(JobDef j)
    {
        string root = j.Tier <= 1 ? j.Id : j.From.FirstOrDefault() ?? j.Id;
        int i = System.Array.IndexOf(Families, root);
        return i < 0 ? 99 : i;
    }

    /// <summary>이름의 나무에서의 자리 (판 안 좌표)</summary>
    private Vector2 CodexSpot(JobDef j)
    {
        const float X0 = 78f, Step = 68f;
        if (j.Tier == 0)
            return new Vector2(X0 + Step * 1.5f, 62);
        int fam = Mathf.Max(0, FamilyOrder(j) - 1);
        if (j.Tier == 1)
            return new Vector2(X0 + Step * fam, 118);
        var siblings = _codex.Where(o => o.Tier == j.Tier && FamilyOrder(o) == FamilyOrder(j)).ToList();
        int k = siblings.IndexOf(j);
        float spread = siblings.Count > 1 ? (k - (siblings.Count - 1) * 0.5f) * 30f : 0f;
        float baseX = FamilyOrder(j) == 0 ? X0 + Step * 3.6f : X0 + Step * fam;
        return new Vector2(baseX + spread, 118 + (j.Tier - 1) * 58);
    }

    private void CodexMove(bool left, bool right, bool up, bool down)
    {
        if (_codex.Count == 0 || !(left || right || up || down))
            return;
        Vector2 here = CodexSpot(_codex[_pick]);
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _codex.Count; i++)
        {
            if (i == _pick)
                continue;
            Vector2 d = CodexSpot(_codex[i]) - here;
            bool okDir = left ? d.X < -4 && Mathf.Abs(d.Y) < 20 : right ? d.X > 4 && Mathf.Abs(d.Y) < 20 : up ? d.Y < -20 : d.Y > 20;
            if (!okDir)
                continue;
            float score = left || right ? Mathf.Abs(d.X) : Mathf.Abs(d.Y) + Mathf.Abs(d.X) * 0.5f;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        if (best < 0)
            return;
        _pick = best;
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    // ── 그리기 ──────────────────────────────────────────

    public override void _Draw()
    {
        if (_screen == Screen.None || _hero == null)
            return;
        float px = GameRoot.Instance.View.PixelScale;
        Vector2 low = Ui.LowSize(this, px);
        float drop = Mathf.Round(6f * (1f - Mathf.Min(_age / 0.15f, 1f)));
        Ui.Begin(this, px);
        if (_screen is not Screen.Title and not Screen.Confirm && _from != Screen.Title)
            DrawRect(new Rect2(Vector2.Zero, low), new Color(0.03f, 0.02f, 0.06f, 0.55f));
        Ui.End(this);
        Ui.Begin(this, px, new Vector2(0, -drop));
        switch (_screen)
        {
            case Screen.Title:
            case Screen.Confirm:
                DrawTitle(low, px);
                break;
            case Screen.Pause: DrawPause(low); break;
            case Screen.Journal: DrawJournal(low); break;
            case Screen.Codex: DrawCodex(low); break;
            case Screen.Map: DrawMap(low); break;
            case Screen.Help: DrawHelp(low); break;
            case Screen.Settings: DrawSettings(low); break;
        }
        Ui.End(this);
    }

    private void DrawTitle(Vector2 low, float px)
    {
        // 아래에서 위로 옅어지는 어둠 + 흩날리는 잎
        for (int i = 0; i < 12; i++)
            DrawRect(new Rect2(0, low.Y - (i + 1) * 30, low.X, 30), new Color(0.03f, 0.02f, 0.07f, 0.72f * (1f - i / 12f)));
        for (int i = 0; i < _leaves.Length; i++)
        {
            var (x, speed, sway, phase) = _leaves[i];
            float y = Mathf.PosMod(_age * speed + i * 37f, low.Y + 20f) - 10f;
            float lx = x * low.X + Mathf.Sin(_age * 0.9f + phase) * sway;
            bool gold = i % 4 == 0;
            Color c = gold ? new Color(Ui.Gold, 0.9f) : new Color(0.82f, 0.84f, 0.9f, 0.55f);
            DrawRect(new Rect2(Mathf.Round(lx), Mathf.Round(y), 2, 1), c);
            DrawRect(new Rect2(Mathf.Round(lx) + (Mathf.Sin(_age * 3f + phase) > 0 ? 1 : 0), Mathf.Round(y) + 1, 1, 1), c);
        }
        // 제목: 굴림 12px 를 네 배로 — 굵은 픽셀 글씨
        float cx = low.X * 0.5f;
        Ui.End(this);
        const float Big = 4f;
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * px * Big);
        PixelText.DrawCentered(this, "무명록", new Vector2(cx / Big, 110f / Big), Ui.Gold);
        Ui.Begin(this, px);
        PixelText.DrawCentered(this, "無 名 錄", new Vector2(cx, 132), new Color(Ui.Text, 0.85f));
        DrawRect(new Rect2(cx - 90, 140, 180, 1), new Color(Ui.Gold, 0.6f));
        PixelText.DrawCentered(this, "이름이 지는 대륙", new Vector2(cx, 158), Ui.Dim);

        for (int i = 0; i < TitleItems.Length; i++)
        {
            bool disabled = i == 0 && !HasSave;
            var r = new Rect2(cx - 62, 196 + i * 26, 124, 20);
            Ui.Button(this, r, TitleItems[i], _screen == Screen.Title && _pick == i, disabled);
        }
        Ui.Hints(this, new Vector2(cx, low.Y - 16), ("W S", "고르기"), ("J · Enter", "확인"));
        if (_screen == Screen.Confirm)
        {
            var box = new Rect2(cx - 150, 140, 300, 96);
            Ui.Frame(this, box, Ui.Red);
            PixelText.DrawCentered(this, "처음부터 다시 시작할까요?", new Vector2(cx, box.Position.Y + 28), Ui.Text);
            PixelText.DrawCentered(this, "지금까지의 기록(이름·각인·이야기)이 지워집니다.", new Vector2(cx, box.Position.Y + 46), Ui.Dim);
            Ui.Button(this, new Rect2(cx - 104, box.End.Y - 30, 96, 20), "새로 시작", _pick == 0);
            Ui.Button(this, new Rect2(cx + 8, box.End.Y - 30, 96, 20), "돌아가기", _pick == 1);
        }
    }

    private void DrawPause(Vector2 low)
    {
        float cx = low.X * 0.5f;
        // 판 안에 단추·바로가기 글쇠·조작 안내가 다 들어오게 (밖에 그리면 아래 스킬 칸과 겹친다)
        var box = new Rect2(cx - 116, 52, 232, 52 + PauseItems.Length * 26 + 14);
        Ui.Frame(this, box);
        Ui.Title(this, box, "멈춤");
        string[] keys = { "Esc", "Tab", "N", "M", "", "", "" };
        for (int i = 0; i < PauseItems.Length; i++)
        {
            var r = new Rect2(cx - 66, box.Position.Y + 24 + i * 26, 132, 20);
            Ui.Button(this, r, PauseItems[i], _pick == i);
            if (keys[i].Length > 0)
                PixelText.Draw(this, keys[i], new Vector2(r.End.X + 8, r.Position.Y + 14), Ui.Faint);
        }
        Ui.Hints(this, new Vector2(cx, box.End.Y - 8), ("W S", "고르기"), ("J", "확인"), ("Esc", "돌아가기"));
    }

    private void DrawJournal(Vector2 low)
    {
        var box = new Rect2(low.X * 0.5f - 270, 34, 540, 284);
        Ui.Frame(this, box);
        Ui.Title(this, box, "이야기");
        var story = Story.Instance;
        var list = Stories();
        float x = box.Position.X + 14, y = box.Position.Y + 30;
        DrawRect(new Rect2(x + 178, box.Position.Y + 18, 1, box.Size.Y - 48), new Color(Ui.Gold, 0.25f));
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            bool on = i == _pick;
            bool done = story.Finished(s);
            var row = new Rect2(x - 4, y + i * 22 - 13, 174, 19);
            if (on)
                Ui.Highlight(this, row);
            string mark = done ? "✔" : s == story.Tracked ? "◆" : "·";
            PixelText.Draw(this, mark, new Vector2(x, y + i * 22), done ? Ui.Green : Ui.Gold);
            PixelText.Draw(this, s.Name, new Vector2(x + 14, y + i * 22), done ? Ui.Dim : on ? Colors.White : Ui.Text);
        }
        if (_pick < list.Count)
        {
            var s = list[_pick];
            float dx = x + 192, w = box.End.X - dx - 14;
            PixelText.Draw(this, $"「{s.Name}」", new Vector2(dx, y), Ui.Gold);
            float ty = y + 18;
            foreach (string row in Ui.Wrap(s.Summary, w))
            {
                PixelText.Draw(this, row, new Vector2(dx, ty), Ui.Dim);
                ty += 14;
            }
            ty += 8;
            int at = story.StepIndex(s);
            for (int k = 0; k < s.Steps.Count && k <= at; k++)
            {
                bool past = k < at;
                string text = past ? s.Steps[k].Text : story.StepText(s);
                var rows = Ui.Wrap(text, w - 16);
                PixelText.Draw(this, past ? "✔" : "▶", new Vector2(dx, ty), past ? Ui.Green : Ui.Gold);
                foreach (string row in rows)
                {
                    PixelText.Draw(this, row, new Vector2(dx + 14, ty), past ? Ui.Faint : Ui.Text);
                    ty += 14;
                }
                ty += 3;
                if (ty > box.End.Y - 40)
                    break;
            }
            if (story.Finished(s))
                PixelText.Draw(this, "이 이야기는 끝났다.", new Vector2(dx, ty + 4), Ui.Green);
        }
        Ui.Hints(this, new Vector2(low.X * 0.5f, box.End.Y - 10), ("W S", "고르기"), ("J", "이 이야기를 따라간다"), ("Esc", "닫기"));
    }

    private void DrawCodex(Vector2 low)
    {
        var box = new Rect2(low.X * 0.5f - 290, 26, 580, 306);
        Ui.Frame(this, box);
        Ui.Title(this, box, "이름록");
        Vector2 o = box.Position;
        // 나무의 가지: 어미 → 자식
        foreach (var j in _codex)
        {
            Vector2 c = o + CodexSpot(j);
            foreach (string parent in j.From)
            {
                var p = _codex.FirstOrDefault(x => x.Id == parent);
                if (p == null)
                    continue;
                Vector2 pc = o + CodexSpot(p);
                bool lit = _hero.HeldJob(j.Id);
                Color line = lit ? Ui.Gold : new Color(Ui.GoldDim, 0.45f);
                float midY = Mathf.Round((pc.Y + c.Y) * 0.5f);
                DrawRect(new Rect2(pc.X, pc.Y + 9, 1, midY - pc.Y - 9), line);
                DrawRect(new Rect2(Mathf.Min(pc.X, c.X), midY, Mathf.Abs(pc.X - c.X) + 1, 1), line);
                DrawRect(new Rect2(c.X, midY, 1, c.Y - 9 - midY), line);
            }
        }
        for (int i = 0; i < _codex.Count; i++)
        {
            var j = _codex[i];
            Vector2 c = o + CodexSpot(j);
            bool held = _hero.HeldJob(j.Id);
            bool current = j.Id == _hero.JobId;
            var pal = CombatFx.PaletteOf(j.Palette);
            if (i == _pick)
            {
                float pulse = Mathf.PosMod(_age, 0.8f) < 0.4f ? 13f : 12f;
                DrawRect(new Rect2(c - Vector2.One * pulse, Vector2.One * pulse * 2), new Color(Ui.Gold, 0.9f), false, 1f);
            }
            Ui.Emblem(this, c, j.Sealed && !held ? "?" : j.Glyph, pal.Mid, held || !j.Sealed);
            if (j.Tier <= 1)
                PixelText.DrawCentered(this, j.Name, c + new Vector2(0, 24), current ? Ui.Gold : held ? Ui.Text : Ui.Dim);
            if (current)
                PixelText.DrawCentered(this, "▼", c + new Vector2(0, -12), Ui.Gold);
        }
        // 고른 이름의 풀이
        if (_pick < _codex.Count)
        {
            var j = _codex[_pick];
            bool held = _hero.HeldJob(j.Id);
            bool secret = j.Sealed && !held;
            var pal = CombatFx.PaletteOf(j.Palette);
            float dx = o.X + 368, w = box.End.X - dx - 16;
            DrawRect(new Rect2(dx - 12, o.Y + 20, 1, 206), new Color(Ui.Gold, 0.25f));
            Ui.Emblem(this, new Vector2(dx + 10, o.Y + 40), secret ? "?" : j.Glyph, pal.Mid, !secret);
            PixelText.Draw(this, secret ? "봉인된 이름" : j.Name, new Vector2(dx + 28, o.Y + 38), secret ? Ui.Dim : pal.Bright);
            PixelText.Draw(this, secret ? "아직 아무도 불러 주지 않았다" : j.Motto ?? "", new Vector2(dx + 28, o.Y + 52), Ui.Dim);
            float ty = o.Y + 78;
            string body = secret ? "이 이름으로 가는 길은 책에 적혀 있지 않다. 다음 이야기에서 열린다." : j.Description;
            foreach (string row in Ui.Wrap(body, w))
            {
                PixelText.Draw(this, row, new Vector2(dx, ty), Ui.Text);
                ty += 14;
            }
            ty += 10;
            if (!secret)
            {
                int stage = _hero.EngraveStage(j.Id);
                var eng = EngraveDef.Instance;
                PixelText.Draw(this, "각인", new Vector2(dx, ty), Ui.Gold);
                PixelText.Draw(this, stage > 0 ? $"{eng.Title(stage)}  ({stage}/{eng.MaxStage})" : held ? "아직 새기지 않았다" : "—",
                    new Vector2(dx + 34, ty), stage > 0 ? Ui.Text : Ui.Dim);
                ty += 8;
                Ui.Bar(this, new Rect2(dx, ty, w, 4), held ? _hero.EngraveProgress(j.Id) : 0f, pal.Bright, pal.Mid);
                ty += 20;
                if (stage > 0)
                {
                    PixelText.Draw(this, $"힘 +{Mathf.RoundToInt(eng.PowerPerStage * stage * 100)}%   체력 +{Mathf.RoundToInt(eng.HpPerStage * stage * 100)}%",
                        new Vector2(dx, ty), Ui.Green);
                    ty += 16;
                }
                if (!held && !j.Sealed && j.Trainer != null)
                {
                    var npc = NpcDef.All.FirstOrDefault(n => n.Id == j.Trainer);
                    foreach (string row in Ui.Wrap($"받는 곳: {npc?.Title ?? "이름지기"} {npc?.Name} (레벨 {j.RequiredLevel})", w))
                    {
                        PixelText.Draw(this, row, new Vector2(dx, ty), Ui.Dim);
                        ty += 14;
                    }
                }
            }
        }
        // 행적: 걸어온 길 (세상은 이것을 보고 이름을 붙인다 — 무엇이 무엇이 되는지는 적지 않는다)
        float by = box.End.Y - 62;
        DrawRect(new Rect2(o.X + 14, by - 12, box.Size.X - 28, 1), new Color(Ui.Gold, 0.25f));
        PixelText.Draw(this, "걸어온 길", new Vector2(o.X + 16, by + 4), Ui.Gold);
        (string label, string key)[] deeds =
        {
            ("쓰러뜨린 것", "kill"), ("무명", "kill_hushed"), ("밤 사냥", "kill_night"), ("받아넘김", "parry"), ("완벽 회피", "perfect_dodge"),
            ("되받아침", "reflect"), ("깨운 이름비", "stele"), ("깨운 여신상", "statue"), ("쓰러진 횟수", "death"),
        };
        float bx = o.X + 84;
        float lineY = by + 4;
        foreach (var (label, key) in deeds)
        {
            string text = $"{label} {_hero.Deed(key)}";
            float tw = PixelText.Width(text) + 14;
            if (bx + tw > box.End.X - 14)
            {
                bx = o.X + 84;
                lineY += 15;
            }
            PixelText.Draw(this, text, new Vector2(bx, lineY), Ui.Dim);
            bx += tw;
        }
        Ui.Hints(this, new Vector2(low.X * 0.5f, box.End.Y - 10), ("WASD", "이름 고르기"), ("Esc", "닫기"));
    }

    private void DrawMap(Vector2 low)
    {
        const float Side = 336f;
        var map = new Rect2(Mathf.Round(low.X * 0.5f - 300), 12, Side, Side);
        Ui.Frame(this, new Rect2(map.Position - Vector2.One * 3, map.Size + Vector2.One * 6));
        if (_mapTex != null)
            DrawTextureRect(_mapTex, map, false);
        var world = GameRoot.Instance.World;
        float k = Side / world.Width;
        Vector2 P(float x, float z) => (map.Position + new Vector2(x, z) * k).Round();
        // 무명지: 잿빛 원 / 깨운 이름비: 금빛 점
        int restored = 0, total = 0;
        if (Hush.Instance != null)
            foreach (var z in Hush.Instance.All())
            {
                total++;
                Vector2 c = P(z.At.X, z.At.Y);
                if (z.Restored)
                {
                    restored++;
                    DrawRect(new Rect2(c - Vector2.One * 2, new Vector2(5, 5)), Ui.Ink);
                    DrawRect(new Rect2(c - Vector2.One, new Vector2(3, 3)), Ui.Gold);
                    continue;
                }
                float r = z.Radius * k;
                DrawCircle(c, r, new Color(0.62f, 0.64f, 0.7f, 0.72f));
                DrawArc(c, r, 0, Mathf.Tau, 24, new Color(0.9f, 0.92f, 1f, 0.8f), 1f);
                DrawRect(new Rect2(c - Vector2.One, new Vector2(3, 3)), Ui.Ink);
            }
        if (world.Meta.TryGetProperty("statues", out var statues))
            foreach (var s in statues.EnumerateArray())
            {
                Vector2 c = P(s.GetProperty("x").GetSingle(), s.GetProperty("z").GetSingle());
                bool awake = _hero.Statues.Contains(s.GetProperty("id").GetString());
                DrawRect(new Rect2(c - Vector2.One * 2, new Vector2(5, 5)), Ui.Ink);
                DrawRect(new Rect2(c - Vector2.One, new Vector2(3, 3)), awake ? Ui.Blue : new Color(0.3f, 0.36f, 0.5f));
            }
        foreach (var n in NpcDef.All)
        {
            if (n.Role != "trainer")
                continue;
            Vector2 c = P(n.X, n.Z);
            var job = JobDef.Get(n.Job);
            DrawRect(new Rect2(c - new Vector2(5, 6), new Vector2(11, 12)), new Color(Ui.Ink, 0.8f));
            PixelText.DrawCentered(this, job.Glyph, c + new Vector2(0, 4), CombatFx.PaletteOf(job.Palette).Bright);
        }
        var story = Story.Instance?.Tracked;
        var marker = story != null ? Story.Instance.Current(story)?.Marker : null;
        if (marker is { Length: 2 } && Mathf.PosMod(_age, 0.9f) < 0.6f)
        {
            Vector2 c = P(marker[0], marker[1]);
            for (int dy = -4; dy <= 4; dy++)
            {
                int half = 4 - Mathf.Abs(dy);
                DrawRect(new Rect2(c + new Vector2(-half, dy), new Vector2(half * 2 + 1, 1)), dy == -4 || dy == 4 ? Ui.Ink : Ui.Gold);
            }
        }
        Vector2 me = P(_hero.GlobalPosition.X, _hero.GlobalPosition.Z);
        DrawRect(new Rect2(me - Vector2.One * 3, new Vector2(7, 7)), Ui.Ink);
        DrawRect(new Rect2(me - Vector2.One * 2, new Vector2(5, 5)), Mathf.PosMod(_age, 0.6f) < 0.3f ? Colors.White : Ui.Red);

        // 오른쪽: 범례와 지금 있는 곳
        var side = new Rect2(map.End.X + 14, 12, low.X - map.End.X - 26, Side);
        Ui.Frame(this, side);
        Ui.Title(this, side, "하루미 대륙");
        float x = side.Position.X + 14, y = side.Position.Y + 34;
        string zone = world.ZoneAt(_hero.GlobalPosition.X, _hero.GlobalPosition.Z) ?? "길 위";
        PixelText.Draw(this, "지금 있는 곳", new Vector2(x, y), Ui.Dim);
        PixelText.Draw(this, zone, new Vector2(x, y + 15), Ui.Text);
        y += 42;
        PixelText.Draw(this, $"깨운 이름비  {restored} / {total}", new Vector2(x, y), Ui.Gold);
        Ui.Bar(this, new Rect2(x, y + 6, side.Size.X - 28, 4), total > 0 ? restored / (float)total : 0f, Ui.Gold, Ui.GoldDim);
        y += 34;
        void Legend(Color c, string label)
        {
            DrawRect(new Rect2(x, y - 8, 7, 7), Ui.Ink);
            DrawRect(new Rect2(x + 1, y - 7, 5, 5), c);
            PixelText.Draw(this, label, new Vector2(x + 14, y), Ui.Text);
            y += 17;
        }
        Legend(Colors.White, "나");
        Legend(Ui.Gold, "이야기가 가리키는 곳 · 깨운 이름비");
        Legend(new Color(0.62f, 0.64f, 0.7f), "무명지 (색을 잃은 땅)");
        Legend(Ui.Blue, "깨운 여신상 (빠른 이동)");
        Legend(new Color(0.3f, 0.36f, 0.5f), "잠든 여신상");
        y += 6;
        PixelText.Draw(this, "誓 墨 風 呼  이름지기", new Vector2(x, y), Ui.Dim);
        y += 26;
        foreach (string row in Ui.Wrap("길은 한가운데 거울 호수에서 사방으로 갈린다. 멀리 갈수록, 잿빛이 짙을수록 사납다.", side.Size.X - 28))
        {
            PixelText.Draw(this, row, new Vector2(x, y), Ui.Faint);
            y += 14;
        }
        Ui.Hints(this, new Vector2(side.GetCenter().X, side.End.Y - 10), ("Esc · M", "닫기"));
    }

    private void DrawHelp(Vector2 low)
    {
        var box = new Rect2(low.X * 0.5f - 210, 40, 420, 46 + HelpKeys.Length * 18);
        Ui.Frame(this, box);
        Ui.Title(this, box, "조작법");
        for (int i = 0; i < HelpKeys.Length; i++)
        {
            float y = box.Position.Y + 34 + i * 18;
            float w = Ui.KeyCap(this, new Vector2(box.Position.X + 24, y), HelpKeys[i].key);
            PixelText.Draw(this, HelpKeys[i].label, new Vector2(box.Position.X + 118, y), Ui.Text);
        }
        Ui.Hints(this, new Vector2(low.X * 0.5f, box.End.Y - 8), ("Esc", "돌아가기"));
    }

    private void DrawSettings(Vector2 low)
    {
        var box = new Rect2(low.X * 0.5f - 160, 96, 320, 150);
        Ui.Frame(this, box);
        Ui.Title(this, box, "설정");
        var s = Settings.Instance;
        (string label, string value, float bar)[] rows =
        {
            ("전체 음량", s.Volume.ToString(), s.Volume / 10f),
            ("화면 흔들림", s.Shake == 0 ? "끔" : s.Shake.ToString(), s.Shake / 10f),
            ("전체 화면", s.Fullscreen ? "켬" : "끔", -1f),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = box.Position.Y + 40 + i * 28;
            bool on = _pick == i;
            if (on)
                Ui.Highlight(this, new Rect2(box.Position.X + 8, y - 14, box.Size.X - 16, 22));
            PixelText.Draw(this, rows[i].label, new Vector2(box.Position.X + 22, y), on ? Ui.Gold : Ui.Text);
            float vx = box.Position.X + 200;
            if (rows[i].bar >= 0f)
                Ui.Bar(this, new Rect2(vx - 50, y - 7, 100, 5), rows[i].bar, Ui.Gold, Ui.GoldDim);
            PixelText.DrawCentered(this, rows[i].value, new Vector2(rows[i].bar >= 0f ? vx + 72 : vx, y), on ? Colors.White : Ui.Dim);
            if (on)
            {
                PixelText.Draw(this, "◀", new Vector2(vx - 68, y), Ui.Gold);
                PixelText.Draw(this, "▶", new Vector2(vx + 86, y), Ui.Gold);
            }
        }
        Ui.Hints(this, new Vector2(low.X * 0.5f, box.End.Y - 10), ("W S", "고르기"), ("A D", "바꾸기"), ("Esc", "돌아가기"));
    }

    // ── 캡처용: --menu=codex 로 화면을 열어 둔다 ──────────────

    public void OpenForCapture(string name)
    {
        Screen s = name switch
        {
            "title" => Screen.Title, "pause" => Screen.Pause, "journal" => Screen.Journal, "codex" => Screen.Codex,
            "map" => Screen.Map, "help" => Screen.Help, "settings" => Screen.Settings, _ => Screen.None,
        };
        if (s == Screen.Title)
            OpenTitle();
        else if (s != Screen.None)
            Show(s, Screen.None);
    }
}
