using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 플레이어 상태에 접근하는 유일한 통로 (CLAUDE.md 규칙 2 — 나중에 서버 권위로 옮기기 위함).
/// 지금은 전투·스킬 칸에 필요한 만큼만. 직업은 나중에 archive/astra-3d 의 것을 옮기며 늘린다.
/// </summary>
public interface IPlayerContext
{
    float Hp { get; }
    float MaxHp { get; }
    bool IsAlive { get; }
    Vector3 WorldPosition { get; }
    /// <summary>스킬 칸에 든 스킬 id (빈 칸은 null)</summary>
    System.Collections.Generic.IReadOnlyList<string> Skills { get; }
    float CooldownRemaining(string skillId);
    /// <summary>진화까지 숙련 진행도 0~1. 진화가 없으면 -1.</summary>
    float MasteryProgress(string skillId);
    /// <summary>모습: 바탕 원화 id (data/player/appearance.json) 와 옷 색 번호</summary>
    string LookBase { get; }
    int LookAccent { get; }
    void SetLook(string baseId, int accent);
    /// <summary>빚은 모습 id (없으면 null) — Core/Forge 가 만든 user://forge/&lt;id&gt;</summary>
    string LookForge { get; }
    void SetForge(string forgeId);
    /// <summary>지금 상태를 저장한다 (모습 확정 등)</summary>
    void Save();
    /// <summary>깨운 여신상 id</summary>
    System.Collections.Generic.IReadOnlySet<string> Statues { get; }
    /// <summary>마지막으로 기도한 여신상 (없으면 null)</summary>
    string RespawnStatue { get; }
    /// <summary>여신상에 기도: 깨우고(처음이면 true), 부활 지점으로 정하고, 체력을 채운다</summary>
    bool Pray(string statueId, Vector3 respawnFeet);
    /// <summary>빠른 이동 (발 위치)</summary>
    void TravelTo(Vector3 feet);
    int Level { get; }
    /// <summary>이번 레벨에서 모은 경험치와 다음 레벨까지 필요한 양 (최고 레벨이면 0)</summary>
    int Exp { get; }
    int ExpToNext { get; }
    string JobId { get; }
    string JobName { get; }
    void GrantExp(int amount);
    /// <summary>전직할 수 있나 (레벨·지금 직업). 교관 대화가 묻는다</summary>
    bool CanBecome(Data.JobDef job);
    void ChangeJob(string jobId);

    // ── 무명록 (M9) ──
    /// <summary>나래가 지어 준 이름 (없으면 null)</summary>
    string HeroName { get; }
    void SetHeroName(string name);
    /// <summary>행적: 사건을 센 횟수 (Core/Deeds)</summary>
    int Deed(string key);
    void CountDeed(string key, int by = 1);
    /// <summary>이 이름(직업)을 거쳤거나 지금 쓰고 있나</summary>
    bool HeldJob(string jobId);
    /// <summary>각인 단계 (0~최대)와 다음 단계까지 0~1</summary>
    int EngraveStage(string jobId);
    float EngraveProgress(string jobId);
    /// <summary>이야기·세계 상태 값 (없으면 0). 표시(flag)는 0 이 아닌 값</summary>
    int Var(string key);
    void SetVar(string key, int value);
    bool HasFlag(string flag);
    int Shards { get; }
    void AddShards(int n);
    /// <summary>이슬 병: 남은 횟수와 최대 (여신상에서 채운다)</summary>
    int Flask { get; }
    int FlaskMax { get; }
    /// <summary>바라보는 쪽 (단위 벡터)</summary>
    Vector3 Forward { get; }
}
