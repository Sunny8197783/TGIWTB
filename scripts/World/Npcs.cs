using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 마을 사람 (data/npcs). 가까이 서서 F(패드 A)로 말을 건다 — 대화창은 Render/Dialogue 하나를 같이 쓴다.
///  - 이야기(World/Story)가 그 사람에게 걸린 단계를 갖고 있으면 그 말이 먼저다.
///  - 이름지기(role trainer): 이름을 받을 수 있으면 마지막에 '이름을 받는다 / 다음에' → 주인공 ChangeJob.
///  - 머리 위 표시: 금빛 '!' = 이야기가 기다린다, 또는 이름을 받을 수 있다.
/// 대화 중에는 사람이 주인공 쪽으로 돌아선다. 나래(role companion)는 World/Companion 이 맡는다.
/// </summary>
public partial class Npcs : Control
{
    private sealed class Actor
    {
        public NpcDef Def;
        public CharacterSprite Sprite;
        public Vector3 Feet;
    }

    private const float Reach = 2.6f;          // 말을 걸 수 있는 거리 (m)
    private const float NameRange = 10f;       // 이름표가 보이는 거리
    private const float FaceRange = 5f;        // 이 안에 들어오면 주인공을 바라본다
    private const float BodyRadius = 0.35f;
    private const string Placeholder = "res://art/characters/hero"; // 원화가 아직 없을 때

    private readonly WorldData _world;
    private readonly List<Actor> _actors = new();
    private IPlayerContext _player;
    private int _near = -1;
    private Actor _talking;
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
            if (def.Role == "companion")
                continue;
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
        var at = _player.WorldPosition;
        if (_talking != null && !Dialogue.Open && Story.Instance?.Busy != true)
            _talking = null;
        _near = -1;
        float best = Reach;
        for (int i = 0; i < _actors.Count; i++)
        {
            var a = _actors[i];
            float d = new Vector2(a.Feet.X - at.X, a.Feet.Z - at.Z).Length();
            // 가까이 오면 바라본다 (대화 중인 사람은 늘)
            if (a == _talking || d < FaceRange)
                a.Sprite.Dir = DirTo(a.Feet, at);
            a.Sprite.Advance(dt);
            a.Sprite.PlaceAt(a.Feet, a.Feet.Y);
            if (d < best)
            {
                best = d;
                _near = i;
            }
        }
        if (_near >= 0 && _talking == null && _player.IsAlive && !GameRoot.Instance.InputLocked && !Dialogue.Open
            && Input.IsActionJustPressed(Controls.Interact))
            Talk(_actors[_near]);
        QueueRedraw();
    }

    private static int DirTo(Vector3 from, Vector3 to)
    {
        float angle = Mathf.RadToDeg(Mathf.Atan2(to.X - from.X, to.Z - from.Z));
        return Mathf.PosMod(Mathf.RoundToInt(angle / 45f), 8);
    }

    // ── 대화 ────────────────────────────────────────────

    private void Talk(Actor a)
    {
        _talking = a;
        var def = a.Def;
        if (Story.Instance?.TalkTo(def.Id) == true)
            return;
        var job = def.Job != null ? JobDef.Get(def.Job) : null;
        string key;
        bool offer = false;
        if (def.Role == "trainer" && job != null)
        {
            if (_player.JobId == job.Id) key = "member";
            else if (_player.CanBecome(job)) { key = "offer"; offer = true; }
            else if (_player.JobId != JobDef.Novice) key = "other";
            else key = "greet";
        }
        else if (def.Role == "guide")
            key = _player.JobId != JobDef.Novice ? "after" : _player.Level >= MinJobLevel() ? "ready" : "greet";
        else
            key = "greet";
        if (!def.Lines.TryGetValue(key, out var lines) || lines.Count == 0)
            lines = def.Lines.Values.FirstOrDefault() ?? new List<string> { "…" };
        string jobName = job?.Name ?? _player.JobName;
        int level = job?.RequiredLevel ?? MinJobLevel();
        var shown = Say(def, lines, jobName, level);
        if (!offer)
        {
            Dialogue.Show(shown);
            return;
        }
        Dialogue.Show(shown, null, new[] { "이름을 받는다", "다음에" }, pick =>
        {
            if (pick != 0)
                return;
            _player.ChangeJob(def.Job);
            if (def.Lines.TryGetValue("accept", out var accept) && accept.Count > 0)
                Dialogue.Show(Say(def, accept, _player.JobName, level));
        });
    }

    private static IEnumerable<Dialogue.Line> Say(NpcDef def, List<string> lines, string jobName, int level) =>
        lines.Select(l => new Dialogue.Line(def.Name, def.Title, l.Replace("{level}", level.ToString()).Replace("{name}", jobName), def.Art)).ToList();

    private static int MinJobLevel() =>
        JobDef.All.Values.Where(j => j.RequiredLevel > 0 && !j.Sealed).Select(j => j.RequiredLevel).DefaultIfEmpty(1).Min();

    // ── 그리기 ──────────────────────────────────────────

    public override void _Draw()
    {
        if (_player == null || Hud.Erase > 0.5f)
            return;
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;
        Ui.Begin(this, px);
        var at = _player.WorldPosition;
        foreach (var a in _actors)
        {
            float d = new Vector2(a.Feet.X - at.X, a.Feet.Z - at.Z).Length();
            if (d > NameRange || Dialogue.Open)
                continue;
            Vector2 head = (view.WorldToScreen(a.Feet + Vector3.Up * 2.2f) / px).Round();
            PixelText.DrawCentered(this, a.Def.Title, head + new Vector2(0, -12), Ui.Dim, 0.9f);
            PixelText.DrawCentered(this, a.Def.Name, head, new Color(1f, 0.95f, 0.8f));
            var job = a.Def.Job != null ? JobDef.Get(a.Def.Job) : null;
            bool mark = Story.Instance?.HasTalk(a.Def.Id) == true
                || (a.Def.Role == "trainer" && job != null && _player.CanBecome(job));
            if (mark)
            {
                float bob = Mathf.PosMod(_age, 0.8f) < 0.4f ? 0f : 2f;
                PixelText.DrawCentered(this, "!", head + new Vector2(0, -26 - bob), Ui.Gold);
            }
        }
        if (_near >= 0 && !Dialogue.Open && !GameRoot.Instance.InputLocked)
            Prompt(this, view, px, _player.WorldPosition, "대화");
        Ui.End(this);
    }

    /// <summary>주인공 머리 위 'F 무엇' (여신상·이름비·사람이 같은 모양으로 쓴다). Ui.Begin 안에서 부른다</summary>
    public static void Prompt(CanvasItem ci, PixelView view, float px, Vector3 heroAt, string label)
    {
        Vector2 p = (view.WorldToScreen(heroAt + Vector3.Up * 2.4f) / px).Round();
        float w = 13f + 5f + PixelText.Width(label) + 12f;
        var r = new Rect2(Mathf.Round(p.X - w * 0.5f), p.Y - 15, Mathf.Round(w), 19);
        Ui.Frame(ci, r);
        Ui.KeyCap(ci, new Vector2(r.Position.X + 5, r.Position.Y + 14), "F");
        PixelText.Draw(ci, label, new Vector2(r.Position.X + 23, r.Position.Y + 14), Ui.Text);
    }
}
