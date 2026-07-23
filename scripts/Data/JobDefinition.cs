using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PixelMmo.Data;

/// <summary>
/// data/jobs/*.json 1:1 매핑. 코드에 직업 수치를 하드코딩하지 않는다. (CLAUDE.md 규칙 1)
/// </summary>
public sealed class JobDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 1;

    /// <summary>히든 직업. 조건은 게임 내 어디에도 노출하지 않는다. (규칙 4)</summary>
    public bool Hidden { get; set; }

    public int MaxRank { get; set; } = 10;

    /// <summary>전직 시 다음 직업으로 들고 갈 수 있는 스킬 슬롯 수.</summary>
    public int CarrySlots { get; set; } = 2;

    public string Description { get; set; } = "";

    public JobUnlock Unlock { get; set; } = new();

    /// <summary>랭크 1당 붙는 스탯 증가량. stat 이름 → 증가량.</summary>
    public Dictionary<string, int> StatPerRank { get; set; } = new();

    /// <summary>
    /// index i = 랭크 (i+1) 에 도달하는 데 필요한 누적 직업 경험치.
    /// 따라서 [0] 은 항상 0 이고 길이는 MaxRank.
    /// </summary>
    public List<int> ExpPerRank { get; set; } = new();

    /// <summary>랭크 문자열 → 그 랭크에서 지급되는 스킬 id 목록.</summary>
    public Dictionary<string, List<string>> RankSkills { get; set; } = new();

    /// <summary>
    /// P1 전용. 랭크와 무관하게 시작부터 쥐어 주는 스킬.
    /// 전투 손맛을 4개 키로 검증해야 하는데 랭크 성장이 아직 없어서 둔 임시 필드다.
    /// P2 에서 전직·랭크가 붙으면 이 배열은 통째로 지운다.
    /// </summary>
    public List<string> PrototypeSkills { get; set; } = new();

    /// <summary>특정 랭크로 넘어가기 위한 추가 관문(플래그/카운터).</summary>
    public List<JobRankGate> RankGates { get; set; } = new();

    /// <summary>rank 이하의 모든 랭크에서 지급되는 스킬 id 를 순서대로 모은다.</summary>
    public IEnumerable<string> SkillsUpToRank(int rank)
    {
        for (int r = 1; r <= rank; r++)
        {
            if (!RankSkills.TryGetValue(r.ToString(), out var ids) || ids == null)
                continue;
            foreach (string id in ids)
                yield return id;
        }
    }

    public JobRankGate GateForRank(int rank)
    {
        foreach (var gate in RankGates)
        {
            if (gate.Rank == rank)
                return gate;
        }
        return null;
    }
}

public sealed class JobUnlock
{
    public List<string> RequiredFlags { get; set; } = new();
    public Dictionary<string, int> RequiredCounters { get; set; } = new();
    public Dictionary<string, int> RequiredJobRanks { get; set; } = new();
}

public sealed class JobRankGate
{
    public int Rank { get; set; }
    public List<string> RequiredFlags { get; set; } = new();
    public Dictionary<string, int> RequiredCounters { get; set; } = new();

    /// <summary>플레이어에게 보여줄 수 있는 문구. 히든 직업에는 비워 둔다. (규칙 4)</summary>
    public string Hint { get; set; } = "";
}

/// <summary>세이브에 들어가는 직업 이력 1건.</summary>
public sealed class JobRecord
{
    public string JobId { get; set; } = "";
    public int Rank { get; set; } = 1;
    public int JobExp { get; set; }
    public int MaxRankReached { get; set; } = 1;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrEmpty(JobId);
}
