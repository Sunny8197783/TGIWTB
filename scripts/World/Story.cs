using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 이야기 실행기 (data/story/*.json — Data/StoryDef). 줄기마다 지금 단계의 목표를 지켜보다가, 채워지면 끝의 일들을 차례로 벌이고 다음 단계로 넘긴다.
/// 진행은 세이브 값에: story:&lt;줄기&gt; = 단계 번호, storyp:&lt;줄기&gt; = 이번 단계에서 센 수.
/// 대화·이름 짓기처럼 기다려야 하는 일은 줄을 세워 하나씩 (대화창이 닫혀야 다음 일).
///
/// 낭만 서사 (Crisis): '뿌리의 상처'에서 허가 주인공을 쓰러뜨리는 일격을 날리는 순간(또는 허의 체력이 절반일 때)
/// 주인공의 이름이 지워진다 — 화면의 색과 HUD 가 사라진다. 떠났던 나래가 돌아와 자기가 지어 준 그 이름을 부르면 색이 돌아온다.
/// </summary>
public partial class Story : Node
{
    public static Story Instance { get; private set; }

    private const float CheckEvery = 0.25f;
    private const string CrisisZone = "h_root";
    private const string DefaultName = "하람";
    private static readonly Color Gold = new(1f, 0.9f, 0.55f);

    private readonly Hero _hero;
    private readonly Queue<(StoryDef line, StoryDef.ActionDef act)> _queue = new();
    private float _check;
    private string _tracked;

    // 위기 연출
    private enum Crisis { None, Erasing, Silence, Called, Rising }
    private Crisis _crisis;
    private float _crisisT;
    private Enemy _boss;

    public Story(Hero hero)
    {
        Name = "Story";
        _hero = hero;
        Instance = this;
    }

    public Story() { }

    public override void _Ready()
    {
        _hero.DeedBook.Changed += OnDeed;
        if (Hush.Instance != null)
        {
            Hush.Instance.Restored += _ => { _check = 0f; _hero.DamageBoost = 1f; EndBossWatch(); };
            Hush.Instance.BossSpawned += OnBoss;
            Hush.Instance.EventFailed += _ => { _hero.DamageBoost = 1f; EndBossWatch(); };
        }
        _tracked = StoryDef.All.FirstOrDefault(Active)?.Id;
        // 개발: --crisis — 2초 뒤 주인공 앞에 허를 세우고 위기 연출을 바로 돌린다 (--autotalk=1 과 같이 쓴다)
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--crisis")
                _devCrisis = 2f;
            else if (a.StartsWith("--autotalk="))
                Dialogue.AutoAdvance = float.Parse(a.Substring(11), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private float _devCrisis = -1f;

    private void DevCrisis()
    {
        var def = MonsterDef.Get("hollow_stag");
        Vector3 at = _hero.GlobalPosition;
        var boss = MonsterSpawner.SpawnAt(def, new Vector2(at.X, at.Z - 5f), def.Level, false);
        OnBoss(CrisisZone, boss);
        StartCrisis();
    }

    // ── 상태 ────────────────────────────────────────────

    public int StepIndex(StoryDef s) => _hero.Var("story:" + s.Id);
    public bool Finished(StoryDef s) => StepIndex(s) >= s.Steps.Count;
    public bool Opened(StoryDef s) => s.Needs == null || _hero.HasFlag(s.Needs);
    public bool Active(StoryDef s) => Opened(s) && !Finished(s);
    public StoryDef.StepDef Current(StoryDef s) => Active(s) ? s.Steps[StepIndex(s)] : null;
    public int Progress(StoryDef s) => _hero.Var("storyp:" + s.Id);
    /// <summary>기다리는 일이 남았다 (대화 중) — 다른 창은 이때 열지 않는다</summary>
    public bool Busy => _queue.Count > 0 || Dialogue.Open || _crisis != Crisis.None;

    /// <summary>화면에 띄울 줄기 (없으면 null): 마지막으로 나아간 것, 없으면 차례가 가장 앞선 것</summary>
    public StoryDef Tracked
    {
        get
        {
            var s = StoryDef.All.FirstOrDefault(x => x.Id == _tracked);
            if (s == null || !Active(s))
                s = StoryDef.All.FirstOrDefault(Active);
            return s;
        }
    }

    public void Track(string id) => _tracked = id;

    /// <summary>할 일 한 줄 (셀 것이 있으면 '3/6' 을 붙인다)</summary>
    public string StepText(StoryDef s)
    {
        var step = Current(s);
        if (step == null)
            return null;
        var g = step.Goal;
        return g.Type switch
        {
            "kill" => $"{step.Text}  {Mathf.Min(Progress(s), g.Count)}/{g.Count}",
            "restore_any" => $"{step.Text}  {RestoredAmong(g.Any)}/{g.Count}",
            _ => step.Text,
        };
    }

    private static int RestoredAmong(List<string> ids) => ids?.Count(id => Hush.Instance?.IsRestored(id) == true) ?? 0;

    // ── 흐름 ────────────────────────────────────────────

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_devCrisis > 0f && (_devCrisis -= dt) <= 0f)
            DevCrisis();
        if (_crisis != Crisis.None)
        {
            TickCrisis(dt);
            return;
        }
        if (_queue.Count > 0)
        {
            if (!Dialogue.Open)
            {
                var (line, act) = _queue.Dequeue();
                Run(line, act);
            }
            return;
        }
        WatchBoss();
        _check -= dt;
        if (_check > 0f || Dialogue.Open || GameRoot.Instance.Cinematic)
            return;
        _check = CheckEvery;
        foreach (var s in StoryDef.All)
            if (Active(s) && GoalMet(s, Current(s).Goal))
            {
                Complete(s);
                return;   // 한 번에 하나씩 — 일들이 섞이지 않게
            }
    }

    private bool GoalMet(StoryDef s, StoryDef.GoalDef g) => g.Type switch
    {
        "kill" => Progress(s) >= g.Count,
        "restore" => Hush.Instance?.IsRestored(g.Hush) == true,
        "restore_any" => RestoredAmong(g.Any) >= g.Count,
        "job" => g.Job != null ? _hero.JobId == g.Job : _hero.JobId != JobDef.Novice,
        "level" => _hero.Level >= g.Level,
        "flag" => _hero.HasFlag(g.Flag),
        "engrave" => _hero.EngraveStage(_hero.JobId) >= g.Count,
        "deed" => _hero.Deed(g.Deed) >= g.Count,
        "reach" => new Vector2(_hero.GlobalPosition.X - g.X, _hero.GlobalPosition.Z - g.Z).Length() <= g.R,
        _ => false,   // talk 는 말을 걸어야 끝난다
    };

    private void Complete(StoryDef s)
    {
        var step = Current(s);
        if (step == null)
            return;
        foreach (var a in step.OnDone ?? new List<StoryDef.ActionDef>())
            _queue.Enqueue((s, a));
        _hero.SetVar("storyp:" + s.Id, 0);
        _hero.SetVar("story:" + s.Id, StepIndex(s) + 1);
        _tracked = s.Id;
        Sfx.Play("confirmation", -8f, 1.2f, 0f, "UI");
        Hud.Toast(Finished(s) ? $"이야기 「{s.Name}」을 마쳤다" : $"「{s.Name}」 — 이야기가 나아갔다");
        var next = Current(s);
        foreach (var a in next?.OnStart ?? new List<StoryDef.ActionDef>())
            _queue.Enqueue((s, a));
        _check = 0f;
    }

    private void OnDeed(string key, int value)
    {
        foreach (var s in StoryDef.All)
        {
            var g = Current(s)?.Goal;
            if (g == null || g.Type != "kill")
                continue;
            if (key == (g.Monster != null ? "kill:" + g.Monster : "kill"))
                _hero.SetVar("storyp:" + s.Id, Progress(s) + 1);
        }
    }

    // ── 말 걸기 ─────────────────────────────────────────

    /// <summary>이 사람에게 말을 걸어야 넘어가는 단계가 있나 (머리 위 표시)</summary>
    public bool HasTalk(string npcId) => StoryDef.All.Any(s => Current(s)?.Goal is { Type: "talk" } g && g.Npc == npcId);

    /// <summary>말을 걸었다. 이야기가 받아서 대화를 띄웠으면 true (아니면 그 사람의 평소 말)</summary>
    public bool TalkTo(string npcId)
    {
        if (Busy)
            return true;
        foreach (var s in StoryDef.All)
        {
            var step = Current(s);
            if (step?.Goal is not { Type: "talk" } g || g.Npc != npcId)
                continue;
            var line = s;
            Dialogue.Show(Lines(npcId, step.Lines), () => Complete(line));
            return true;
        }
        return false;
    }

    private IEnumerable<Dialogue.Line> Lines(string speaker, List<string> lines)
    {
        var who = speaker != null ? NpcDef.All.FirstOrDefault(n => n.Id == speaker) : null;
        foreach (string text in lines ?? new List<string>())
            yield return who != null
                ? new Dialogue.Line(who.Name, who.Title, Fill(text), who.Art)
                : new Dialogue.Line(null, null, Fill(text));
    }

    private string Fill(string text) => text?.Replace("{name}", _hero.HeroName ?? "이름 없는 잎");

    // ── 일 ──────────────────────────────────────────────

    private void Run(StoryDef line, StoryDef.ActionDef a)
    {
        switch (a.Do)
        {
            case "say":
                Dialogue.Show(Lines(a.Speaker, a.Lines));
                break;
            case "name":
                var who = NpcDef.All.FirstOrDefault(n => n.Id == a.Speaker);
                Dialogue.AskName(new Dialogue.Line(who?.Name, who?.Title, a.Text, who?.Art), DefaultName, _hero.SetHeroName);
                break;
            case "flag":
                _hero.SetVar(a.Flag, a.Value);
                break;
            case "exp":
                _hero.GrantExp(a.Value);
                break;
            case "shards":
                _hero.AddShards(a.Value);
                Hud.Float(_hero.GlobalPosition + Vector3.Up * 2.6f, $"+{a.Value} 이름 조각", Gold);
                break;
            case "announce":
                Hud.Announce(Fill(a.Text), Fill(a.Sub), Gold);
                break;
            case "companion":
                Companion.Instance?.Set(a.Mode);
                break;
            case "heal":
                _hero.Heal(99999f);
                break;
            case "flask":
                _hero.SetVar("flask_bonus", _hero.Var("flask_bonus") + 1);
                _hero.RefillFlask();
                break;
            default:
                GD.PushWarning($"[Story] {line.Id}: 모르는 일 {a.Do}");
                break;
        }
    }

    // ── 위기: 이름이 지워진다 → 나래가 부른다 ────────────────

    private void OnBoss(string zone, Enemy boss)
    {
        if (zone != CrisisZone || _hero.HasFlag("narae_returned"))
            return;
        _boss = boss;
        // 쓰러뜨리는 일격이 곧 '이름을 지우는' 순간이 된다
        _hero.LastStand = () =>
        {
            if (_crisis != Crisis.None || _boss == null)
                return false;
            StartCrisis();
            return true;
        };
    }

    private void EndBossWatch()
    {
        _boss = null;
        _hero.LastStand = null;
    }

    private void WatchBoss()
    {
        if (_boss == null)
            return;
        if (!IsInstanceValid(_boss) || !_boss.Alive)
        {
            EndBossWatch();
            return;
        }
        // 너무 잘 싸워도 허는 이름을 지우려 든다
        if (_boss.HpRatio <= 0.5f && _hero.IsAlive)
            StartCrisis();
    }

    private void StartCrisis()
    {
        _crisis = Crisis.Erasing;
        _crisisT = 0f;
        _hero.LastStand = null;
        var root = GameRoot.Instance;
        root.Cinematic = true;
        _hero.Hidden = true;
        foreach (var e in Enemy.All)
            e.Frozen = true;
        CombatFx.Shake(0.8f);
        CombatFx.Flash(new Color(0.75f, 0.77f, 0.85f), 0.7f);
        Sfx.Play("impactBell_heavy", 0f, 0.35f, 0f, "UI");
        Sfx.SetMuffled(true);
        Dialogue.Show(Lines(null, new List<string>
        {
            "허의 빈 눈이 너를 본다.",
            "…이름이, 지워진다.",
            "손끝부터 색이 빠져나간다. 무엇이었는지, 누구였는지 —",
        }), () => { _crisis = Crisis.Silence; _crisisT = 0f; });
    }

    private void TickCrisis(float dt)
    {
        _crisisT += dt;
        switch (_crisis)
        {
            case Crisis.Erasing:
                SetErase(Mathf.Min(1f, _crisisT / 2.5f));
                break;
            case Crisis.Silence:
                SetErase(1f);
                if (_crisisT < 1.6f)
                    break;
                _crisis = Crisis.Called;
                _crisisT = 0f;
                string name = _hero.HeroName ?? "이름 없는 잎";
                var narae = NpcDef.All.FirstOrDefault(n => n.Id == "narae");
                Sfx.Play("impactBell_heavy", 2f, 1.5f, 0f, "UI");
                CombatFx.Flash(new Color(1f, 0.95f, 0.75f), 0.5f);
                Dialogue.Show(new[] { new Dialogue.Line("???", null, $"「{name}」——!!", null) }, () =>
                {
                    _crisis = Crisis.Rising;
                    _crisisT = 0f;
                    Rise(narae);
                });
                break;
            case Crisis.Called:
                SetErase(1f);
                break;
            case Crisis.Rising:
                SetErase(Mathf.Max(0f, 1f - _crisisT / 0.8f));
                break;
        }
    }

    /// <summary>색이 터져 나오듯 돌아오고, 나래가 달려 들어온다</summary>
    private void Rise(NpcDef narae)
    {
        var pal = CombatFx.PaletteOf("lantern");
        Vector3 at = _hero.GlobalPosition;
        CombatFx.Flash(Colors.White, 1f);
        CombatFx.Shockwave(at, 9f, 0.9f, pal);
        CombatFx.SparkleBurst(at + Vector3.Up, 90, 3f, pal);
        CombatFx.Shake(0.6f);
        Sfx.SetMuffled(false);
        Sfx.Play("confirmation", 0f, 0.8f, 0f, "UI");
        Sfx.Play("impactBell_heavy", 0f, 1f, 0f, "UI");
        _hero.Heal(99999f);
        _hero.RefillFlask();
        _hero.DamageBoost = 1.3f;
        Companion.Instance?.Return(at + new Vector3(2.5f, 0f, 7f));
        if (_boss != null && IsInstanceValid(_boss))
            _boss.Stagger(_boss.GlobalPosition - at);
        Dialogue.Show(new[]
        {
            new Dialogue.Line(narae?.Name ?? "나래", narae?.Title, "네 이름은 내가 지었어.", narae?.Art),
            new Dialogue.Line(narae?.Name ?? "나래", narae?.Title, "그러니까 — 내가 기억하는 한, 넌 안 지워져!", narae?.Art),
            new Dialogue.Line(narae?.Name ?? "나래", narae?.Title, Fill("일어나, {name}. 같이 끝내자."), narae?.Art),
        }, () =>
        {
            SetErase(0f);
            _crisis = Crisis.None;
            GameRoot.Instance.Cinematic = false;
            _hero.Hidden = false;
            foreach (var e in Enemy.All)
                e.Frozen = false;
            _hero.SetVar("narae_returned", 1);
            EndBossWatch();   // 한 번뿐인 장면
            Hud.Announce("나래가 돌아왔다", "등불이 네 이름을 비춘다 — 힘이 솟는다", Gold);
        });
    }

    private void SetErase(float k)
    {
        Hud.Erase = k;
        _hero.SetErased(k);
        CombatFx.Vignette(new Color(0.05f, 0.05f, 0.08f), k, 0.2f);
    }
}
