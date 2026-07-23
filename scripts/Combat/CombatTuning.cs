using System.Collections.Generic;

namespace PixelMmo.Combat;

/// <summary>
/// §C 전투 손맛 수치의 유일한 출처. 여기 없는 전투 상수는 코드에 있으면 안 된다.
/// (CLAUDE.md: 매직 넘버 금지)
///
/// 필드는 static readonly TuningEntry — 항목 자체는 고정이고, 그 안의 Value 만
/// F3 슬라이더가 실시간으로 바꾼다. 명세값은 Entry.Default 에 그대로 박혀 있어
/// 언제든 Reset 으로 되돌아온다.
///
/// 시간 값은 전부 초 단위 float, 괄호 안은 60fps 기준 프레임 수.
/// </summary>
public static class CombatTuning
{
    private static readonly List<TuningEntry> Entries = new();

    public const string GroupMove = "이동";
    public const string GroupAttack = "공격";
    public const string GroupHitstop = "히트스톱";
    public const string GroupKnockback = "넉백";
    public const string GroupInvuln = "무적";
    public const string GroupScreen = "화면";
    public const string GroupInput = "입력";

    // --- §C-1 이동 --------------------------------------------------------

    public static readonly TuningEntry MoveSpeed =
        Custom("move.speed", GroupMove, "이동 속도", 90f, 0f, 300f, 1f, "px/s", isTime: false);

    /// <summary>0.06s (4프레임)</summary>
    public static readonly TuningEntry AccelTime =
        Sec("move.accel", GroupMove, "가속 시간", 0.06f, 0f, 0.4f, 0.005f);

    /// <summary>0.04s (2~3프레임)</summary>
    public static readonly TuningEntry DecelTime =
        Sec("move.decel", GroupMove, "감속 시간", 0.04f, 0f, 0.4f, 0.005f);

    public static readonly TuningEntry DashSpeed =
        Custom("dash.speed", GroupMove, "대시 속도", 320f, 60f, 800f, 5f, "px/s", isTime: false);

    /// <summary>0.14s (8프레임)</summary>
    public static readonly TuningEntry DashDuration =
        Sec("dash.duration", GroupMove, "대시 지속", 0.14f, 0.02f, 0.6f, 0.005f);

    public static readonly TuningEntry DashCooldown =
        Sec("dash.cooldown", GroupMove, "대시 쿨다운", 0.6f, 0f, 3f, 0.02f);

    /// <summary>0.10s (6프레임). sk_hidden_deathline 습득 시 0.20s 로 덮어쓴다. (§E)</summary>
    public static readonly TuningEntry DashInvuln =
        Sec("dash.invuln", GroupMove, "대시 무적", 0.10f, 0f, 0.5f, 0.005f);

    // --- §C-2 공격 프레임 --------------------------------------------------

    /// <summary>0.08s (5프레임). 이 동안 이동 속도가 WindupMoveScale 로 줄어든다.</summary>
    public static readonly TuningEntry AttackWindup =
        Sec("atk.windup", GroupAttack, "선딜", 0.08f, 0.01f, 0.5f, 0.005f);

    /// <summary>0.10s (6프레임). 히트박스 활성 구간.</summary>
    public static readonly TuningEntry AttackActive =
        Sec("atk.active", GroupAttack, "판정", 0.10f, 0.01f, 0.5f, 0.005f);

    /// <summary>0.16s (10프레임). 이동 불가.</summary>
    public static readonly TuningEntry AttackRecovery =
        Sec("atk.recovery", GroupAttack, "후딜", 0.16f, 0.01f, 0.8f, 0.005f);

    public static readonly TuningEntry WindupMoveScale =
        Ratio("atk.windupMove", GroupAttack, "선딜 이동 배율", 0.40f, 0f, 1f, 0.05f);

    /// <summary>후딜의 뒤쪽 40% 구간에서 다음 공격을 선입력하면 콤보로 이어진다.</summary>
    public static readonly TuningEntry CancelWindowRatio =
        Ratio("atk.cancel", GroupAttack, "캔슬 창 비율", 0.40f, 0f, 1f, 0.05f);

    // --- §C-3 히트스톱 (타격감의 80%) --------------------------------------

    /// <summary>0.06s (4프레임)</summary>
    public static readonly TuningEntry HitstopNormal =
        Sec("stop.normal", GroupHitstop, "일반 타격", 0.06f, 0f, 0.4f, 0.005f);

    /// <summary>0.11s (7프레임)</summary>
    public static readonly TuningEntry HitstopHeavy =
        Sec("stop.heavy", GroupHitstop, "강타격", 0.11f, 0f, 0.4f, 0.005f);

    /// <summary>0.16s (10프레임)</summary>
    public static readonly TuningEntry HitstopKill =
        Sec("stop.kill", GroupHitstop, "처치", 0.16f, 0f, 0.6f, 0.005f);

    // --- §C-4 넉백 ---------------------------------------------------------

    public static readonly TuningEntry KnockbackDistance =
        Px("kb.dist", GroupKnockback, "일반 거리", 20f, 0f, 120f, 1f);

    /// <summary>0.12s, ease-out</summary>
    public static readonly TuningEntry KnockbackTime =
        Sec("kb.time", GroupKnockback, "일반 시간", 0.12f, 0.02f, 0.6f, 0.005f);

    public static readonly TuningEntry KnockbackHeavyDistance =
        Px("kb.heavyDist", GroupKnockback, "강타격 거리", 40f, 0f, 200f, 1f);

    /// <summary>0.18s, ease-out</summary>
    public static readonly TuningEntry KnockbackHeavyTime =
        Sec("kb.heavyTime", GroupKnockback, "강타격 시간", 0.18f, 0.02f, 0.8f, 0.005f);

    // --- §C-5 무적 프레임 --------------------------------------------------

    /// <summary>0.5s (30프레임)</summary>
    public static readonly TuningEntry PlayerIFrames =
        Sec("inv.player", GroupInvuln, "피격 무적", 0.5f, 0f, 2f, 0.02f);

    /// <summary>10Hz — 0.05s 간격으로 투명도 토글.</summary>
    public static readonly TuningEntry IFrameBlinkHz =
        Custom("inv.blinkHz", GroupInvuln, "점멸 주파수", 10f, 1f, 30f, 1f, "Hz", isTime: false);

    // --- §C-6 화면 연출 ----------------------------------------------------

    public static readonly TuningEntry ShakeNormalAmp =
        Px("shake.normalAmp", GroupScreen, "일반 타격 진폭", 1.5f, 0f, 12f, 0.1f);

    public static readonly TuningEntry ShakeNormalTime =
        Sec("shake.normalTime", GroupScreen, "일반 타격 지속", 0.08f, 0f, 0.6f, 0.005f);

    public static readonly TuningEntry ShakeHeavyAmp =
        Px("shake.heavyAmp", GroupScreen, "강타격 진폭", 3.0f, 0f, 16f, 0.1f);

    public static readonly TuningEntry ShakeHeavyTime =
        Sec("shake.heavyTime", GroupScreen, "강타격 지속", 0.15f, 0f, 0.8f, 0.005f);

    public static readonly TuningEntry ShakeHurtAmp =
        Px("shake.hurtAmp", GroupScreen, "플레이어 피격 진폭", 4.0f, 0f, 20f, 0.1f);

    public static readonly TuningEntry ShakeHurtTime =
        Sec("shake.hurtTime", GroupScreen, "플레이어 피격 지속", 0.2f, 0f, 1f, 0.01f);

    /// <summary>데미지 숫자가 위로 올라가는 거리.</summary>
    public static readonly TuningEntry PopupRise =
        Px("fx.popupRise", GroupScreen, "데미지 팝업 상승", 24f, 0f, 96f, 1f);

    public static readonly TuningEntry PopupLife =
        Sec("fx.popupLife", GroupScreen, "데미지 팝업 지속", 0.6f, 0.1f, 2f, 0.05f);

    /// <summary>피격 시 대상이 흰색으로 물드는 시간.</summary>
    public static readonly TuningEntry HitFlashTime =
        Sec("fx.flash", GroupScreen, "흰색 플래시", 0.08f, 0f, 0.4f, 0.005f);

    public static readonly TuningEntry DeathParticles =
        Custom("fx.particles", GroupScreen, "처치 파티클 수", 8f, 0f, 32f, 1f, "개", isTime: false);

    // --- §C-7 입력 ---------------------------------------------------------

    /// <summary>0.15s (9프레임) — 후딜 중에 누른 공격을 기억한다.</summary>
    public static readonly TuningEntry InputBuffer =
        Sec("input.buffer", GroupInput, "입력 버퍼", 0.15f, 0f, 0.5f, 0.005f);

    // --- 조회 -------------------------------------------------------------

    public static IReadOnlyList<TuningEntry> All => Entries;

    public static void ResetAll()
    {
        foreach (var entry in Entries)
            entry.Reset();
    }

    public static int ModifiedCount()
    {
        int count = 0;
        foreach (var entry in Entries)
        {
            if (entry.IsModified)
                count++;
        }
        return count;
    }

    // --- 등록 헬퍼 ---------------------------------------------------------

    private static TuningEntry Sec(string key, string group, string label,
        float value, float min, float max, float step)
        => Register(new TuningEntry(key, group, label, value, min, max, step, "s", isTime: true));

    private static TuningEntry Px(string key, string group, string label,
        float value, float min, float max, float step)
        => Register(new TuningEntry(key, group, label, value, min, max, step, "px", isTime: false));

    private static TuningEntry Ratio(string key, string group, string label,
        float value, float min, float max, float step)
        => Register(new TuningEntry(key, group, label, value, min, max, step, "x", isTime: false));

    private static TuningEntry Custom(string key, string group, string label,
        float value, float min, float max, float step, string unit, bool isTime)
        => Register(new TuningEntry(key, group, label, value, min, max, step, unit, isTime));

    private static TuningEntry Register(TuningEntry entry)
    {
        Entries.Add(entry);
        return entry;
    }
}
