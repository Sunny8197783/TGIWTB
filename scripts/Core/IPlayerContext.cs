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
}
