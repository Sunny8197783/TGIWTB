using Godot;

namespace PixelMmo.Combat;

/// <summary>한 번의 타격이 전달하는 것 전부.</summary>
public struct DamageInfo
{
    public float Amount;

    /// <summary>넉백 방향. 정규화되어 있어야 한다.</summary>
    public Vector2 Direction;

    /// <summary>강타격 — 히트스톱·넉백·화면흔들림이 전부 강한 쪽 수치를 쓴다. (§C-3, §C-4, §C-6)</summary>
    public bool Heavy;

    public string SkillId;
    public Node2D Source;
}

/// <summary>피해를 받을 수 있는 것. 플레이어와 몬스터가 같은 경로를 탄다.</summary>
public interface IDamageable
{
    bool IsAlive { get; }
    Vector2 GlobalPosition { get; }

    /// <summary>숙련 매크로 방어에서 '같은 대상'을 식별하는 키. (§F)</summary>
    ulong InstanceId { get; }

    int Level { get; }

    /// <summary>실제로 피해가 들어갔으면 true. 무적·회피로 흘렸으면 false.</summary>
    bool TakeDamage(in DamageInfo info);

    /// <summary>이 엔티티만 시간을 멈춘다. 화면 전체가 아니다. (§C-3)</summary>
    void ApplyHitstop(float seconds);
}
