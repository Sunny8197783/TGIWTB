using System;
using System.Collections.Generic;
using PixelMmo.Balance;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>스킬 1회 사용에 대한 판정 재료. 전투 코드가 채워서 넘긴다. (§F)</summary>
public struct SkillUseContext
{
    public string SkillId;

    /// <summary>실제로 대상에 맞았는가. 헛스윙은 숙련이 오르지 않는다.</summary>
    public bool Hit;

    /// <summary>맞은 시점에 대상이 살아 있었는가. 시체 때리기는 0.</summary>
    public bool TargetAlive;

    public int TargetLevel;
    public ulong TargetInstanceId;

    public int SelfLevel;
    public float SelfHpRatio;

    public double NowSeconds;
}

/// <summary>RegisterUse 결과. Gain 이 0 이면 무효 사용이고 InvalidReason 에 사유가 담긴다.</summary>
public readonly struct MasteryResult
{
    public string SkillId { get; init; }
    public float Gain { get; init; }
    public float Total { get; init; }
    public string InvalidReason { get; init; }

    /// <summary>non-null 이면 이 스킬이 진화 임계값을 넘었다는 뜻. (§F)</summary>
    public string EvolvedInto { get; init; }

    public int SameTargetStreak { get; init; }
    public float DecayFactor { get; init; }

    public bool IsValid => Gain > 0f;

    /// <summary>디버그 오버레이 로그 한 줄. (§I)</summary>
    public string ToLogLine()
    {
        if (!IsValid)
            return $"{SkillId} INVALID: {InvalidReason}";

        string suffix = DecayFactor < 1f
            ? $"(sameTarget x{SameTargetStreak} 감쇠)"
            : $"(mastery {Total:0.0})";
        return $"{SkillId} HIT +{Gain:0.00} {suffix}";
    }
}

/// <summary>
/// 숙련 저장소 + 유효성 판정. 매크로 방어 3종을 여기서 전부 처리한다.
///   1) 죽은 대상 → 0
///   2) 최소 간격 미만 연타 → 0
///   3) 같은 대상 반복 → 6회차부터 지수 감쇠
/// </summary>
public sealed class MasteryTracker
{
    private sealed class SkillRuntime
    {
        public float Value;
        public double LastCreditedAt = double.NegativeInfinity;
        public ulong LastTargetId;
        public int SameTargetStreak;
        public double LastTargetAt = double.NegativeInfinity;
        public bool EvolutionFired;
    }

    private readonly Dictionary<string, SkillRuntime> _skills = new();

    public event Action<MasteryResult> UseRegistered;

    public float Get(string skillId)
        => _skills.TryGetValue(skillId, out var s) ? s.Value : 0f;

    public IEnumerable<KeyValuePair<string, float>> All()
    {
        foreach (var pair in _skills)
            yield return new KeyValuePair<string, float>(pair.Key, pair.Value.Value);
    }

    /// <summary>숙련 배수. 데미지에 곱한다. (§E "숙련 배수 적용")</summary>
    public float DamageMultiplier(SkillDefinition def)
    {
        if (def?.Mastery == null)
            return 1f;
        return 1f + Get(def.Id) / 100f * def.Mastery.DamageBonusPer100;
    }

    /// <summary>진화 임계값 대비 진행도 0~1. 진화가 없는 스킬은 -1. (§I 게이지)</summary>
    public float EvolutionProgress(SkillDefinition def)
    {
        var evo = def?.Mastery?.Evolution;
        if (evo == null || evo.At <= 0f)
            return -1f;
        return Math.Clamp(Get(def.Id) / evo.At, 0f, 1f);
    }

    /// <summary>
    /// 스킬 1회 사용을 기록한다. 무효 사용이면 Gain 0 + 사유를 담아 돌려준다.
    /// </summary>
    public MasteryResult RegisterUse(SkillUseContext ctx, SkillDefinition def)
    {
        MasteryResult result = Evaluate(ctx, def);
        UseRegistered?.Invoke(result);
        return result;
    }

    private MasteryResult Evaluate(SkillUseContext ctx, SkillDefinition def)
    {
        string id = ctx.SkillId ?? def?.Id ?? "";

        if (def == null)
            return Invalid(id, "unknownSkill");

        // (0) 헛스윙 — 맞지 않으면 숙련은 오르지 않는다.
        if (!ctx.Hit)
            return Invalid(id, "miss");

        // (1) 죽은 대상 때리기.
        if (!ctx.TargetAlive)
            return Invalid(id, "deadTarget");

        var state = GetOrCreate(id);

        // (2) 최소 간격 미만 연타.
        float minInterval = def.Mastery?.MinInterval ?? MasteryTuning.DefaultMinInterval;
        if (minInterval <= 0f)
            minInterval = MasteryTuning.DefaultMinInterval;
        if (ctx.NowSeconds - state.LastCreditedAt < minInterval)
            return Invalid(id, "cooldown");

        // (3) 같은 대상 반복 — 연타 카운트 갱신.
        bool sameTarget = state.LastTargetId == ctx.TargetInstanceId
            && ctx.NowSeconds - state.LastTargetAt <= MasteryTuning.SameTargetStreakResetSeconds;
        state.SameTargetStreak = sameTarget ? state.SameTargetStreak + 1 : 1;
        state.LastTargetId = ctx.TargetInstanceId;
        state.LastTargetAt = ctx.NowSeconds;

        float decay = SameTargetDecay(state.SameTargetStreak);
        float levelFactor = LevelFactor(ctx.SelfLevel, ctx.TargetLevel);
        float gain = MasteryTuning.BaseGainPerValidUse * decay * levelFactor;

        if (gain < MasteryTuning.MinGain)
            return Invalid(id, $"decayFloor (sameTarget x{state.SameTargetStreak})");

        state.LastCreditedAt = ctx.NowSeconds;
        state.Value += gain;

        string evolvedInto = null;
        var evo = def.Mastery?.Evolution;
        if (evo != null && !state.EvolutionFired && evo.At > 0f
            && state.Value >= evo.At && !string.IsNullOrEmpty(evo.Into))
        {
            state.EvolutionFired = true;
            evolvedInto = evo.Into;
        }

        return new MasteryResult
        {
            SkillId = id,
            Gain = gain,
            Total = state.Value,
            InvalidReason = null,
            EvolvedInto = evolvedInto,
            SameTargetStreak = state.SameTargetStreak,
            DecayFactor = decay * levelFactor,
        };
    }

    /// <summary>연타 n회차의 감쇠 계수. Free 회까지 1.0, 이후 반감 주기마다 절반.</summary>
    public static float SameTargetDecay(int streak)
    {
        int over = streak - MasteryTuning.SameTargetFreeUses;
        if (over <= 0)
            return 1f;
        return (float)Math.Pow(0.5, over / MasteryTuning.SameTargetHalfLife);
    }

    private static float LevelFactor(int selfLevel, int targetLevel)
    {
        int diff = selfLevel - targetLevel - MasteryTuning.LevelDiffTolerance;
        if (diff <= 0)
            return 1f;
        float factor = 1f - diff * MasteryTuning.LevelDiffPenaltyPerLevel;
        return Math.Max(MasteryTuning.MinLevelFactor, factor);
    }

    private static MasteryResult Invalid(string id, string reason) => new()
    {
        SkillId = id,
        Gain = 0f,
        InvalidReason = reason,
        DecayFactor = 1f,
    };

    private SkillRuntime GetOrCreate(string id)
    {
        if (!_skills.TryGetValue(id, out var state))
        {
            state = new SkillRuntime();
            _skills[id] = state;
        }
        return state;
    }

    /// <summary>
    /// 진화로 스킬이 교체될 때 숙련을 그대로 넘긴다.
    /// 갈고닦은 결과가 진화인데 0 부터 다시 시작하면 성장이 끊긴 것처럼 느껴진다. (§K)
    /// </summary>
    public void Transfer(string fromSkillId, string toSkillId)
    {
        if (!_skills.TryGetValue(fromSkillId, out var from) || string.IsNullOrEmpty(toSkillId))
            return;

        var to = GetOrCreate(toSkillId);
        to.Value = Math.Max(to.Value, from.Value);
        _skills.Remove(fromSkillId);
    }

    // --- 세이브 연동 -------------------------------------------------------

    public Dictionary<string, float> Snapshot()
    {
        var result = new Dictionary<string, float>();
        foreach (var pair in _skills)
            result[pair.Key] = pair.Value.Value;
        return result;
    }

    public void Restore(Dictionary<string, float> values)
    {
        _skills.Clear();
        if (values == null)
            return;

        var db = GameDatabase.Instance;
        foreach (var pair in values)
        {
            var state = new SkillRuntime { Value = pair.Value };

            // 이미 진화 임계값을 넘긴 채로 저장된 스킬은 로드 시 다시 진화하지 않는다.
            var evo = db?.GetSkill(pair.Key)?.Mastery?.Evolution;
            if (evo != null && evo.At > 0f && pair.Value >= evo.At)
                state.EvolutionFired = true;

            _skills[pair.Key] = state;
        }
    }
}
