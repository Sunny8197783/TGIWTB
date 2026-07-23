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
    public static readonly float FacingMarkerSize = 4f;
    public static readonly float FacingMarkerDistance = 8f;

    // --- 세이브 플래그 / 카운터 키 ---
    public static readonly string FlagDiedOnce = "died_once";
    public static readonly string CounterDeaths = "deaths";
}
