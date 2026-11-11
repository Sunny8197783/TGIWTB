using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 몬스터 정의 — data/monsters/*.json (CLAUDE.md 규칙 1). 시간은 초.
/// 움직임(Move)과 공격 목록(Attacks)의 조합이 종마다 다른 싸움을 만든다:
///   chase 붙어서 친다 · kite 거리를 두고 쏜다 · circle 둘레를 돌다가 덮친다
///   공격 모양: arc(앞 부채꼴) · circle(자리 둘레, 예고 원) · charge(일직선 돌진) · shot(투사체) · nova(제 둘레 전체)
/// </summary>
public sealed class MonsterDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>스프라이트 시트 폴더 (res://art/characters/...)</summary>
    public string Art { get; set; }
    /// <summary>시트 한 칸 크기 (px). 큰 몸(골렘·보스)은 192 — tools/fetch_assets.py 의 cell 과 같아야 한다</summary>
    public int Cell { get; set; } = 128;
    /// <summary>이 수치가 맞춰진 레벨. 스폰 레벨이 높으면 체력·피해·경험치가 따라 오른다 (CombatTuning.MonsterHpPerLevel…)</summary>
    public int Level { get; set; } = 1;
    public float Hp { get; set; }
    /// <summary>이만큼 피해가 쌓여야 움찔한다 (공격 중에도). 작을수록 잘 끊긴다.</summary>
    public float Poise { get; set; }
    public float Speed { get; set; }
    public float Sight { get; set; }
    public float Radius { get; set; }
    /// <summary>맞을 때 밀리는 정도 배율 (가벼울수록 큼).</summary>
    public float Knockback { get; set; } = 1f;
    /// <summary>쓰러뜨리면 주는 경험치 (data/player/levels.json 곡선과 함께 본다)</summary>
    public int Exp { get; set; }
    /// <summary>chase · kite · circle</summary>
    public string Move { get; set; } = "chase";
    /// <summary>kite·circle 이 지키려는 거리 (m)</summary>
    public float PreferRange { get; set; } = 6f;
    /// <summary>공격을 마치면 연기처럼 사라져 다른 자리에 나타난다 (단풍 여우)</summary>
    public bool Blink { get; set; }
    /// <summary>강타가 아니면 움찔하지 않는다 (골렘·보스)</summary>
    public bool SuperArmor { get; set; }
    /// <summary>보스: 화면 위에 이름과 큰 체력바, 잠들지 않는다</summary>
    public bool Boss { get; set; }
    public string Title { get; set; }
    /// <summary>머리 높이 (m) — 체력바·이름표 자리. 0 이면 반지름에서 어림</summary>
    public float Height { get; set; }
    public AttackDef Attack { get; set; }
    /// <summary>공격이 여럿이면 거리와 무게로 고른다. 없으면 Attack 하나</summary>
    public List<AttackDef> Attacks { get; set; }
    public GuardDef Guard { get; set; }
    public SoundDef Sounds { get; set; } = new();

    public float HeadHeight => Height > 0f ? Height : Radius * 3.2f + 0.35f;

    public sealed class AttackDef
    {
        public string Id { get; set; } = "attack";
        /// <summary>동작 시트 이름</summary>
        public string Anim { get; set; } = "attack";
        /// <summary>arc · circle · charge · shot · nova</summary>
        public string Shape { get; set; } = "arc";
        public float Damage { get; set; }
        /// <summary>arc: 닿는 거리. circle: 자리까지의 거리. charge: 판정 반지름 여유. shot: 쏘기 시작하는 거리</summary>
        public float Reach { get; set; }
        /// <summary>circle·nova 의 반지름 (m)</summary>
        public float Radius { get; set; } = 2f;
        /// <summary>예고 동작 — 플레이어가 읽고 막기·회피를 고를 시간.</summary>
        public float Windup { get; set; }
        public float Active { get; set; }
        public float Recover { get; set; }
        /// <summary>공격하며 돌진하는 속도 (m/s).</summary>
        public float Lunge { get; set; }
        public float Cooldown { get; set; }
        /// <summary>강한 공격: 예고 동안 바닥에 빨간 원(돌진은 긴 띠)이 차오른다.</summary>
        public bool Telegraph { get; set; }
        /// <summary>이 공격을 고르는 거리 범위 (m). Max 0 이면 Reach</summary>
        public float MinRange { get; set; }
        public float MaxRange { get; set; }
        public float Weight { get; set; } = 1f;
        /// <summary>체력이 이 비율 아래일 때만 쓴다 (보스 2막). 1 이면 늘</summary>
        public float BelowHp { get; set; } = 1f;
        public ShotDef Shot { get; set; }
        public string Sound { get; set; }
        /// <summary>동작에서 '닿는 칸' — 예고가 끝나는 순간 이 칸이 오게 재생을 휜다. 0 이면 고르게</summary>
        public int HitFrame { get; set; }

        public float PickMax => MaxRange > 0f ? MaxRange : Reach;
    }

    /// <summary>적이 쏘는 것. 막으면 사라지고, 패링하면 되돌아간다 (Combat/EnemyShot)</summary>
    public sealed class ShotDef
    {
        public float Speed { get; set; } = 9f;
        public float Range { get; set; } = 14f;
        public float Radius { get; set; } = 0.35f;
        public int Count { get; set; } = 1;
        public float SpreadDeg { get; set; }
        /// <summary>주인공 쪽으로 휘는 세기 (도/초)</summary>
        public float Homing { get; set; }
        /// <summary>색 램프 (CombatFx.Palettes)</summary>
        public string Palette { get; set; } = "sakura";
    }

    /// <summary>껍데기에 숨기 (바위 게): 숨은 동안 피해를 Reduce 배만 받고, 강타에 깨지면 무방비</summary>
    public sealed class GuardDef
    {
        public float Chance { get; set; } = 0.5f;
        public float Time { get; set; } = 1.6f;
        public float Reduce { get; set; } = 0.15f;
    }

    public sealed class SoundDef
    {
        public string Hit { get; set; } = "impactPunch_medium";
        public string Windup { get; set; } = "drawKnife";
        public string Death { get; set; } = "impactPunch_heavy";
        public string Alert { get; set; }
    }

    private static readonly Dictionary<string, MonsterDef> Cache = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool Exists(string id) => Cache.ContainsKey(id) || FileAccess.FileExists($"res://data/monsters/{id}.json");

    public static MonsterDef Get(string id)
    {
        if (!Cache.TryGetValue(id, out var def))
        {
            string json = FileAccess.GetFileAsString($"res://data/monsters/{id}.json");
            def = JsonSerializer.Deserialize<MonsterDef>(json, Options);
            def.Attacks ??= new List<AttackDef>();
            if (def.Attacks.Count == 0 && def.Attack != null)
                def.Attacks.Add(def.Attack);
            def.Attack ??= def.Attacks.Count > 0 ? def.Attacks[0] : new AttackDef();
            Cache[id] = def;
        }
        return def;
    }
}
