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
    /// <summary>플레이어는 파란 사각형. (§A 아트 방침)</summary>
    public static readonly float BodySize = 12f;
    public static readonly Color BodyColor = new(0.31f, 0.55f, 0.95f);
    public static readonly Color FacingColor = new(0.75f, 0.87f, 1.0f);

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
    public static readonly float FacingMarkerSize = 4f;
    public static readonly float FacingMarkerDistance = 8f;

    // --- 세이브 플래그 / 카운터 키 ---
    public static readonly string FlagDiedOnce = "died_once";
    public static readonly string CounterDeaths = "deaths";
}
