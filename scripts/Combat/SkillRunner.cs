using System.Collections.Generic;
using Godot;
using PixelMmo.Data;

namespace PixelMmo.Combat;

public enum SkillPhase
{
    None,
    Windup,
    Active,
    Recovery,
}

/// <summary>
/// §C-2 공격 프레임 진행. 선딜 → 판정 → 후딜.
/// 실제 히트 판정은 소유자가 IsActive 인 프레임에 직접 돌린다.
/// </summary>
public sealed class SkillRunner
{
    private readonly HashSet<ulong> _hitThisSwing = new();

    public SkillDefinition Skill { get; private set; }
    public SkillPhase Phase { get; private set; } = SkillPhase.None;

    /// <summary>판정이 시작된 순간 고정된 방향. (§C-7 "판정 시작 후 고정")</summary>
    public Vector2 LockedFacing { get; private set; } = Vector2.Right;

    private float _timer;
    private float _phaseLength;

    public bool IsBusy => Phase != SkillPhase.None;
    public bool IsActive => Phase == SkillPhase.Active;

    /// <summary>현재 단계의 진행도 0~1. 공격 모션 보간에 쓴다.</summary>
    public float PhaseProgress => _phaseLength > 0f
        ? Mathf.Clamp(1f - (_timer / _phaseLength), 0f, 1f)
        : 1f;

    /// <summary>선딜 중에만 방향을 바꿀 수 있다. (§C-7)</summary>
    public bool CanTurn => Phase != SkillPhase.Active && Phase != SkillPhase.Recovery;

    /// <summary>후딜은 이동 불가. (§C-2)</summary>
    public bool CanMove => Phase != SkillPhase.Recovery;

    /// <summary>선딜 동안 이동 속도 40%. (§C-2)</summary>
    public float MoveScale => Phase == SkillPhase.Windup ? CombatTuning.WindupMoveScale : 1f;

    /// <summary>후딜의 뒤쪽 40% — 여기서 선입력이 있으면 다음 공격으로 이어진다. (§C-2 캔슬 창)</summary>
    public bool InCancelWindow
    {
        get
        {
            if (Phase != SkillPhase.Recovery || _phaseLength <= 0f)
                return false;
            float elapsed = _phaseLength - _timer;
            return elapsed >= _phaseLength * (1f - CombatTuning.CancelWindowRatio);
        }
    }

    public void Begin(SkillDefinition skill, Vector2 facing)
    {
        Skill = skill;
        LockedFacing = facing;
        _hitThisSwing.Clear();
        EnterPhase(SkillPhase.Windup, CombatTuning.AttackWindup);
    }

    public void Cancel()
    {
        Phase = SkillPhase.None;
        Skill = null;
        _timer = 0f;
        _phaseLength = 0f;
        JustEnteredActive = false;
        _hitThisSwing.Clear();
    }

    /// <summary>선딜 중 방향 갱신.</summary>
    public void Aim(Vector2 facing)
    {
        if (CanTurn && facing != Vector2.Zero)
            LockedFacing = facing;
    }

    /// <summary>한 스윙에 같은 대상은 한 번만 맞는다.</summary>
    public bool TryMarkHit(ulong instanceId) => _hitThisSwing.Add(instanceId);

    /// <summary>
    /// 이번 Tick 에서 판정 프레임에 막 진입했는가.
    /// 돌진처럼 '판정이 시작되는 순간' 한 번만 해야 하는 일이 있다.
    /// </summary>
    public bool JustEnteredActive { get; private set; }

    public void Tick(float delta)
    {
        JustEnteredActive = false;

        if (Phase == SkillPhase.None)
            return;

        _timer -= delta;
        if (_timer > 0f)
            return;

        switch (Phase)
        {
            case SkillPhase.Windup:
                EnterPhase(SkillPhase.Active, CombatTuning.AttackActive);
                JustEnteredActive = true;
                break;
            case SkillPhase.Active:
                EnterPhase(SkillPhase.Recovery, CombatTuning.AttackRecovery);
                break;
            default:
                Cancel();
                break;
        }
    }

    private void EnterPhase(SkillPhase phase, float length)
    {
        Phase = phase;
        _phaseLength = length;
        _timer = length;
    }
}
