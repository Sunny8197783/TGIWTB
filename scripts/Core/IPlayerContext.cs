using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 플레이어 상태에 접근하는 유일한 통로 (CLAUDE.md 규칙 2 — 나중에 서버 권위로 옮기기 위함).
/// 지금은 전투에 필요한 만큼만. 직업·숙련·세이브는 M5 에서 archive/astra-3d 의 것을 옮기며 늘린다.
/// </summary>
public interface IPlayerContext
{
    float Hp { get; }
    float MaxHp { get; }
    bool IsAlive { get; }
    Vector3 WorldPosition { get; }
}
