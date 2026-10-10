using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 마을 사람과 대화 (data/npcs). 가까이 서서 F(패드 A)로 말을 건다.
///  - 대화창은 화면 아래, 한 글자씩 찍힌다. F·공격으로 넘기고, 마지막 줄에서 닫힌다.
///  - 교관: 전직할 수 있으면 마지막에 '전직한다 / 다음에' 를 고른다 → 주인공 ChangeJob.
///  - 전직할 수 있는 교관 머리 위엔 금빛 '!' 가 떠서 멀리서도 찾아간다.
/// 대화 중에는 주인공이 멈추고(MenuOpen), 사람은 주인공 쪽으로 돌아선다.
/// </summary>
public partial class Npcs : Control
{
    private sealed class Actor
    {
        public NpcDef Def;
        public CharacterSprite Sprite;
        public Vector3 Feet;
        public int HomeDir;
    }

    private const float Reach = 2.6f;          // 말을 걸 수 있는 거리 (m)
    private const float NameRange = 9f;        // 이름표가 보이는 거리
    private const float CharsPerSecond = 38f;  // 대사가 찍히는 빠르기
    private const float BodyRadius = 0.35f;
    private const string Placeholder = "res://art/characters/hero"; // 원화가 아직 없을 때

    private static readonly Color Back = new(0.06f, 0.05f, 0.09f, 0.9f);
    private static readonly Color Frame = new(0.95f, 0.86f, 0.62f);
    private static readonly Color Dim = new(0.72f, 0.68f, 0.62f);
    private static readonly Color Gold = new(1f, 0.85f, 0.35f);
    private static readonly Color NameColor = new(1f, 0.95f, 0.8f);

    private readonly WorldData _world;
    private readonly List<Actor> _actors = new();
    private IPlayerContext _player;
    private int _near = -1;

    // 대화
    private Actor _talking;
    private List<string> _lines;
    private int _line;
    private float _shown;          // 찍힌 글자 수
    private bool _choice;          // 마지막 줄 뒤에 고르기 (전직)
    private int _pick;             // 0 = 전직한다, 1 = 다음에
    private float _age;

    public Npcs(WorldData world)
    {
        Name = "Npcs";
        _world = world;
    }

    public Npcs() { }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        var stage = GameRoot.Instance.Stage;
        foreach (var def in NpcDef.All)
        {
            string art = ResourceLoader.Exists(def.Art + "/rot.png") ? def.Art : Placeholder;
            var a = new Actor { Def = def, Sprite = new CharacterSprite(art), Feet = new Vector3(def.X, _world.WalkHeightAt(def.X, def.Z), def.Z) };
            stage.AddChild(a.Sprite);
            a.Sprite.PlaceAt(a.Feet, a.Feet.Y);
            // 지나가다 몸이 겹치지 않게
            var body = new StaticBody3D { Name = "Npc_" + def.Id };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = BodyRadius, Height = 2f }, Position = Vector3.Up });
            stage.AddChild(body);
            body.GlobalPosition = a.Feet;
            _actors.Add(a);
        }
    }

    public void Bind(IPlayerContext player) => _player = player;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_player == null)
            return;
        foreach (var a in _actors)
        {
            // 대화 상대는 주인공을 본다, 아니면 제자리 방향
            a.Sprite.Dir = a == _talking ? DirTo(a.Feet, _player.WorldPosition) : a.HomeDir;
            a.Sprite.Advance(dt);
        }
        if (_talking != null)
        {
            TalkInput(dt);
            QueueRedraw();
            return;
        }
        _near = -1;
        float best = Reach;
        var at = _player.WorldPosition;
        for (int i = 0; i < _actors.Count; i++)
        {
            float d = new Vector2(_actors[i].Feet.X - at.X, _actors[i].Feet.Z - at.Z).Length();
            if (d < best)
            {
                best = d;
                _near = i;
            }
        }
        if (_near >= 0 && _player.IsAlive && !GameRoot.Instance.InputLocked && Input.IsActionJustPressed(Controls.Interact))
            Open(_actors[_near]);
        QueueRedraw();
    }

    private static int DirTo(Vector3 from, Vector3 to)
    {
        float angle = Mathf.RadToDeg(Mathf.Atan2(to.X - from.X, to.Z - from.Z));
        return Mathf.PosMod(Mathf.RoundToInt(angle / 45f), 8);
    }

    // ── 대화 ────────────────────────────────────────────

    private void Open(Actor a)
    {
        var def = a.Def;
        var job = def.Job != null ? JobDef.Get(def.Job) : null;
        string key;
        _choice = false;
        if (def.Role == "trainer" && job != null)
        {
            if (_player.JobId == job.Id) key = "member";
            else if (_player.CanBecome(job)) { key = "offer"; _choice = true; }
            else if (_player.JobId != JobDef.Novice) key = "other";
            else key = "greet";
        }
        else
            key = _player.JobId != JobDef.Novice ? "after" : _player.Level >= MinJobLevel() ? "ready" : "greet";
        if (!def.Lines.TryGetValue(key, out var lines) || lines.Count == 0)
            lines = def.Lines.Values.FirstOrDefault() ?? new List<string> { "..." };
        string jobName = job?.Name ?? _player.JobName;
        int level = job?.RequiredLevel ?? MinJobLevel();
        _lines = lines.Select(l => l.Replace("{level}", level.ToString()).Replace("{name}", jobName)).ToList();
        _talking = a;
        _line = 0;
        _shown = 0f;
        _pick = 0;
        _age = 0f;
        GameRoot.Instance.MenuOpen = true;
        Sfx.Play("select", -8f, 0.9f, 0f, "UI");
    }

    private static int MinJobLevel() =>
        JobDef.All.Values.Where(j => j.RequiredLevel > 0).Select(j => j.RequiredLevel).DefaultIfEmpty(1).Min();

    private void TalkInput(float dt)
    {
        string line = _lines[_line];
        float before = _shown;
        _shown = Mathf.Min(line.Length, _shown + CharsPerSecond * dt);
        if ((int)_shown != (int)before && (int)_shown % 3 == 0 && line[(int)_shown - 1] != ' ')
            Sfx.Play("click", -20f, 1.6f, 0.15f, "UI"); // 글자 찍히는 소리 (작게)
        bool last = _line == _lines.Count - 1;
        bool done = _shown >= line.Length;
        if (_choice && last && done)
        {
            if (Input.IsActionJustPressed(Controls.Up) || Input.IsActionJustPressed(Controls.Down))
            {
                _pick = 1 - _pick;
                Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
            }
        }
        bool next = Input.IsActionJustPressed(Controls.Interact) || Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed("ui_accept");
        bool cancel = Input.IsActionJustPressed(Controls.Dodge) || Input.IsActionJustPressed("ui_cancel");
        if (cancel)
        {
            Close();
            return;
        }
        if (!next)
            return;
        if (!done)
        {
            _shown = line.Length; // 다 찍기
            return;
        }
        if (!last)
        {
            _line++;
            _shown = 0f;
            return;
        }
        var a = _talking;
        bool change = _choice && _pick == 0;
        Close();
        if (change)
        {
            _player.ChangeJob(a.Def.Job);
            // 전직 축하 한마디
            if (a.Def.Lines.TryGetValue("accept", out var accept) && accept.Count > 0)
            {
                Open(a);
                _lines = accept.Select(l => l.Replace("{name}", _player.JobName)).ToList();
                _choice = false;
            }
        }
    }

    private void Close()
    {
        _talking = null;
        GameRoot.Instance.MenuOpen = false;
    }

    // ── 그리기 ──────────────────────────────────────────

    public override void _Draw()
    {
        if (_player == null)
            return;
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * px);
        var at = _player.WorldPosition;
        foreach (var a in _actors)
        {
            float d = new Vector2(a.Feet.X - at.X, a.Feet.Z - at.Z).Length();
            if (d > NameRange || a == _talking)
                continue;
            Vector2 head = (view.WorldToScreen(a.Feet + Vector3.Up * 2.2f) / px).Round();
            PixelText.DrawCentered(this, a.Def.Title, head + new Vector2(0, -12), Dim, 0.9f);
            PixelText.DrawCentered(this, a.Def.Name, head, NameColor);
            var job = a.Def.Job != null ? JobDef.Get(a.Def.Job) : null;
            if (a.Def.Role == "trainer" && job != null && _player.CanBecome(job))
            {
                float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 2f;
                PixelText.DrawCentered(this, "!", head + new Vector2(0, -26 - bob), Gold);
            }
        }
        if (_talking == null && _near >= 0 && !GameRoot.Instance.InputLocked)
        {
            Vector2 p = (view.WorldToScreen(_player.WorldPosition + Vector3.Up * 2.4f) / px).Round();
            const string label = "F  대화";
            float w = PixelText.Width(label) + 10f;
            var r = new Rect2(Mathf.Round(p.X - w * 0.5f), p.Y - 14, Mathf.Round(w), 17);
            DrawRect(r, Back);
            DrawRect(r, Frame, false, 1f);
            PixelText.DrawCentered(this, label, new Vector2(p.X, p.Y - 1), Colors.White);
        }
        if (_talking != null)
            DrawDialogue();
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>화면 아래 대화창: 이름표 + 한 글자씩 찍히는 줄, 마지막에 고르기</summary>
    private void DrawDialogue()
    {
        float px = GameRoot.Instance.View.PixelScale;
        Vector2 low = GetViewportRect().Size / px;
        float rise = Mathf.Round(6f * (1f - Mathf.Min(_age / 0.15f, 1f)));
        var box = new Rect2(Mathf.Round(low.X * 0.5f - 230), low.Y - 116 + rise, 460, 70); // 스킬 칸 위
        DrawRect(box, Back);
        DrawRect(box, Frame, false, 1f);
        DrawRect(new Rect2(box.Position + new Vector2(2, 2), box.Size - new Vector2(4, 4)), new Color(Frame, 0.3f), false, 1f);
        // 이름표
        string name = $"{_talking.Def.Name}  ·  {_talking.Def.Title}";
        var tag = new Rect2(box.Position + new Vector2(10, -12), new Vector2(PixelText.Width(name) + 14, 16));
        DrawRect(tag, Back);
        DrawRect(tag, Frame, false, 1f);
        PixelText.Draw(this, name, tag.Position + new Vector2(7, 12), Gold);
        // 줄 (길면 두 줄로 접는다)
        string line = _lines[_line][..(int)_shown];
        var rows = Wrap(line, box.Size.X - 28);
        for (int i = 0; i < rows.Count && i < 3; i++)
            PixelText.Draw(this, rows[i], box.Position + new Vector2(14, 24 + i * 15), Colors.White);
        bool done = _shown >= _lines[_line].Length;
        bool last = _line == _lines.Count - 1;
        if (_choice && last && done)
        {
            string[] opts = { "전직한다", "다음에" };
            for (int i = 0; i < 2; i++)
            {
                var r = new Rect2(box.End.X - 104, box.Position.Y - 46 + i * 20, 94, 18);
                bool on = _pick == i;
                DrawRect(r, on ? new Color(Gold, 0.9f) : Back);
                DrawRect(r, on ? Colors.White : Dim, false, 1f);
                PixelText.DrawCentered(this, opts[i], new Vector2(r.GetCenter().X, r.Position.Y + 13), on ? PixelText.Edge : Dim);
            }
        }
        else if (done)
        {
            // 다음 줄 표시 ▼ (숨 쉬듯)
            float bob = Mathf.PosMod(_age, 0.7f) < 0.35f ? 0f : 1f;
            PixelText.Draw(this, "▼", box.End - new Vector2(18, 8 - bob), Frame);
        }
    }

    private static List<string> Wrap(string s, float width)
    {
        var rows = new List<string>();
        string cur = "";
        foreach (string word in s.Split(' '))
        {
            string tryLine = cur.Length == 0 ? word : cur + " " + word;
            if (PixelText.Width(tryLine) > width && cur.Length > 0)
            {
                rows.Add(cur);
                cur = word;
            }
            else
                cur = tryLine;
        }
        rows.Add(cur);
        return rows;
    }
}
