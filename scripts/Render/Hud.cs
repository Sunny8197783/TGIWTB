using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.World;

namespace PixelMmo.Render;

/// <summary>
/// 화면 위 정보 (M9 개편). 전부 저해상도 픽셀 단위로 그려 월드와 같은 픽셀 크기로 보인다 (부품은 Render/Ui).
///  왼쪽 위  이름의 문장 + 이름·레벨·각인, 체력(늦게 따라오는 잔상)·경험치, 이슬 병, 이름 조각
///  오른쪽 위 미니맵(길잡이·여신상·이름비) + 지역 이름·밤낮, 그 아래 지금의 이야기 한 줄
///  아래 가운데 기술 칸 (키 딱지·대기·숙련 줄), 왼쪽에 이슬 병 키
///  위 가운데  한 줄 알림, 이름비 싸움의 진행, 우두머리 체력
///  월드     적 이름표·체력, 튀어 오르는 피해 숫자·경험치·짧은 글
/// Erase(0~1): 이름이 지워지는 연출 — HUD 전체가 흐려져 사라진다.
/// </summary>
public partial class Hud : Control
{
    private static Hud _i;
    private IPlayerContext _player;
    private float _chip = 1f;      // 깎인 만큼 늦게 따라오는 흰 잔상
    private float _bossChip = 1f;
    private readonly List<(Vector3 at, int value, bool heavy, float age)> _numbers = new();
    private readonly List<(Vector3 at, int value, float age)> _exps = new();
    private readonly List<(Vector3 at, string text, Color color, float age)> _floats = new();
    private readonly List<(string text, float age)> _toasts = new();
    private float _expShown;   // 경험치 막대가 차오르는 중간값 (0..1)
    private string _announce, _announceSub;
    private Color _announceColor;
    private float _announceAge = 99f;
    private float _age;
    private Texture2D _map;

    private const float EnemyBarShow = 3f;   // 맞은 뒤 체력바를 띄워 두는 시간 (초)
    private const float NumberLife = 0.7f;
    private const float AnnounceLife = 3.8f;
    private const float ExpLife = 1.1f;
    private const float FloatLife = 1.3f;
    private const float ToastLife = 4.5f;
    private const int SlotSize = 24, SlotGap = 6;
    private const int MiniSize = 72;          // 미니맵 한 변 (px) = 144m (map_mini 는 2m = 1px)
    private const float MiniMetersPerPx = 2f;

    private static readonly Color HpHi = new(1f, 0.42f, 0.36f), HpLo = new(0.78f, 0.18f, 0.22f);
    private static readonly Color ExpHi = new(0.62f, 0.9f, 1f), ExpLo = new(0.25f, 0.55f, 0.85f);
    private static readonly Color ExpText = new(0.75f, 0.95f, 0.55f);
    private static readonly Color Dew = new(0.55f, 0.95f, 0.75f);
    private static readonly string[] SlotKeys = { "Q", "E", "R" };

    // 3x5 숫자 (위에서 아래로 한 줄씩, 1 = 칠함) — 피해 숫자
    private static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
    };

    /// <summary>이름이 지워지는 연출 (World/Story): 0 = 평소, 1 = HUD 가 모두 사라진다</summary>
    public static float Erase { get; set; }

    public static void Damage(Vector3 at, float value, bool heavy)
    {
        if (_i != null)
            _i._numbers.Add((at, Mathf.RoundToInt(value), heavy, 0f));
    }

    /// <summary>쓰러뜨린 적 머리 위로 떠오르는 경험치 (연두 '+8')</summary>
    public static void Exp(Vector3 at, int value)
    {
        if (_i != null)
            _i._exps.Add((at, value, 0f));
    }

    /// <summary>화면 위쪽 가운데 한 줄 알림 (진화·히든 습득·이름비). 조건은 절대 쓰지 않는다 (규칙 4).</summary>
    public static void Announce(string line, string sub, Color color)
    {
        if (_i == null || string.IsNullOrEmpty(line))
            return;
        _i._announce = line;
        _i._announceSub = sub;
        _i._announceColor = color;
        _i._announceAge = 0f;
    }

    /// <summary>머리 위로 떠오르는 짧은 글 ('!' · '깨뜨림!' · 나래의 한마디)</summary>
    public static void Float(Vector3 at, string text, Color color)
    {
        if (_i != null && !string.IsNullOrEmpty(text))
            _i._floats.Add((at, text, color, 0f));
    }

    /// <summary>왼쪽 아래에 잠깐 쌓이는 소식 (이야기가 나아갔다)</summary>
    public static void Toast(string text)
    {
        if (_i == null || string.IsNullOrEmpty(text))
            return;
        _i._toasts.Add((text, 0f));
        if (_i._toasts.Count > 4)
            _i._toasts.RemoveAt(0);
    }

    public override void _EnterTree() => _i = this;

    public override void _Ready()
    {
        Name = "Hud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest; // 한글 글자(PixelText)를 픽셀 그대로 키운다
        if (ResourceLoader.Exists("res://art/ui/map_mini.png"))
            _map = GD.Load<Texture2D>("res://art/ui/map_mini.png");
        Erase = 0f;
    }

    public void Bind(IPlayerContext player) => _player = player;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _announceAge += dt;
        if (_player != null)
        {
            float hp = _player.Hp / _player.MaxHp;
            _chip = Mathf.MoveToward(_chip, hp, dt * (_chip > hp ? 0.6f : 5f));
            float want = _player.ExpToNext > 0 ? _player.Exp / (float)_player.ExpToNext : 1f;
            _expShown = want < _expShown ? want : Mathf.MoveToward(_expShown, want, dt * 1.5f); // 레벨이 오르면 0 부터 다시
        }
        var boss = Hush.Instance?.Boss;
        _bossChip = boss == null ? 1f : Mathf.MoveToward(_bossChip, boss.HpRatio, dt * (_bossChip > boss.HpRatio ? 0.4f : 5f));
        Tick(_exps, dt, ExpLife, (n, a) => (n.at, n.value, a), n => n.age);
        Tick(_numbers, dt, NumberLife, (n, a) => (n.at, n.value, n.heavy, a), n => n.age);
        Tick(_floats, dt, FloatLife, (n, a) => (n.at, n.text, n.color, a), n => n.age);
        Tick(_toasts, dt, ToastLife, (n, a) => (n.text, a), n => n.age);
        Modulate = new Color(1f, 1f, 1f, 1f - Mathf.Clamp(Erase, 0f, 1f));
        QueueRedraw();
    }

    private static void Tick<T>(List<T> list, float dt, float life, System.Func<T, float, T> withAge, System.Func<T, float> age)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            float a = age(list[i]) + dt;
            if (a > life)
                list.RemoveAt(i);
            else
                list[i] = withAge(list[i], a);
        }
    }

    public override void _Draw()
    {
        var root = GameRoot.Instance;
        var view = root.View;
        float px = view.PixelScale;
        Vector2 low = Ui.LowSize(this, px);

        DrawWorld(view, px);
        if (_player == null || root.TitleOpen)
            return;
        Ui.Begin(this, px);
        DrawStatus();
        DrawMini(low);
        DrawTracker(low);
        DrawSlots(low);
        DrawTop(low);
        DrawToasts(low);
        Ui.End(this);
    }

    // ── 왼쪽 위: 나 ─────────────────────────────────────

    private void DrawStatus()
    {
        var job = JobDef.Get(_player.JobId);
        var pal = CombatFx.PaletteOf(job.Palette);
        Ui.Emblem(this, new Vector2(21, 23), job.Glyph, pal.Mid);
        string name = _player.HeroName ?? "이름 없는 잎";
        PixelText.Draw(this, name, new Vector2(38, 17), Ui.Text);
        int stage = _player.EngraveStage(job.Id);
        string sub = $"Lv {_player.Level}  {job.Name}" + (stage > 0 ? $" · {EngraveDef.Instance.Title(stage)}" : "");
        PixelText.Draw(this, sub, new Vector2(38 + PixelText.Width(name) + 8, 17), Ui.Dim);
        var hp = new Rect2(38, 22, 112, 7);
        Ui.Bar(this, hp, _player.Hp / _player.MaxHp, HpHi, HpLo, _chip);
        PixelText.Draw(this, $"{Mathf.CeilToInt(_player.Hp)}/{Mathf.RoundToInt(_player.MaxHp)}", new Vector2(hp.End.X + 6, 30), Ui.Dim);
        Ui.Bar(this, new Rect2(38, 32, 112, 2), _expShown, ExpHi, ExpLo);
        // 각인: 다음 획까지 (문장 아래 가는 줄)
        float eng = _player.EngraveProgress(job.Id);
        DrawRect(new Rect2(12, 36, 18, 1), Ui.Ink);
        DrawRect(new Rect2(12, 36, Mathf.RoundToInt(18 * eng), 1), pal.Bright);
        // 이슬 병과 이름 조각
        for (int i = 0; i < _player.FlaskMax; i++)
            Droplet(new Vector2(40 + i * 10, 39), i < _player.Flask);
        float sx = 40 + _player.FlaskMax * 10 + 8;
        Diamond(new Vector2(sx + 3, 43), Ui.HushGrey);
        PixelText.Draw(this, _player.Shards.ToString(), new Vector2(sx + 10, 48), Ui.Dim);
    }

    /// <summary>이슬 한 방울 (7x9)</summary>
    private void Droplet(Vector2 at, bool full)
    {
        string[] rows = { "0001000", "0011100", "0011100", "0111110", "1111111", "1111111", "1111111", "0111110", "0011100" };
        Color c = full ? Dew : new Color(0.2f, 0.22f, 0.25f);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 7; x++)
                if (rows[y][x] == '1')
                    DrawRect(new Rect2(at + new Vector2(x, y), Vector2.One), y < 4 || x < 2 || !full ? c : c.Darkened(0.25f));
        if (full)
            DrawRect(new Rect2(at + new Vector2(2, 5), Vector2.One), Colors.White);
    }

    private void Diamond(Vector2 center, Color c)
    {
        for (int y = -3; y <= 3; y++)
        {
            int half = 3 - Mathf.Abs(y);
            DrawRect(new Rect2(center + new Vector2(-half, y), new Vector2(half * 2 + 1, 1)), y < 0 ? c : c.Darkened(0.3f));
        }
    }

    // ── 오른쪽 위: 미니맵·이야기 ──────────────────────────

    private void DrawMini(Vector2 low)
    {
        var box = new Rect2(low.X - MiniSize - 12, 12, MiniSize, MiniSize);
        Vector3 at = _player.WorldPosition;
        Ui.Frame(this, new Rect2(box.Position - Vector2.One * 2, box.Size + Vector2.One * 4));
        if (_map != null)
        {
            // 주인공이 가운데 오게 잘라 그린다 (지도 밖은 바다색)
            DrawRect(box, new Color(0.12f, 0.3f, 0.55f));
            Vector2 c = new Vector2(at.X, at.Z) / MiniMetersPerPx;
            var src = new Rect2((c - Vector2.One * MiniSize * 0.5f).Round(), box.Size);
            var clip = src.Intersection(new Rect2(0, 0, _map.GetWidth(), _map.GetHeight()));
            if (clip.Size.X > 0 && clip.Size.Y > 0)
                DrawTextureRectRegion(_map, new Rect2(box.Position + (clip.Position - src.Position), clip.Size), clip);
        }
        // 무명지: 잿빛 원 (깨우면 금빛 점만)
        if (Hush.Instance != null)
            foreach (var z in Hush.Instance.All())
            {
                Vector2 p = MiniPoint(box, at, z.At, out bool inside);
                if (!z.Restored)
                {
                    float r = z.Radius / MiniMetersPerPx;
                    if (new Vector2(z.At.X - at.X, z.At.Y - at.Z).Length() < MiniSize + z.Radius)
                        DrawCircleClipped(box, p, r, new Color(0.75f, 0.77f, 0.85f, 0.55f));
                }
                if (inside)
                    DrawRect(new Rect2(p - Vector2.One, new Vector2(3, 3)), z.Restored ? Ui.Gold : Ui.HushGrey);
            }
        if (root().World.Meta.TryGetProperty("statues", out var statues))
            foreach (var s in statues.EnumerateArray())
            {
                Vector2 p = MiniPoint(box, at, new Vector2(s.GetProperty("x").GetSingle(), s.GetProperty("z").GetSingle()), out bool inside);
                if (inside)
                    DrawRect(new Rect2(p - Vector2.One, new Vector2(3, 3)),
                        _player.Statues.Contains(s.GetProperty("id").GetString()) ? Ui.Blue : new Color(0.3f, 0.4f, 0.55f));
            }
        foreach (var n in NpcDef.All)
        {
            if (n.Role == "companion")
                continue;
            Vector2 p = MiniPoint(box, at, new Vector2(n.X, n.Z), out bool inside);
            if (inside)
                DrawRect(new Rect2(p, new Vector2(2, 2)), Ui.Green);
        }
        // 길잡이: 지금 이야기의 자리 (밖이면 가장자리에 붙는다)
        var story = Story.Instance?.Tracked;
        var marker = story != null ? Story.Instance.Current(story)?.Marker : null;
        if (marker is { Length: 2 })
        {
            Vector2 p = MiniPoint(box, at, new Vector2(marker[0], marker[1]), out _);
            bool blink = Mathf.PosMod(_age, 0.9f) < 0.6f;
            Diamond(p, blink ? Ui.Gold : Colors.White);
        }
        // 나: 흰 점 + 바라보는 쪽
        Vector2 me = box.GetCenter().Round();
        Vector3 f = _player.Forward;
        DrawRect(new Rect2(me - Vector2.One * 2, new Vector2(5, 5)), Ui.Ink);
        DrawRect(new Rect2(me - Vector2.One, new Vector2(3, 3)), Colors.White);
        DrawRect(new Rect2((me + new Vector2(f.X, f.Z) * 3f).Round(), Vector2.One), Colors.White);

        // 지역 이름 + 밤낮
        string zone = Hush.Instance?.ActiveZoneAt(at.X, at.Z) is string h ? "무명지 · " + Hush.Instance.ZoneName(h) : root().World.ZoneAt(at.X, at.Z) ?? "하루미 대륙";
        bool night = root().DayCycle.IsNight;
        float w = PixelText.Width(zone);
        // 긴 이름은 오른쪽 끝을 지도 상자에 맞춰 왼쪽으로 민다 (화면 밖으로 잘리지 않게)
        float cx = Mathf.Min(box.GetCenter().X, box.End.X - 6f - w * 0.5f);
        PixelText.DrawCentered(this, zone, new Vector2(cx + 6, box.End.Y + 17), zone.StartsWith("무명지") ? Ui.HushGrey : Ui.Text);
        var icon = new Vector2(Mathf.Round(cx - w * 0.5f - 6), box.End.Y + 12);
        DrawCircle(icon, 4f, Ui.Ink);
        DrawCircle(icon, 3f, night ? new Color(0.8f, 0.86f, 1f) : new Color(1f, 0.85f, 0.35f));
        if (night)
            DrawCircle(icon + new Vector2(2, -1), 2.5f, Ui.Ink);
    }

    private static GameRoot root() => GameRoot.Instance;

    /// <summary>월드 자리 → 미니맵 점. 밖이면 가장자리에 붙이고 inside = false</summary>
    private static Vector2 MiniPoint(Rect2 box, Vector3 hero, Vector2 world, out bool inside)
    {
        Vector2 d = (world - new Vector2(hero.X, hero.Z)) / MiniMetersPerPx;
        float half = MiniSize * 0.5f - 3f;
        inside = Mathf.Abs(d.X) <= half && Mathf.Abs(d.Y) <= half;
        if (!inside)
            d *= half / Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y));
        return (box.GetCenter() + d).Round();
    }

    private void DrawCircleClipped(Rect2 box, Vector2 c, float r, Color col)
    {
        // 픽셀 고리: 미니맵 밖으로 나간 점은 버린다
        int n = Mathf.Max(12, (int)(r * 3f));
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.Tau;
            Vector2 p = (c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r).Round();
            if (box.HasPoint(p))
                DrawRect(new Rect2(p, Vector2.One), col);
        }
    }

    private void DrawTracker(Vector2 low)
    {
        var story = Story.Instance;
        var s = story?.Tracked;
        if (s == null)
            return;
        const float W = 168f;
        float x = low.X - W - 12, y = 12 + MiniSize + 30;
        var rows = Ui.Wrap(story.StepText(s), W - 10);
        float h = 18 + rows.Count * 13 + 4;
        DrawRect(new Rect2(x, y, W, h), new Color(Ui.Ink, 0.5f));
        DrawRect(new Rect2(x, y, 2, h), Ui.Gold);
        PixelText.Draw(this, $"「{s.Name}」", new Vector2(x + 7, y + 13), Ui.Gold);
        var marker = story.Current(s)?.Marker;
        if (marker is { Length: 2 })
        {
            Vector3 at = _player.WorldPosition;
            int dist = Mathf.RoundToInt(new Vector2(marker[0] - at.X, marker[1] - at.Z).Length());
            string d = $"{dist}m";
            PixelText.Draw(this, d, new Vector2(x + W - PixelText.Width(d) - 5, y + 13), Ui.Dim);
        }
        for (int i = 0; i < rows.Count; i++)
            PixelText.Draw(this, rows[i], new Vector2(x + 7, y + 27 + i * 13), Ui.Text);
    }

    // ── 아래: 기술 칸 ────────────────────────────────────

    private void DrawSlots(Vector2 low)
    {
        var skills = _player.Skills;
        int n = skills.Count;
        float total = n * SlotSize + (n - 1) * SlotGap;
        var origin = new Vector2(Mathf.Round((low.X - total) * 0.5f), low.Y - SlotSize - 22);
        for (int i = 0; i < n; i++)
        {
            string id = skills[i];
            var pos = origin + new Vector2(i * (SlotSize + SlotGap), 0f);
            var r = new Rect2(pos, new Vector2(SlotSize, SlotSize));
            DrawRect(new Rect2(pos - Vector2.One * 2, r.Size + Vector2.One * 4), Ui.Ink);
            if (id == null || !SkillDef.All.TryGetValue(id, out var def))
            {
                DrawRect(r, new Color(0.14f, 0.12f, 0.18f));
                DrawRect(new Rect2(pos - Vector2.One, r.Size + Vector2.One * 2), Ui.Faint, false, 1f);
                Ui.KeyCap(this, pos + new Vector2(SlotSize / 2 - 6, SlotSize + 15), SlotKeys[i], 0.4f);
                continue;
            }
            var pal = CombatFx.PaletteOf(def.Palette);
            float cd = _player.CooldownRemaining(id);
            bool ready = cd <= 0f;
            DrawRect(new Rect2(pos - Vector2.One, r.Size + Vector2.One * 2), ready ? Ui.Gold : Ui.GoldDim, false, 1f);
            DrawRect(r, pal.Deep);
            // 안쪽 무늬: 이름의 빛 — 마름모 셋이 겹친 문양
            for (int y = 0; y < SlotSize; y++)
            {
                int half = SlotSize / 2 - 2 - Mathf.Abs(y - SlotSize / 2);
                if (half <= 0)
                    continue;
                DrawRect(new Rect2(pos + new Vector2(SlotSize / 2 - half, y), new Vector2(half * 2, 1)), y < SlotSize / 2 ? pal.Mid : pal.Mid.Darkened(0.2f));
                int inner = half - 4;
                if (inner > 0)
                    DrawRect(new Rect2(pos + new Vector2(SlotSize / 2 - inner, y), new Vector2(inner * 2, 1)), y < SlotSize / 2 ? pal.Bright : pal.Bright.Darkened(0.15f));
            }
            DrawRect(new Rect2(pos + new Vector2(SlotSize / 2 - 1, SlotSize / 2 - 1), new Vector2(2, 2)), pal.Core);
            if (!ready)
            {
                int covered = Mathf.CeilToInt(SlotSize * Mathf.Clamp(cd / def.Cooldown, 0f, 1f));
                DrawRect(new Rect2(pos, new Vector2(SlotSize, covered)), new Color(0f, 0f, 0f, 0.68f));
                string left = cd >= 1f ? Mathf.CeilToInt(cd).ToString() : cd.ToString("0.0");
                PixelText.DrawCentered(this, left, pos + new Vector2(SlotSize / 2, SlotSize / 2 + 5), Colors.White);
            }
            // 숙련: 칸 바로 아래 한 줄이 진화까지 차오른다
            float mastery = _player.MasteryProgress(id);
            if (mastery >= 0f)
            {
                var bar = pos + new Vector2(0, SlotSize + 2);
                DrawRect(new Rect2(bar, new Vector2(SlotSize, 1)), Ui.Ink);
                DrawRect(new Rect2(bar, new Vector2(Mathf.RoundToInt(SlotSize * mastery), 1)), pal.Bright);
            }
            Ui.KeyCap(this, pos + new Vector2(SlotSize / 2 - 6, SlotSize + 17), SlotKeys[i]);
        }
        // 이슬 병: 기술 칸 왼쪽
        var fp = origin + new Vector2(-SlotSize - 14, 4);
        DrawRect(new Rect2(fp - Vector2.One * 2, new Vector2(20, 20)), Ui.Ink);
        DrawRect(new Rect2(fp - Vector2.One, new Vector2(18, 18)), _player.Flask > 0 ? Ui.GoldDim : Ui.Faint, false, 1f);
        Droplet(fp + new Vector2(4, 3), _player.Flask > 0);
        PixelText.Draw(this, _player.Flask.ToString(), fp + new Vector2(11, 16), _player.Flask > 0 ? Colors.White : Ui.Faint);
        Ui.KeyCap(this, fp + new Vector2(2, SlotSize + 13), "H", _player.Flask > 0 ? 1f : 0.4f);
    }

    // ── 위 가운데: 알림·싸움 진행·우두머리 ─────────────────

    private void DrawTop(Vector2 low)
    {
        float cx = low.X * 0.5f;
        float top = 14f;
        var boss = Hush.Instance?.Boss;
        if (boss != null)
        {
            // 우두머리: 이름 + 긴 체력 막대 (늦게 따라오는 잔상)
            PixelText.DrawCentered(this, boss.Def.Name, new Vector2(cx, top + 10), Ui.HushGrey);
            if (!string.IsNullOrEmpty(boss.Def.Title))
                PixelText.DrawCentered(this, boss.Def.Title, new Vector2(cx, top + 34), Ui.Dim, 0.9f);
            Ui.Bar(this, new Rect2(cx - 140, top + 14, 280, 7), boss.HpRatio, new Color(0.9f, 0.92f, 1f), new Color(0.5f, 0.52f, 0.62f), _bossChip);
            top += 42f;
        }
        string ev = Hush.Instance?.EventText;
        if (!string.IsNullOrEmpty(ev))
        {
            float w = PixelText.Width(ev) + 24f;
            var r = new Rect2(Mathf.Round(cx - w * 0.5f), top, Mathf.Round(w), 18);
            Ui.Frame(this, r, Ui.HushGrey);
            PixelText.DrawCentered(this, ev, new Vector2(cx, top + 13), Ui.HushGrey);
            top += 26f;
        }
        if (_announceAge >= AnnounceLife)
            return;
        // 톡 떨어지며 나타났다가 서서히 사라지는 두 줄
        float k = _announceAge;
        float alpha = Mathf.Min(k / 0.15f, 1f) * Mathf.Clamp((AnnounceLife - k) / 0.6f, 0f, 1f);
        int drop = Mathf.RoundToInt(6f * (1f - Mathf.Min(k / 0.2f, 1f)));
        bool two = !string.IsNullOrEmpty(_announceSub);
        float bandW = Mathf.Max(PixelText.Width(_announce), two ? PixelText.Width(_announceSub) : 0f) + 56f;
        float y = Mathf.Max(top + 6f, 50f) - drop;
        var band = new Rect2(Mathf.Round(cx - bandW * 0.5f), y, Mathf.Round(bandW), two ? 38 : 22);
        DrawRect(band, new Color(0.05f, 0.04f, 0.08f, 0.62f * alpha));
        // 위아래 금줄: 가운데가 짙고 양 끝이 옅다 (세 토막)
        foreach (float ly in new[] { band.Position.Y, band.End.Y - 1 })
        {
            DrawRect(new Rect2(band.Position.X, ly, band.Size.X, 1), new Color(_announceColor, 0.35f * alpha));
            DrawRect(new Rect2(band.Position.X + band.Size.X * 0.2f, ly, band.Size.X * 0.6f, 1), new Color(_announceColor, 0.85f * alpha));
        }
        PixelText.DrawCentered(this, _announce, new Vector2(cx, y + 15), _announceColor, alpha);
        if (two)
            PixelText.DrawCentered(this, _announceSub, new Vector2(cx, y + 31), Colors.White, alpha * 0.85f);
    }

    private void DrawToasts(Vector2 low)
    {
        for (int i = 0; i < _toasts.Count; i++)
        {
            var (text, age) = _toasts[_toasts.Count - 1 - i];
            float alpha = Mathf.Min(age / 0.2f, 1f) * Mathf.Clamp((ToastLife - age) / 0.8f, 0f, 1f);
            float slide = Mathf.Round(8f * (1f - Mathf.Min(age / 0.2f, 1f)));
            var pos = new Vector2(14 - slide, low.Y - 60 - i * 16);
            DrawRect(new Rect2(pos + new Vector2(-4, -11), new Vector2(PixelText.Width(text) + 12, 15)), new Color(Ui.Ink, 0.5f * alpha));
            DrawRect(new Rect2(pos + new Vector2(-4, -11), new Vector2(2, 15)), new Color(Ui.Gold, alpha));
            PixelText.Draw(this, text, pos, Ui.Text, alpha);
        }
    }

    // ── 월드에 붙는 것 ───────────────────────────────────

    private void DrawWorld(PixelView view, float px)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        Ui.Begin(this, px);
        foreach (var e in Enemy.All)
        {
            if (!e.Alive || e.Def.Boss)
                continue;
            bool hit = now - e.LastHitAt <= EnemyBarShow;
            if (!hit && !e.Engaged)
                continue;
            Vector2 head = (view.WorldToScreen(e.GlobalPosition + Vector3.Up * e.Def.HeadHeight) / px).Round();
            // 이름표: 레벨이 나보다 한참 높으면 붉게
            int gap = _player != null ? e.Level - _player.Level : 0;
            Color nameColor = e.Hushed ? Ui.HushGrey : gap >= 4 ? Ui.Red : gap <= -6 ? Ui.Faint : Ui.Text;
            PixelText.DrawCentered(this, $"Lv{e.Level} {e.DisplayName}", head + new Vector2(0, -4), nameColor, hit ? 1f : 0.75f);
            if (hit)
                Ui.Bar(this, new Rect2(head.X - 13, head.Y, 26, 3), e.HpRatio, HpHi, HpLo);
        }
        // 나래: 이름표, 기다리는 동안은 말 걸기
        var comp = Companion.Instance;
        if (comp != null && comp.Present && _player != null && !Dialogue.Open)
        {
            Vector2 head = (view.WorldToScreen(comp.GlobalPosition + Vector3.Up * 2.2f) / px).Round();
            if (comp.CanTalk || new Vector2(comp.GlobalPosition.X - _player.WorldPosition.X, comp.GlobalPosition.Z - _player.WorldPosition.Z).Length() < 10f)
                PixelText.DrawCentered(this, comp.DisplayName, head, new Color(1f, 0.93f, 0.7f), 0.9f);
            if (Story.Instance?.HasTalk("narae") == true)
            {
                float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 2f;
                PixelText.DrawCentered(this, "!", head + new Vector2(0, -14 - bob), Ui.Gold);
            }
            if (comp.CanTalk && !GameRoot.Instance.InputLocked)
                Npcs.Prompt(this, view, px, _player.WorldPosition, "대화");
        }
        foreach (var (at, value, age) in _exps)
        {
            float k = age / ExpLife;
            Vector2 p = view.WorldToScreen(at) / px - new Vector2(0, 10f * k + 4f);
            PixelText.DrawCentered(this, $"+{value} EXP", p.Round(), ExpText, Mathf.Clamp((1f - k) * 2.5f, 0f, 1f));
        }
        foreach (var (at, text, color, age) in _floats)
        {
            float k = age / FloatLife;
            // 톡 튀어 올랐다가 머문 뒤 사라진다
            float rise = 12f * Mathf.Sin(Mathf.Min(k * 4f, 1f) * Mathf.Pi * 0.5f);
            Vector2 p = view.WorldToScreen(at) / px - new Vector2(0, rise);
            PixelText.DrawCentered(this, text, p.Round(), color, Mathf.Clamp((1f - k) * 3f, 0f, 1f));
        }
        Ui.End(this);

        foreach (var (at, value, heavy, age) in _numbers)
        {
            float k = age / NumberLife;
            // 톡 튀어 올랐다가 살짝 가라앉으며 사라진다
            float rise = 14f * Mathf.Sin(Mathf.Min(k * 2.2f, 1f) * Mathf.Pi * 0.5f) - (k > 0.6f ? (k - 0.6f) * 8f : 0f);
            int scale = heavy ? 3 : 2;
            Color c = heavy ? new Color(1f, 0.85f, 0.3f) : Colors.White;
            if (k > 0.75f && Mathf.PosMod(age, 0.08f) < 0.04f)
                continue; // 사라지기 전 깜빡
            Vector2 p = view.WorldToScreen(at) - new Vector2(0, rise) * px;
            Number(Snap(p, px), value, scale, c, px);
        }
    }

    private void Number(Vector2 center, int value, int scale, Color c, float px)
    {
        string s = value.ToString();
        float cell = px * scale;
        float width = (s.Length * 4 - 1) * cell;
        Vector2 origin = center - new Vector2(width * 0.5f, 5 * cell * 0.5f);
        // 테두리 먼저 (한 픽셀 두께로 사방), 그다음 글자
        for (int pass = 0; pass < 2; pass++)
        {
            for (int d = 0; d < s.Length; d++)
            {
                if (s[d] < '0' || s[d] > '9')
                    continue;
                string g = Digits[s[d] - '0'];
                for (int y = 0; y < 5; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        if (g[y * 3 + x] != '1')
                            continue;
                        Vector2 p = origin + new Vector2(d * 4 + x, y) * cell;
                        if (pass == 0)
                            DrawRect(new Rect2(p - Vector2.One * px, new Vector2(cell + 2 * px, cell + 2 * px)), Ui.Ink);
                        else
                            DrawRect(new Rect2(p, new Vector2(cell, cell)), c);
                    }
                }
            }
        }
    }

    private static Vector2 Snap(Vector2 p, float px) => (p / px).Round() * px;
}
