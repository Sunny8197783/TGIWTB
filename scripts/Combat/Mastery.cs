using System;
using System.Collections.Generic;
using System.Linq;
using PixelMmo.Data;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>RegisterUse 결과. Gain 이 0 이면 무효 사용이고 Invalid 에 사유가 담긴다.</summary>
public readonly record struct MasteryResult(float Gain, float Total, string EvolvedInto, string Invalid);

/// <summary>
/// 숙련·진화·히든 습득 (archive/astra-3d MasteryTracker 이식).
/// 규칙 3: '쓴 횟수'가 아니라 '유효한 사용'만 센다 — 산 적에게 맞은 시전만 여기로 온다 (SkillRunner).
/// 매크로 방어: 최소 간격 미만 연타 → 0, 같은 적만 계속 → 6회차부터 반감, 너무 작아지면 0.
/// 규칙 4: 히든 스킬의 조건(counter·횟수·체력)은 data/skills 에만 있고 화면 어디에도 나오지 않는다.
/// </summary>
public sealed class Mastery
{
    private sealed class Entry
    {
        public float Value;
        public double LastAt = double.NegativeInfinity;
        public ulong Target;
        public int Streak;
        public double TargetAt = double.NegativeInfinity;
    }

    private readonly Dictionary<string, Entry> _skills = new();
    private readonly Dictionary<string, int> _counters = new();   // 히든 스킬 id → 조건 충족 횟수
    private readonly HashSet<string> _learned = new();
    private readonly SkillDef[] _hidden;

    public Mastery(IEnumerable<SkillDef> defs) => _hidden = defs.Where(d => d.Hidden && d.Learn != null).ToArray();

    public float Get(string id) => _skills.TryGetValue(id, out var e) ? e.Value : 0f;

    /// <summary>숙련 배수 — 스킬 피해에 곱한다.</summary>
    public float Multiplier(SkillDef def) => 1f + Get(def.Id) / 100f * (def.Mastery?.DamageBonusPer100 ?? 0f);

    /// <summary>진화까지 진행도 0~1. 진화가 없는 스킬은 -1.</summary>
    public float Progress(SkillDef def)
    {
        var evo = def?.Mastery?.Evolution;
        return evo == null || evo.At <= 0f ? -1f : Math.Clamp(Get(def.Id) / evo.At, 0f, 1f);
    }

    /// <summary>유효한 시전 1회를 기록한다. 진화 임계값을 이번에 넘었으면 EvolvedInto 에 다음 스킬 id.</summary>
    public MasteryResult Use(SkillDef def, ulong target, double now)
    {
        var e = GetOrCreate(def.Id);
        float minInterval = def.Mastery?.MinInterval > 0f ? def.Mastery.MinInterval : T.MasteryMinInterval;
        if (now - e.LastAt < minInterval)
            return new(0f, e.Value, null, "interval");

        bool same = e.Target == target && now - e.TargetAt <= T.MasteryStreakReset;
        e.Streak = same ? e.Streak + 1 : 1;
        e.Target = target;
        e.TargetAt = now;
        float gain = T.MasteryGain * SameTargetDecay(e.Streak);
        if (gain < T.MasteryMinGain)
            return new(0f, e.Value, null, $"sameTarget x{e.Streak}");

        float before = e.Value;
        e.LastAt = now;
        e.Value += gain;
        var evo = def.Mastery?.Evolution;
        // 넘는 순간 한 번만 — 불러온 세이브가 이미 넘어 있으면 다시 진화하지 않는다
        string into = evo != null && evo.At > 0f && before < evo.At && e.Value >= evo.At ? evo.Into : null;
        return new(gain, e.Value, into, null);
    }

    /// <summary>연타 n회차 감쇠: 여유 횟수까지 1, 이후 반감 주기마다 절반.</summary>
    public static float SameTargetDecay(int streak)
    {
        int over = streak - T.MasterySameTargetFree;
        return over <= 0 ? 1f : MathF.Pow(0.5f, over / T.MasterySameTargetHalfLife);
    }

    /// <summary>진화로 스킬이 바뀌면 숙련을 그대로 넘긴다 — 0 부터 다시면 성장이 끊긴 것 같다.</summary>
    public void Transfer(string from, string to)
    {
        if (!_skills.Remove(from, out var e))
            return;
        var dst = GetOrCreate(to);
        dst.Value = Math.Max(dst.Value, e.Value);
    }

    /// <summary>게임 사건 하나 (perfect_dodge, parry …). 조건이 맞는 히든 스킬의 횟수를 올리고, 채우면 습득한 스킬을 돌려준다.</summary>
    public SkillDef Count(string counter, float hpRatio)
    {
        SkillDef learned = null;
        foreach (var def in _hidden)
        {
            if (_learned.Contains(def.Id) || def.Learn.Counter != counter || hpRatio > def.Learn.HpRatioAtMost)
                continue;
            _counters.TryGetValue(def.Id, out int n);
            _counters[def.Id] = ++n;
            if (n >= def.Learn.Required && _learned.Add(def.Id))
                learned = def;
        }
        return learned;
    }

    /// <summary>익힌 패시브 중 가장 좋은 값 (없으면 기본값).</summary>
    public float Passive(Func<SkillDef.PassiveDef, float> pick, float baseline)
    {
        float v = baseline;
        foreach (var def in _hidden)
            if (def.Passive != null && _learned.Contains(def.Id))
                v = Math.Max(v, pick(def.Passive));
        return v;
    }

    private Entry GetOrCreate(string id)
    {
        if (!_skills.TryGetValue(id, out var e))
            _skills[id] = e = new Entry();
        return e;
    }

    // ── 세이브 ──────────────────────────────────────────

    public void Save(SaveData s)
    {
        s.Mastery = _skills.ToDictionary(p => p.Key, p => p.Value.Value);
        s.Counters = new Dictionary<string, int>(_counters);
        s.Learned = _learned.ToList();
    }

    public void Load(SaveData s)
    {
        foreach (var (id, v) in s.Mastery)
            GetOrCreate(id).Value = v;
        foreach (var (id, n) in s.Counters)
            _counters[id] = n;
        _learned.UnionWith(s.Learned);
    }

    public void Seed(string id, float value) => GetOrCreate(id).Value = value;

    /// <summary>`--selftest`: 판정 규칙이 깨지면 예외. 에디터 없이 `tools/godot.sh --headless -- --selftest`.</summary>
    public static void SelfCheck()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception($"[Mastery] 실패: {what}"); }

        var basic = new SkillDef
        {
            Id = "a",
            Mastery = new SkillDef.MasteryDef { MinInterval = 1f, DamageBonusPer100 = 0.5f, Evolution = new() { At = 3f, Into = "b" } },
        };
        var hidden = new SkillDef
        {
            Id = "h", Hidden = true,
            Learn = new SkillDef.LearnDef { Counter = "dodge", HpRatioAtMost = 0.3f, Required = 2 },
            Passive = new SkillDef.PassiveDef { ParryWindow = 0.3f },
        };
        var m = new Mastery(new[] { basic, hidden });

        Check(m.Use(basic, 1, 0.0).Gain == T.MasteryGain, "첫 사용은 유효");
        Check(m.Use(basic, 2, 0.5).Invalid == "interval", "최소 간격 미만 연타는 0");
        Check(m.Use(basic, 2, 2.0).EvolvedInto == null, "2 < 3 은 아직");
        var evo = m.Use(basic, 3, 4.0);
        Check(evo.EvolvedInto == "b", "3 을 넘는 순간 진화");
        Check(m.Use(basic, 4, 6.0).EvolvedInto == null, "진화는 한 번만");
        Check(Math.Abs(m.Multiplier(basic) - (1f + 4f / 100f * 0.5f)) < 1e-4f, "숙련 배수");

        // 같은 적만 치면 여유 횟수 뒤로 반감, 끝내 0
        var farm = new SkillDef { Id = "f", Mastery = new SkillDef.MasteryDef { MinInterval = 0.01f } };
        float last = 1f;
        MasteryResult r = default;
        for (int i = 1; i <= 40; i++)
        {
            r = m.Use(farm, 7, i);
            if (i == T.MasterySameTargetFree + 1)
                Check(r.Gain < last, "여유 횟수 뒤로 감쇠");
            last = r.Gain > 0f ? r.Gain : last;
        }
        Check(r.Gain == 0f, "같은 대상만 계속 치면 끝내 0");
        Check(m.Use(farm, 8, 41).Gain == T.MasteryGain, "다른 대상이면 다시 온전히");

        m.Transfer("a", "b");
        Check(m.Get("a") == 0f && m.Get("b") >= 4f, "진화하면 숙련을 넘긴다");

        Check(m.Count("dodge", 0.9f) == null && m.Count("dodge", 0.2f) == null, "체력 조건이 안 맞으면 안 센다");
        Check(m.Passive(p => p.ParryWindow, 0.15f) == 0.15f, "익히기 전엔 기본값");
        Check(m.Count("dodge", 0.1f)?.Id == "h", "두 번째 충족에서 습득");
        Check(m.Count("dodge", 0.1f) == null, "습득은 한 번만");
        Check(m.Passive(p => p.ParryWindow, 0.15f) == 0.3f, "패시브 적용");

        var save = new SaveData();
        m.Save(save);
        var m2 = new Mastery(new[] { basic, hidden });
        m2.Load(save);
        Check(m2.Get("b") == m.Get("b") && m2.Passive(p => p.ParryWindow, 0f) == 0.3f, "세이브 왕복");
        Godot.GD.Print("[Mastery] selftest OK");
    }
}
