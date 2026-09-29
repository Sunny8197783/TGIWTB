using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>몬스터 정의 — data/monsters/*.json (CLAUDE.md 규칙 1). 시간은 초.</summary>
public sealed class MonsterDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>스프라이트 시트 폴더 (res://art/characters/...)</summary>
    public string Art { get; set; }
    public float Hp { get; set; }
    /// <summary>이만큼 피해가 쌓여야 움찔한다 (공격 중에도). 작을수록 잘 끊긴다.</summary>
    public float Poise { get; set; }
    public float Speed { get; set; }
    public float Sight { get; set; }
    public float Radius { get; set; }
    /// <summary>맞을 때 밀리는 정도 배율 (가벼울수록 큼).</summary>
    public float Knockback { get; set; } = 1f;
    public AttackDef Attack { get; set; }
    public SoundDef Sounds { get; set; }

    public sealed class AttackDef
    {
        public float Damage { get; set; }
        public float Reach { get; set; }
        /// <summary>예고 동작 — 플레이어가 읽고 막기·회피를 고를 시간.</summary>
        public float Windup { get; set; }
        public float Active { get; set; }
        public float Recover { get; set; }
        /// <summary>공격하며 돌진하는 속도 (m/s).</summary>
        public float Lunge { get; set; }
        public float Cooldown { get; set; }
        /// <summary>강한 공격: 예고 동안 바닥에 빨간 원이 차오른다.</summary>
        public bool Telegraph { get; set; }
    }

    public sealed class SoundDef
    {
        public string Hit { get; set; }
        public string Windup { get; set; }
        public string Death { get; set; }
    }

    private static readonly Dictionary<string, MonsterDef> Cache = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static MonsterDef Get(string id)
    {
        if (!Cache.TryGetValue(id, out var def))
        {
            string json = FileAccess.GetFileAsString($"res://data/monsters/{id}.json");
            Cache[id] = def = JsonSerializer.Deserialize<MonsterDef>(json, Options);
        }
        return def;
    }
}
