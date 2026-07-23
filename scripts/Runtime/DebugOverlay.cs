using System.Collections.Generic;
using System.Text;
using Godot;
using PixelMmo.Balance;
using PixelMmo.Combat;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>
/// F1 오버레이. P1 의 검증은 이것 없이는 불가능하다. (§I)
/// 좌상단 상태 + 하단 로그 패널. 게임 로직은 건드리지 않고 읽기만 한다.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private static readonly int FontSize = 7;
    private static readonly int GaugeCells = 10;
    private static readonly Color PanelBg = new(0f, 0f, 0f, 0.55f);

    private IPlayerContext _player;
    private Node2D _playerNode;
    private readonly DpsMeter _dps = new();

    private ColorRect _infoBg;
    private Label _info;
    private ColorRect _logBg;
    private Label _log;

    public void Bind(IPlayerContext player, Node2D playerNode)
    {
        _player = player;
        _playerNode = playerNode;
    }

    public override void _Ready()
    {
        Name = "DebugOverlay";
        Layer = 10;
        Visible = false;

        // 배경은 라벨이 실제로 쓰는 크기를 따라간다. 스킬이 늘면 정보 패널도 같이 커진다.
        _infoBg = new ColorRect { Color = PanelBg, MouseFilter = Control.MouseFilterEnum.Ignore };
        _infoBg.Position = new Vector2(2, 2);
        AddChild(_infoBg);

        _info = MakeLabel();
        _info.Position = new Vector2(5, 4);
        AddChild(_info);

        _logBg = new ColorRect { Color = PanelBg, MouseFilter = Control.MouseFilterEnum.Ignore };
        _logBg.Position = new Vector2(2, 250);
        _logBg.Size = new Vector2(400, 108);
        AddChild(_logBg);

        _log = MakeLabel();
        _log.Position = new Vector2(5, 252);
        _log.Size = new Vector2(396, 104);
        AddChild(_log);

        DebugLog.Changed += RefreshLog;

        if (CombatFeedback.Instance != null)
            CombatFeedback.Instance.DamageDealt += OnDamageDealt;

        RefreshLog();
    }

    public override void _ExitTree()
    {
        DebugLog.Changed -= RefreshLog;
        if (CombatFeedback.Instance != null)
            CombatFeedback.Instance.DamageDealt -= OnDamageDealt;
    }

    private static Label MakeLabel()
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", new Color(0.88f, 0.94f, 0.88f));
        return label;
    }

    public void Toggle()
    {
        Visible = !Visible;
        DebugFlags.ShowOverlay = Visible;
    }

    private void OnDamageDealt(float amount)
        => _dps.Record(amount, Time.GetTicksMsec() / 1000.0);

    public override void _Process(double delta)
    {
        if (!Visible || _player == null)
            return;

        _info.Text = BuildInfo();
        _infoBg.Size = _info.GetMinimumSize() + new Vector2(6, 4);
    }

    private string BuildInfo()
    {
        var db = GameDatabase.Instance;
        var text = new StringBuilder();

        Vector2 pos = _playerNode?.GlobalPosition ?? Vector2.Zero;
        var zone = WorldLayout.ZoneAt(pos);

        text.Append($"FPS {Engine.GetFramesPerSecond():0}");
        text.Append($"   HP {_player.Hp:0}/{_player.MaxHp:0}");
        text.Append($"   ({pos.X:0}, {pos.Y:0})  {zone?.DisplayName ?? "-"}\n");

        var job = _player.Jobs;
        text.Append($"{job.CurrentJobId} R{job.CurrentRank}   ");
        foreach (string stat in Stats.All)
            text.Append($"{stat}{_player.GetStat(stat)} ");
        text.Append('\n');

        text.Append($"DPS(60s) {_dps.Value(Time.GetTicksMsec() / 1000.0):0.0}");
        text.Append($"   튜닝 변경 {CombatTuning.ModifiedCount()}개\n");

        text.Append("-- 숙련 --\n");
        foreach (string skillId in job.LearnedSkills)
            AppendMasteryRow(text, db, skillId);

        text.Append($"flags: {string.Join(", ", VisibleFlags())}\n");
        text.Append($"counters: {FormatCounters()}");

        return text.ToString();
    }

    private void AppendMasteryRow(StringBuilder text, GameDatabase db, string skillId)
    {
        var skill = db?.GetSkill(skillId);
        float value = _player.Mastery.Get(skillId);

        text.Append($"  {skillId,-20} {value,7:0.0}");

        float progress = _player.Mastery.EvolutionProgress(skill);
        if (progress >= 0f)
        {
            int filled = Mathf.RoundToInt(progress * GaugeCells);
            text.Append("  [");
            for (int i = 0; i < GaugeCells; i++)
                text.Append(i < filled ? '#' : '-');
            text.Append($"] {skill.Mastery.Evolution.At:0}");
        }

        text.Append('\n');
    }

    /// <summary>
    /// 히든 스킬·직업의 존재를 오버레이에서도 흘리지 않는다.
    /// 개발용 화면이지만 규칙 4 를 여기서 깨면 스트리밍 한 번으로 새어 나간다.
    /// </summary>
    private IEnumerable<string> VisibleFlags()
    {
        var db = GameDatabase.Instance;
        var hiddenFlags = new HashSet<string>();

        if (db != null)
        {
            foreach (var job in db.Jobs.Values)
            {
                if (!job.Hidden)
                    continue;
                foreach (string flag in job.Unlock.RequiredFlags)
                    hiddenFlags.Add(flag);
            }
        }

        foreach (string flag in SortedFlags())
        {
            if (!hiddenFlags.Contains(flag))
                yield return flag;
        }
    }

    private IEnumerable<string> SortedFlags()
    {
        var list = new List<string>();
        if (_player is PlayerCharacter concrete)
            list.AddRange(concrete.Flags);
        list.Sort();
        return list;
    }

    /// <summary>히든 스킬의 습득 카운터는 오버레이에도 띄우지 않는다. (규칙 4)</summary>
    private string FormatCounters()
    {
        if (_player is not PlayerCharacter concrete)
            return "-";

        var hiddenCounters = new HashSet<string>();
        var db = GameDatabase.Instance;
        if (db != null)
        {
            foreach (var skill in db.Skills.Values)
            {
                if (skill.Hidden && !string.IsNullOrEmpty(skill.LearnTrigger?.Counter))
                    hiddenCounters.Add(skill.LearnTrigger.Counter);
            }
        }

        var parts = new List<string>();
        foreach (var pair in concrete.Counters)
        {
            if (!hiddenCounters.Contains(pair.Key))
                parts.Add($"{pair.Key}={pair.Value}");
        }
        parts.Sort();
        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private void RefreshLog()
        => _log.Text = string.Join("\n", DebugLog.Recent(DebugLog.VisibleLines));
}
