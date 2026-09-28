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
    public const string GroupMotion = "모션";
    public const string GroupVfx = "이펙트";

    public static readonly TuningEntry StaminaMax = Custom("stamina.max", GroupScreen, "최대 스태미나", 100f, 25f, 200f, 5f, "", false);
    public static readonly TuningEntry StaminaDashCost = Custom("stamina.dash", GroupScreen, "회피 스태미나 소모", 24f, 0f, 100f, 1f, "", false);
    public static readonly TuningEntry StaminaRegen = Custom("stamina.regen", GroupScreen, "스태미나 초당 회복", 20f, 1f, 100f, 1f, "/s", false);
    public static readonly TuningEntry StaminaRegenDelay = Sec("stamina.delay", GroupScreen, "스태미나 회복 지연", .5f, 0f, 2f, .05f); // 30 frames

    // --- §C-1 이동 --------------------------------------------------------

    public static readonly TuningEntry MoveSpeed =
        Custom("move.speed", GroupMove, "이동 속도", 90f, 0f, 300f, 1f, "px/s", isTime: false);

    /// <summary>
    /// 달리기 속도 140px/s (Shift 홀드). 걷기 90px/s 의 약 1.56배.
    /// 대시(320px/s, 0.14s 버스트)와는 별개의 지속 이동.
    /// </summary>
    public static readonly TuningEntry RunSpeed =
        Custom("move.runSpeed", GroupMove, "달리기 속도", 140f, 0f, 400f, 5f, "px/s", isTime: false);

    /// <summary>0.12s (7프레임). ref dev_in_airforce: 서 있다가 몸이 기울기까지 6~8프레임.</summary>
    public static readonly TuningEntry AccelTime =
        Sec("move.accel", GroupMove, "가속 시간", 0.12f, 0f, 0.4f, 0.005f);

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

    /// <summary>
    /// 시선 고정(Shift)이 대상을 잡는 거리. 이 안의 가장 가까운 적을 계속 바라본다.
    /// 3배 줌에서 화면에 보이는 범위가 가로 ±107px 이라, 보이지도 않는 적에게
    /// 고정되지 않도록 130px 로 잡았다. (고블린 유지 거리 120px 는 들어온다.)
    /// </summary>
    public static readonly TuningEntry LockOnRange =
        Px("lock.range", GroupMove, "시선 고정 거리", 130f, 40f, 400f, 5f);

    // --- §C-2 공격 프레임 --------------------------------------------------

    // 스프라이트 애니메이션이 들어오면서 프레임 수(5~8)를 눈으로 읽을 시간이 필요해졌다.
    // 명세값(0.08/0.10/0.16)은 도형 시절 기준이라 동작이 순식간에 지나가 버린다.
    // 총 0.32s → 0.60s 로 늘려 예비동작→타격→마무리가 실제로 보이게 한다.

    /// <summary>0.07s (4프레임). ref dev_in_airforce: 거의 즉발 — 칼을 드는 예비동작이 2프레임(30fps).</summary>
    public static readonly TuningEntry AttackWindup =
        Sec("atk.windup", GroupAttack, "선딜", 0.07f, 0.01f, 0.5f, 0.005f);

    /// <summary>0.14s (8프레임). 히트박스 활성 구간.</summary>
    public static readonly TuningEntry AttackActive =
        Sec("atk.active", GroupAttack, "판정", 0.14f, 0.01f, 0.5f, 0.005f);

    /// <summary>0.15s (9프레임). ref dev_in_airforce 실측치. 이동은 RecoveryMoveScale 로 느려질 뿐 멈추지 않는다.</summary>
    public static readonly TuningEntry AttackRecovery =
        Sec("atk.recovery", GroupAttack, "후딜", 0.15f, 0.01f, 0.8f, 0.005f);

    public static readonly TuningEntry WindupMoveScale =
        Ratio("atk.windupMove", GroupAttack, "선딜 이동 배율", 0.75f, 0f, 1f, 0.05f);

    /// <summary>
    /// 후딜에도 완전히 멈추지 않는다 — "무조건 멈춰야 하는 건 답답하다" (§ref dev_in_airforce).
    /// 0 이면 예전처럼 정지, 지금은 느리게라도 흐름이 이어지게 한다.
    /// </summary>
    public static readonly TuningEntry RecoveryMoveScale =
        Ratio("atk.recoveryMove", GroupAttack, "후딜 이동 배율", 0.70f, 0f, 1f, 0.05f);

    /// <summary>후딜의 뒤쪽 40% 구간에서 다음 공격을 선입력하면 콤보로 이어진다.</summary>
    public static readonly TuningEntry CancelWindowRatio =
        Ratio("atk.cancel", GroupAttack, "캔슬 창 비율", 0.40f, 0f, 1f, 0.05f);

    /// <summary>
    /// 강타(skill.Heavy) 전용 선딜. AttackWindup 보다 느리게 들어올려 "무거운 한 방"임을
    /// 휘두르기 전에 미리 읽게 한다. animations.json 의 heavy_windup 이 같은 키를 본다.
    /// </summary>
    public static readonly TuningEntry HeavyWindup =
        Sec("atk.heavyWindup", GroupAttack, "강타 선딜", 0.11f, 0.01f, 0.6f, 0.005f);

    /// <summary>강타 전용 후딜. 큰 스윙일수록 자세를 회복하는 데 더 걸린다.</summary>
    public static readonly TuningEntry HeavyRecovery =
        Sec("atk.heavyRecovery", GroupAttack, "강타 후딜", 0.22f, 0.01f, 0.9f, 0.005f);

    /// <summary>
    /// 기본 공격을 이 시간 안에 다시 넣으면 콤보가 이어진다(3타: 좌→우→마무리).
    /// 캔슬 창보다 넉넉히 잡아 후딜 끝난 직후 입력도 이어지게 한다.
    /// </summary>
    public static readonly TuningEntry ComboWindowSeconds =
        Sec("atk.comboWindow", GroupAttack, "콤보 유지 시간", 0.55f, 0.1f, 1.5f, 0.05f);

    /// <summary>3타 마무리 피해 배율. 강타 취급으로 히트스톱·화면흔들림·넉백도 같이 커진다.</summary>
    public static readonly TuningEntry ComboFinisherDamageMult =
        Ratio("atk.comboFinisherDmg", GroupAttack, "콤보 마무리 배율", 1.3f, 1f, 3f, 0.05f);

    // --- §C-3 히트스톱 (타격감의 80%) --------------------------------------

    /// <summary>
    /// 0.03s (2프레임). ref dev_in_airforce: 평타 적중엔 눈에 띄는 정지가 거의 없다
    /// ("있어도 1~2프레임") — 손맛은 여기서 만들지 않고 스파크·플래시·카메라 킥이 만든다.
    /// 진짜 씹히는 정지는 강타·패링뿐. (HitstopHeavy 참고)
    /// </summary>
    public static readonly TuningEntry HitstopNormal =
        Sec("stop.normal", GroupHitstop, "일반 타격", 0.03f, 0f, 0.4f, 0.005f);

    /// <summary>
    /// 0.14s (8프레임). 명세 0.11s 에서 늘렸다 — 강타·카운터가 일반 타격과
    /// 확실히 다르게 느껴져야 강한 한 방을 노릴 이유가 생긴다.
    /// </summary>
    public static readonly TuningEntry HitstopHeavy =
        Sec("stop.heavy", GroupHitstop, "강타격", 0.14f, 0f, 0.4f, 0.005f);

    /// <summary>0.18s (11프레임). 명세 0.16s. 처치의 '툭' 끊기는 맛을 조금 더.</summary>
    public static readonly TuningEntry HitstopKill =
        Sec("stop.kill", GroupHitstop, "처치", 0.18f, 0f, 0.6f, 0.005f);

    /// <summary>
    /// 처치된 몬스터가 터지기까지 남아 있는 시간. §C 에 없는 구현 값이지만
    /// 매직 넘버를 코드에 두지 않기 위해 여기 모은다.
    /// 처치 히트스톱보다 길어야 '시체를 때리는' 경우가 실제로 생기고,
    /// 그래야 숙련 매크로 방어(deadTarget)가 로그로 확인된다. (§F, §K)
    /// </summary>
    public static readonly TuningEntry DeathLinger =
        Sec("stop.corpse", GroupHitstop, "시체 유지", 0.45f, 0f, 1f, 0.01f);

    // --- §C-4 넉백 ---------------------------------------------------------

    public static readonly TuningEntry KnockbackDistance =
        Px("kb.dist", GroupKnockback, "일반 거리", 20f, 0f, 120f, 1f);

    /// <summary>0.10s, ease-out. 명세 0.12s — 짧게 끝나야 '튕겨 나갔다'로 읽힌다.</summary>
    public static readonly TuningEntry KnockbackTime =
        Sec("kb.time", GroupKnockback, "일반 시간", 0.10f, 0.02f, 0.6f, 0.005f);

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

    public static readonly TuningEntry ShakeStrength =
        Ratio("shake.strength", GroupScreen, "화면 흔들림 강도 (0=끔)", 1f, 0f, 1f, .05f);
    public static readonly TuningEntry ShakeFrequency =
        Custom("shake.frequency", GroupScreen, "타격 진동 주파수", 18f, 6f, 30f, 1f, "Hz", isTime: false);

    /// <summary>2.2px. 명세 1.5px — 3배 줌이라 1.5px 는 거의 안 보였다.</summary>
    public static readonly TuningEntry ShakeNormalAmp =
        Px("shake.normalAmp", GroupScreen, "일반 타격 진폭", 2.2f, 0f, 12f, 0.1f);

    public static readonly TuningEntry ShakeNormalTime =
        Sec("shake.normalTime", GroupScreen, "일반 타격 지속", 0.12f, 0f, 0.6f, 0.005f); // 7 frames

    /// <summary>4.5px. 명세 3.0px — 강타·카운터가 확실히 크게 흔들려야 한다.</summary>
    public static readonly TuningEntry ShakeHeavyAmp =
        Px("shake.heavyAmp", GroupScreen, "강타격 진폭", 4.5f, 0f, 16f, 0.1f);

    public static readonly TuningEntry ShakeHeavyTime =
        Sec("shake.heavyTime", GroupScreen, "강타격 지속", 0.18f, 0f, 0.8f, 0.005f); // 11 frames

    public static readonly TuningEntry ShakeHurtAmp =
        Px("shake.hurtAmp", GroupScreen, "플레이어 피격 진폭", 4.0f, 0f, 20f, 0.1f);

    public static readonly TuningEntry ShakeHurtTime =
        Sec("shake.hurtTime", GroupScreen, "플레이어 피격 지속", 0.2f, 0f, 1f, 0.01f);

    /// <summary>
    /// ref dev_in_airforce: 패링 성공은 강타격보다도 확실히 크게 — 36px 에서 시작해
    /// 스프링처럼 잦아든다(실측 비율로 ShakeHeavyAmp 의 약 1.6배). 0.33s(20프레임) 진동.
    /// </summary>
    public static readonly TuningEntry ShakeParryAmp =
        Px("shake.parryAmp", GroupScreen, "패링 진폭", 7.5f, 0f, 24f, 0.1f);

    public static readonly TuningEntry ShakeParryTime =
        Sec("shake.parryTime", GroupScreen, "패링 지속", 0.33f, 0f, 1f, 0.01f);

    /// <summary>데미지 숫자가 위로 올라가는 거리.</summary>
    public static readonly TuningEntry PopupRise =
        Px("fx.popupRise", GroupScreen, "데미지 팝업 상승", 24f, 0f, 96f, 1f);

    public static readonly TuningEntry PopupLife =
        Sec("fx.popupLife", GroupScreen, "데미지 팝업 지속", 0.6f, 0.1f, 2f, 0.05f);

    /// <summary>0.07s (4프레임). 피격 시 대상이 흰색으로 물드는 시간 — 가장 강한 '맞았다' 신호.</summary>
    public static readonly TuningEntry HitFlashTime =
        Sec("fx.flash", GroupScreen, "흰색 플래시", 0.07f, 0f, 0.4f, 0.005f);

    public static readonly TuningEntry DeathParticles =
        Custom("fx.particles", GroupScreen, "처치 파티클 수", 8f, 0f, 32f, 1f, "개", isTime: false);

    // --- 공격 모션 ---------------------------------------------------------
    //
    // 스프라이트가 없으므로(규칙 5) 도형을 앞뒤로 밀어 예비 동작 → 내지르기 → 복귀를
    // 만든다. 무엇이 언제 때리는지 눈으로 읽히게 하는 것이 목적이라 §C 에는 없지만
    // 매직 넘버를 코드에 두지 않기 위해 여기 모은다.

    /// <summary>선딜에 뒤로 빼는 거리. 예비 동작이 있어야 공격이 예고된다.</summary>
    public static readonly TuningEntry MotionWindupBack =
        Px("motion.windupBack", GroupMotion, "선딜 뒤로 빼기", 6f, 0f, 16f, 0.5f);

    /// <summary>판정에 앞으로 내미는 거리. 몸이 실리는 만큼 때린 느낌이 난다.</summary>
    public static readonly TuningEntry MotionActiveForward =
        Px("motion.activeForward", GroupMotion, "판정 앞으로 내밀기", 11f, 0f, 32f, 0.5f);

    /// <summary>몬스터 모션 배율. 몸집이 큰 놈일수록 크게 움직여야 읽힌다.</summary>
    public static readonly TuningEntry MotionMonsterScale =
        Ratio("motion.monster", GroupMotion, "몬스터 모션 배율", 1.6f, 0f, 4f, 0.1f);

    // --- 스킬 이펙트 (VFX) -------------------------------------------------
    // 도형 기반 연출. §C 에 없지만 매직 넘버를 코드에 두지 않으려 여기 모은다.

    /// <summary>0.22s (13프레임). ref dev_in_airforce 실측 0.2~0.25s.</summary>
    public static readonly TuningEntry VfxSlashLife =
        Sec("vfx.slashLife", GroupVfx, "베기 궤적 지속", 0.22f, 0f, 0.6f, 0.005f);

    public static readonly TuningEntry VfxSparkLife =
        Sec("vfx.sparkLife", GroupVfx, "타격 스파크 지속", 0.16f, 0f, 0.5f, 0.005f);

    public static readonly TuningEntry VfxSparkRadius =
        Px("vfx.sparkRadius", GroupVfx, "일반 타격 불꽃 반경", 11f, 0f, 40f, 1f);
    public static readonly TuningEntry VfxHeavySparkRadius =
        Px("vfx.heavySparkRadius", GroupVfx, "강타격 불꽃 반경", 19f, 0f, 60f, 1f);

    public static readonly TuningEntry VfxShockLife =
        Sec("vfx.shockLife", GroupVfx, "충격파 지속", 0.28f, 0f, 0.8f, 0.005f);

    public static readonly TuningEntry VfxBashShock =
        Px("vfx.bashShock", GroupVfx, "강타 충격파 반경", 46f, 0f, 160f, 1f);

    public static readonly TuningEntry VfxWarcryShock =
        Px("vfx.warcryShock", GroupVfx, "함성 충격파 반경", 66f, 0f, 220f, 1f);

    public static readonly TuningEntry VfxDashWindLife =
        Sec("vfx.dashWindLife", GroupVfx, "대시 바람 지속", 0.24f, 0f, 0.8f, 0.005f);

    public static readonly TuningEntry VfxConeLife =
        Sec("vfx.coneLife", GroupVfx, "강타 원뿔 지속", 0.22f, 0f, 0.8f, 0.005f);

    public static readonly TuningEntry VfxConeLength =
        Px("vfx.coneLength", GroupVfx, "강타 원뿔 길이", 46f, 0f, 160f, 1f);

    public static readonly TuningEntry VfxConeHalfWidth =
        Px("vfx.coneHalfWidth", GroupVfx, "강타 원뿔 폭", 13f, 0f, 60f, 0.5f);

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
