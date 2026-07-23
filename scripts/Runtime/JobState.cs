using System.Collections.Generic;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>
/// 플레이어가 거쳐 온 직업 이력과 그로부터 파생되는 스탯/스킬.
/// P1 에서는 job_warrior R1 하나만 쓰지만, 구조는 P2 전직을 그대로 받는다.
/// </summary>
public sealed class JobState
{
    private readonly List<JobRecord> _history = new();
    private readonly List<string> _learnedSkills = new();
    private readonly List<string> _carriedSkillSlots = new();
    private readonly Dictionary<string, int> _inheritedStats = new();

    public string CurrentJobId { get; private set; } = "";

    public IReadOnlyList<JobRecord> History => _history;
    public IReadOnlyList<string> LearnedSkills => _learnedSkills;
    public IReadOnlyList<string> CarriedSkillSlots => _carriedSkillSlots;
    public IReadOnlyDictionary<string, int> InheritedStats => _inheritedStats;

    public JobRecord Current => FindRecord(CurrentJobId);
    public int CurrentRank => Current?.Rank ?? 0;

    /// <summary>
    /// 직업을 시작한다. 이미 이력에 있으면 그 기록을 이어받는다.
    /// 랭크 1 스킬을 실제로 지급한다. (§D-1)
    /// </summary>
    public void StartJob(string jobId, GameDatabase db)
    {
        var def = db?.GetJob(jobId);
        if (def == null)
            return;

        CurrentJobId = jobId;
        var record = FindRecord(jobId);
        if (record == null)
        {
            record = new JobRecord { JobId = jobId, Rank = 1, JobExp = 0, MaxRankReached = 1 };
            _history.Add(record);
        }

        GrantRankSkills(def, record.Rank);
    }

    /// <summary>해당 랭크까지의 스킬을 보유 목록에 채운다. 중복은 무시.</summary>
    public void GrantRankSkills(JobDefinition def, int rank)
    {
        foreach (string skillId in def.SkillsUpToRank(rank))
            Learn(skillId);
    }

    public bool Learn(string skillId)
    {
        if (string.IsNullOrEmpty(skillId) || _learnedSkills.Contains(skillId))
            return false;

        _learnedSkills.Add(skillId);
        if (!_carriedSkillSlots.Contains(skillId))
            _carriedSkillSlots.Add(skillId);
        return true;
    }

    public bool Has(string skillId) => _learnedSkills.Contains(skillId);

    /// <summary>숙련 진화로 스킬을 교체한다. 순서(슬롯 위치)를 유지한다.</summary>
    public bool Replace(string oldSkillId, string newSkillId)
    {
        int index = _learnedSkills.IndexOf(oldSkillId);
        if (index < 0 || string.IsNullOrEmpty(newSkillId))
            return false;

        _learnedSkills[index] = newSkillId;

        int slot = _carriedSkillSlots.IndexOf(oldSkillId);
        if (slot >= 0)
            _carriedSkillSlots[slot] = newSkillId;
        else
            _carriedSkillSlots.Add(newSkillId);

        return true;
    }

    /// <summary>
    /// 모든 직업 이력의 랭크 보너스 + 전직 시 승계된 스탯의 합.
    /// 플레이어 최종 스탯 = 기본 스탯 + 이 값. (§D-1)
    /// </summary>
    public int StatTotal(string stat)
    {
        var db = GameDatabase.Instance;
        int total = 0;

        if (db != null)
        {
            foreach (var record in _history)
            {
                var def = db.GetJob(record.JobId);
                if (def == null)
                    continue;
                if (def.StatPerRank.TryGetValue(stat, out int perRank))
                    total += perRank * record.Rank;
            }
        }

        if (_inheritedStats.TryGetValue(stat, out int inherited))
            total += inherited;

        return total;
    }

    public JobRecord FindRecord(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
            return null;
        foreach (var record in _history)
        {
            if (record.JobId == jobId)
                return record;
        }
        return null;
    }

    // --- 세이브 연동 -------------------------------------------------------

    public void RestoreFrom(string currentJobId, List<JobRecord> history, List<string> learned,
        List<string> carried, Dictionary<string, int> inherited)
    {
        _history.Clear();
        _learnedSkills.Clear();
        _carriedSkillSlots.Clear();
        _inheritedStats.Clear();

        CurrentJobId = currentJobId ?? "";

        if (history != null)
        {
            foreach (var record in history)
            {
                if (record != null && record.IsValid)
                    _history.Add(record);
            }
        }

        if (learned != null)
        {
            foreach (string id in learned)
            {
                if (!string.IsNullOrEmpty(id) && !_learnedSkills.Contains(id))
                    _learnedSkills.Add(id);
            }
        }

        if (carried != null)
        {
            foreach (string id in carried)
            {
                if (!string.IsNullOrEmpty(id) && !_carriedSkillSlots.Contains(id))
                    _carriedSkillSlots.Add(id);
            }
        }

        if (inherited != null)
        {
            foreach (var pair in inherited)
                _inheritedStats[pair.Key] = pair.Value;
        }
    }

    public List<JobRecord> CopyHistory() => new(_history);
    public List<string> CopyLearnedSkills() => new(_learnedSkills);
    public List<string> CopyCarriedSlots() => new(_carriedSkillSlots);
    public Dictionary<string, int> CopyInheritedStats() => new(_inheritedStats);
}
