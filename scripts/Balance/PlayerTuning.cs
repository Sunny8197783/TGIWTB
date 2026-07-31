using System.Collections.Generic;
using Godot;

namespace PixelMmo.Balance;

/// <summary>플레이어 초기 상태. (§D-1) 전투 손맛 수치는 CombatTuning 쪽에 있다.</summary>
public static class PlayerTuning
{
    public static readonly float BaseMaxHp = 100f;
    public static readonly int StartLevel = 1;

    /// <summary>P1 에서는 이 직업 R1 고정. (§B)</summary>
    public static readonly string StartingJobId = "job_warrior";

    public static readonly IReadOnlyDictionary<string, int> BaseStats = new Dictionary<string, int>
    {
        [Stats.Str] = 10,
        [Stats.Dex] = 10,
        [Stats.Vit] = 10,
        [Stats.Int] = 5,
        [Stats.Wis] = 5,
        [Stats.Luk] = 5,
    };

    /// <summary>사망 후 마을에서 되살아나기까지의 시간(초).</summary>
    public static readonly float RespawnDelay = 1.2f;

    // --- 도형 (스프라이트 금지 — CLAUDE.md 규칙 5) ---
    /// <summary>충돌·전투 판정용 몸 크기. 시각 피규어와 별개로 게임 수치다.</summary>
    public static readonly float BodySize = 12f;

    /// <summary>구버전 참조 호환용. 피규어의 기본 상의 색으로도 쓴다.</summary>
    public static readonly Color BodyColor = new(0.31f, 0.55f, 0.95f);

    // --- 업라이트 피규어 (P2) ---------------------------------------------
    // 위에서 90도로 내려다보지 않고 비스듬히(≈60~70도) 보는 느낌은,
    // 캐릭터를 '세워서 앞모습으로' 그려서 낸다. 2D 엔진엔 기울일 카메라가 없다.
    // 바닥 그림자가 위치감과 입체감의 대부분을 만든다.

    /// <summary>피규어 전체 배율. 키우면 파츠가 더 선명해진다(꾸미기 가독성).</summary>
    public static readonly float FigureScale = 1.6f;

    public static readonly Color ShadowColor = new(0f, 0f, 0f, 0.28f);

    /// <summary>파츠 외곽선 — 실루엣을 또렷하게(픽셀아트 룩). Grow 폭은 월드px.</summary>
    public static readonly Color OutlineColor = new(0.07f, 0.07f, 0.10f);
    public static readonly float OutlineWidth = 0.7f;

    /// <summary>허리 벨트색(갑옷 디테일).</summary>
    public static readonly Color BeltColor = new(0.24f, 0.16f, 0.10f);

    // 살아있는 느낌 — 스프라이트 애니메이션 전까지 도형에 넣는 '숨쉬기/걸음' 들썩임.
    // 스프라이트가 들어와도 이 오프셋은 그대로 위에 얹어 쓸 수 있다.
    public static readonly float IdleBobAmp = 1.1f;   // 대기 시 위아래 폭(px)
    public static readonly float IdleBobHz = 1.7f;    // 느리고 부드럽게
    public static readonly float WalkBobAmp = 1.4f;   // 걸을 때 폭
    public static readonly float WalkBobHz = 7.5f;    // 걸음 리듬

    // 커스터마이즈 프리셋 — 색 슬롯이 고를 팔레트. 첫 항목이 기본값.
    public static readonly Color[] SkinTones =
    {
        new(0.95f, 0.80f, 0.66f), new(0.86f, 0.66f, 0.50f),
        new(0.68f, 0.49f, 0.35f), new(0.52f, 0.36f, 0.26f),
        new(0.98f, 0.87f, 0.78f),
    };

    public static readonly Color[] HairColors =
    {
        new(0.30f, 0.20f, 0.13f), new(0.08f, 0.08f, 0.10f),
        new(0.85f, 0.70f, 0.35f), new(0.75f, 0.20f, 0.20f),
        new(0.55f, 0.55f, 0.62f), new(0.40f, 0.55f, 0.85f),
    };

    public static readonly Color[] ShirtColors =
    {
        new(0.31f, 0.55f, 0.95f), new(0.85f, 0.30f, 0.30f),
        new(0.35f, 0.72f, 0.42f), new(0.80f, 0.55f, 0.20f),
        new(0.55f, 0.40f, 0.75f), new(0.90f, 0.90f, 0.92f),
    };

    public static readonly Color[] PantsColors =
    {
        new(0.28f, 0.30f, 0.38f), new(0.20f, 0.22f, 0.26f),
        new(0.45f, 0.32f, 0.20f), new(0.30f, 0.40f, 0.35f),
        new(0.60f, 0.60f, 0.65f),
    };

    /// <summary>가드 중 — 회청색.</summary>
    public static readonly Color GuardColor = new(0.45f, 0.62f, 0.72f);

    /// <summary>퍼펙트 가드 직후 반격 상태 — 밝은 청록.</summary>
    public static readonly Color RiposteColor = new(0.45f, 0.95f, 0.90f);

    /// <summary>함성 버프 중 — 붉은 기 도는 파랑.</summary>
    public static readonly Color BuffColor = new(0.62f, 0.48f, 0.95f);

    // --- 무기 그리기 (도형만, 규칙 5) --------------------------------------

    /// <summary>검 길이(px). 몸 중심에서 칼끝까지.</summary>
    public static readonly float SwordLength = 16f;

    public static readonly float SwordWidth = 2.5f;
    public static readonly Color SwordColor = new(0.85f, 0.90f, 0.95f);

    /// <summary>강타 궤적 색 — 판정 순간 밝게 빛난다.</summary>
    public static readonly Color SwordHotColor = new(1f, 0.85f, 0.45f);

    /// <summary>방패 크기(px).</summary>
    public static readonly float ShieldWidth = 3f;
    public static readonly float ShieldHeight = 13f;

    /// <summary>몸 중심에서 방패까지의 거리.</summary>
    public static readonly float ShieldDistance = 9f;

    public static readonly Color ShieldColor = new(0.72f, 0.78f, 0.85f);

    /// <summary>퍼펙트 가드 직후 방패가 번쩍이는 색.</summary>
    public static readonly Color ShieldPerfectColor = new(0.6f, 1f, 0.95f);

    /// <summary>가드 쿨다운 중 방패가 흐려지는 색 — 지금 못 막는다는 신호.</summary>
    public static readonly Color ShieldCooldownColor = new(0.45f, 0.45f, 0.50f);

    // --- 세이브 플래그 / 카운터 키 ---
    public static readonly string FlagDiedOnce = "died_once";
    public static readonly string CounterDeaths = "deaths";
}
