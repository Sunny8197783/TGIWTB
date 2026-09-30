using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 스킬 정의 — data/skills/*.json (CLAUDE.md 규칙 1). 스킬은 "몇 초에 무엇을 하나" 이벤트 목록이다.
/// 해석기는 Combat/SkillRunner. 새 스킬은 코드 없이 JSON 만 더한다.
/// 이펙트 원칙: 예비 동작(anticipation) → 충돌(impact) → 흩어짐(dissipation), 흰 심 + 3~4단 색 램프.
/// </summary>
public sealed class SkillDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>재사용 대기 (초)</summary>
    public float Cooldown { get; set; }
    /// <summary>스킬 전체 길이 (초). 이 동안 다른 행동을 못 한다 (회피로는 끊을 수 있다: CancelAt 이후).</summary>
    public float Duration { get; set; }
    public float CancelAt { get; set; }
    /// <summary>주인공 동작 시트 이름과, 칼이 닿는 칸·시각 (PlayKeyed)</summary>
    public string Anim { get; set; }
    public int AnimHitFrame { get; set; }
    public float AnimHitAt { get; set; }
    /// <summary>색 램프 이름 — SkillRunner.Palettes</summary>
    public string Palette { get; set; }
    public List<SkillEvent> Events { get; set; } = new();
    public MasteryDef Mastery { get; set; }
    /// <summary>히든 스킬: 칸에 넣지 않는 패시브. 조건(Learn)은 화면 어디에도 보이지 않는다 (규칙 4).</summary>
    public bool Hidden { get; set; }
    public LearnDef Learn { get; set; }
    public PassiveDef Passive { get; set; }

    public sealed class MasteryDef
    {
        /// <summary>이보다 빠른 연속 시전은 세지 않는다 (초). 0 이면 CombatTuning 기본값.</summary>
        public float MinInterval { get; set; }
        /// <summary>숙련 100 당 피해 증가율 (0.3 = +30%)</summary>
        public float DamageBonusPer100 { get; set; }
        public EvolutionDef Evolution { get; set; }
    }

    public sealed class EvolutionDef
    {
        public float At { get; set; }
        public string Into { get; set; }
        /// <summary>진화 순간 화면에 뜨는 한 줄. 조건은 말하지 않는다.</summary>
        public string Announce { get; set; }
    }

    public sealed class LearnDef
    {
        /// <summary>세는 사건: perfect_dodge | parry</summary>
        public string Counter { get; set; }
        public float HpRatioAtMost { get; set; } = 1f;
        public int Required { get; set; } = 1;
        public string Announce { get; set; }
    }

    /// <summary>익히면 바뀌는 판정 창 (초). 0 이면 그대로.</summary>
    public sealed class PassiveDef
    {
        public float ParryWindow { get; set; }
        public float PerfectDodgeWindow { get; set; }
    }

    public sealed class SkillEvent
    {
        /// <summary>시작 후 몇 초에</summary>
        public float T { get; set; }
        /// <summary>sfx / slash / ring / sparkle / petals / lightning / hit / dash / flash / shake / slow / spin</summary>
        public string Fx { get; set; }
        // 쓰는 것만 채운다 — 나머지는 기본값
        public string Sound { get; set; }
        public float Pitch { get; set; } = 1f;
        public float Db { get; set; }
        public string Shape { get; set; }         // slash: "horizontal" | "reverse" | "vertical" | "spin",  hit: "arc" | "circle"
        public float Radius { get; set; } = 2f;
        public float Arc { get; set; } = 180f;
        public float Life { get; set; } = 0.3f;
        public float Thickness { get; set; } = 1f;
        public float Forward { get; set; }        // 주인공 앞쪽으로 얼마나 떨어진 자리에서 (m)
        public float Side { get; set; }           // 오른쪽(+)·왼쪽(-)으로 (m)
        public int Count { get; set; } = 12;
        public float Damage { get; set; }
        public string Impact { get; set; } = "normal"; // normal | heavy
        public float Distance { get; set; }       // dash
        public float Time { get; set; } = 0.2f;   // dash 시간, slow 시간
        public float Amount { get; set; } = 0.5f; // flash 세기, shake 충격량, slow 배율
        public string Anim { get; set; }          // spin: 멈춰 세울 동작
        public int Frame { get; set; }            // spin: 그 동작의 칸 (칼을 뻗은 칸)
    }

    private static Dictionary<string, SkillDef> _all;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyDictionary<string, SkillDef> All
    {
        get
        {
            if (_all != null)
                return _all;
            _all = new Dictionary<string, SkillDef>();
            foreach (string file in DirAccess.GetFilesAt("res://data/skills"))
            {
                if (!file.EndsWith(".json"))
                    continue;
                var def = JsonSerializer.Deserialize<SkillDef>(FileAccess.GetFileAsString($"res://data/skills/{file}"), Options);
                _all[def.Id] = def;
            }
            return _all;
        }
    }
}
