using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// 플레이어 상태에 접근하는 유일한 통로. 전투/스킬/세이브 어느 쪽도
/// PlayerCharacter 를 직접 참조하지 않는다. (CLAUDE.md 규칙 2 — P5 서버 권위 전환 대비)
/// </summary>
public interface IPlayerContext
{
    int Level { get; }
    float Hp { get; }
    float MaxHp { get; }
    float HpRatio { get; }
    bool IsAlive { get; }
    Vector2 WorldPosition { get; }

    JobState Jobs { get; }
    MasteryTracker Mastery { get; }

    /// <summary>장비·직업 보정을 뺀 순수 기본 스탯.</summary>
    int GetBaseStat(string stat);

    /// <summary>기본 스탯 + JobState.StatTotal(stat). 전투 계산은 항상 이쪽을 쓴다. (§D-1)</summary>
    int GetStat(string stat);

    bool HasFlag(string flag);
    void SetFlag(string flag);

    int GetCounter(string key);

    /// <summary>카운터를 delta 만큼 올리고 갱신된 값을 돌려준다.</summary>
    int AddCounter(string key, int delta = 1);

    /// <summary>화면 중앙에 한 줄만 띄운다. 조건 설명은 하지 않는다. (규칙 4)</summary>
    void Announce(string message);

    /// <summary>보유 스킬 교체 (숙련 진화). old → newId.</summary>
    void ReplaceSkill(string oldSkillId, string newSkillId);

    /// <summary>스킬 습득 (히든 트리거 포함).</summary>
    void LearnSkill(string skillId);

    SaveData CaptureSave();
    void RestoreSave(SaveData data);
}
