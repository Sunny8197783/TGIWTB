using System.Collections.Generic;
using PixelMmo.Core;

namespace PixelMmo.Data;

/// <summary>
/// 조건식 — 이름(직업)이 열리는 조건, 이야기 단계가 넘어가는 조건에 같이 쓴다. 적힌 것을 모두 채워야 한다.
/// 히든 이름의 조건은 화면에 내보이지 않는다 (규칙 4): 이 클래스에는 글로 풀어 주는 기능을 두지 않는다.
/// </summary>
public sealed class Requirement
{
    public int Level { get; set; }
    /// <summary>지금 이름이 이 중 하나</summary>
    public List<string> Job { get; set; }
    /// <summary>거쳐 온 이름에 모두 있어야 한다</summary>
    public List<string> Held { get; set; }
    /// <summary>행적 이름 → 이 수 이상</summary>
    public Dictionary<string, int> Deeds { get; set; }
    /// <summary>이름 → 각인 단계 이상</summary>
    public Dictionary<string, int> Engrave { get; set; }
    /// <summary>이야기 표시 (Story 가 세운다)</summary>
    public List<string> Flags { get; set; }

    public bool Met(IPlayerContext p)
    {
        if (p.Level < Level)
            return false;
        if (Job is { Count: > 0 } && !Job.Contains(p.JobId))
            return false;
        if (Held != null)
            foreach (string j in Held)
                if (!p.HeldJob(j))
                    return false;
        if (Deeds != null)
            foreach (var (key, need) in Deeds)
                if (p.Deed(key) < need)
                    return false;
        if (Engrave != null)
            foreach (var (job, stage) in Engrave)
                if (p.EngraveStage(job) < stage)
                    return false;
        if (Flags != null)
            foreach (string f in Flags)
                if (!p.HasFlag(f))
                    return false;
        return true;
    }
}
