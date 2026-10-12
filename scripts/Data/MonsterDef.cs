using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 몬스터 정의 — data/monsters/*.json (CLAUDE.md 규칙 1). 시간은 초, 거리는 m.
/// "base": 다른 몬스터를 바탕으로 적은 칸만 덮어쓴다 (색만 바꾼 지역 변종: 이끼 늑대 = 들개 + recolor + 높은 수치).
/// </summary>
public sealed class MonsterDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>스프라이트 시트 폴더 (res://art/characters/...)</summary>
    public string Art { get; set; }
    public int Level { get; set; } = 1;
    public float Hp { get; set; }
    /// <summary>이만큼 피해가 쌓여야 움찔한다 (공격 중에도). 작을수록 잘 끊긴다.</summary>
    public float Poise { get; set; }
    public float Speed { get; set; }
    public float Sight { get; set; }
    public float Radius { get; set; }
    /// <summary>맞을 때 밀리는 정도 배율 (가벼울수록 큼).</summary>
    public float Knockback { get; set; } = 1f;
    /// <summary>쓰러뜨리면 주는 경험치 — 레벨 차이로 깎이고 늘어난다 (data/player/levels.json exp_level_diff)</summary>
    public int Exp { get; set; }
    /// <summary>집에서 이만큼 끌려 나오면 체력을 채우며 돌아간다</summary>
    public float Leash { get; set; } = 30f;
    /// <summary>원거리: 주인공이 이보다 가까우면 뒷걸음질 (0 = 안 함)</summary>
    public float KeepAway { get; set; }
    /// <summary>앞쪽 이 반각 안에서 맞으면 막는다 (등껍질). 강타·경직·공격 중엔 못 막는다. 0 = 안 막음</summary>
    public float GuardArcDeg { get; set; }
    /// <summary>막았을 때 들어가는 피해 비율</summary>
    public float GuardTake { get; set; } = 0.15f;
    /// <summary>우두머리: 화면 위 큰 체력바, 단계(phases), 경직 저항</summary>
    public bool Boss { get; set; }
    /// <summary>투사체·충격파 색 (CombatFx.Palettes)</summary>
    public string Palette { get; set; } = "fire";
    public List<AttackDef> Attacks { get; set; } = new();
    public List<PhaseDef> Phases { get; set; } = new();
    public RecolorDef Recolor { get; set; }
    public SoundDef Sounds { get; set; } = new();

    public sealed class AttackDef
    {
        /// <summary>
        /// melee: 앞으로 돌진하며 부채꼴 · charge: 길게 노려본 뒤 일직선 돌진(바닥에 띠) ·
        /// shoot: 투사체 · slam: 원 안을 내려찍기(leap 이면 주인공 자리로 뛰어든다) · spin: 제자리 둘레
        /// </summary>
        public string Kind { get; set; } = "melee";
        /// <summary>시트 이름 (attack, attack2 …)</summary>
        public string Anim { get; set; } = "attack";
        public float Damage { get; set; }
        /// <summary>이 거리 안에서만 고른다 (투사체는 쏘기 시작하는 거리)</summary>
        public float Reach { get; set; }
        /// <summary>이보다 가까우면 안 고른다 (돌진·도약은 거리가 있어야)</summary>
        public float MinRange { get; set; }
        /// <summary>예고 동작 — 플레이어가 읽고 막기·회피를 고를 시간.</summary>
        public float Windup { get; set; }
        public float Active { get; set; }
        public float Recover { get; set; }
        /// <summary>공격하며 돌진하는 속도 (m/s). charge 는 돌진 속도</summary>
        public float Lunge { get; set; }
        public float Cooldown { get; set; }
        /// <summary>여럿 중 고를 때의 무게</summary>
        public float Weight { get; set; } = 1f;
        /// <summary>바닥 예고 (빨간 원·띠가 차오른다)</summary>
        public bool Telegraph { get; set; }
        /// <summary>막기로 못 막는다 — 피해야 한다 (예고가 자줏빛)</summary>
        public bool Unblockable { get; set; }
        /// <summary>slam·spin 의 원 반지름</summary>
        public float Radius { get; set; }
        /// <summary>slam: 예고 중 주인공 자리를 찍어 두고 그리로 뛰어든다</summary>
        public bool Leap { get; set; }
        /// <summary>같은 공격을 몇 번 잇는가 (연속 베기). 두 번째부터 예고는 Gap</summary>
        public int Hits { get; set; } = 1;
        public float Gap { get; set; } = 0.25f;
        /// <summary>공격 뒤 주인공에게서 이만큼 떨어진 곳으로 순간이동 (여우불)</summary>
        public float Blink { get; set; }
        /// <summary>우두머리 이 단계부터 쓴다 (0 = 처음부터)</summary>
        public int Phase { get; set; }
        public JobDef.ProjectileDef Projectile { get; set; }
    }

    /// <summary>우두머리 단계: 체력이 hp_below 아래로 내려가면 빨라지고 새 공격이 열린다</summary>
    public sealed class PhaseDef
    {
        public float HpBelow { get; set; }
        public float Speed { get; set; } = 1f;
        /// <summary>대기시간·예고 배율 (작을수록 몰아친다)</summary>
        public float Tempo { get; set; } = 1f;
        public string Line { get; set; }
    }

    /// <summary>불러올 때 색상 범위를 옮긴다 (Render/Recolor) — 같은 그림으로 지역 변종</summary>
    public sealed class RecolorDef
    {
        public float[] Hue { get; set; }
        public float MinSat { get; set; } = 0.15f;
        public float MinVal { get; set; }
        public float To { get; set; }
        public float Sat { get; set; } = 1f;
        public float Val { get; set; } = 1f;
    }

    public sealed class SoundDef
    {
        public string Hit { get; set; } = "impactPunch_medium";
        public string Windup { get; set; } = "drawKnife";
        public string Death { get; set; } = "impactPunch_heavy";
        public string Shoot { get; set; } = "drawKnife";
    }

    private static readonly Dictionary<string, MonsterDef> Cache = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    private static readonly JsonNodeOptions NodeOptions = new();
    private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>`--selftest`: 몬스터 JSON 이 다 읽히고(base 덮어쓰기 포함) 공격이 말이 되는지, 레벨 차이 경험치 배율</summary>
    public static void SelfCheck()
    {
        int n = 0;
        foreach (string f in DirAccess.GetFilesAt("res://data/monsters"))
        {
            if (!f.EndsWith(".json"))
                continue;
            var d = Get(f[..^5]);
            Check(d.Id == f[..^5] && d.Hp > 0f && d.Attacks.Count > 0, $"{f}: id·hp·attacks");
            foreach (var a in d.Attacks)
            {
                Check(a.Reach > a.MinRange && a.Windup > 0f, $"{f}: reach·windup");
                Check(a.Kind != "shoot" || a.Projectile != null, $"{f}: shoot 에 projectile 이 없다");
                Check(a.Kind is not ("slam" or "spin") || a.Radius > 0f, $"{f}: slam·spin 에 radius 가 없다");
            }
            n++;
        }
        var moss = Get("moss_wolf");
        Check(moss.Art == Get("field_wolf").Art && moss.Level == 17 && moss.Recolor != null, "base 덮어쓰기");
        var lv = LevelDef.Instance;
        Check(Mathf.IsEqualApprox(lv.ExpFactor(10, 10), 1f), "같은 레벨 = 1");
        Check(lv.ExpFactor(2, 20) < 0.2f && lv.ExpFactor(30, 20) <= lv.ExpAboveMax, "레벨 차이 배율");
        GD.Print($"[Monsters] selftest OK ({n}종)");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
            throw new System.Exception("[Monsters] " + what);
    }

    public static MonsterDef Get(string id)
    {
        if (!Cache.TryGetValue(id, out var def))
            Cache[id] = def = Load(id).Deserialize<MonsterDef>(Options);
        return def;
    }

    /// <summary>"base" 를 따라 올라가 바탕 위에 덮어쓴 JSON (얕은 덮어쓰기 — 칸 하나를 통째로 바꾼다)</summary>
    private static JsonObject Load(string id)
    {
        var node = JsonNode.Parse(FileAccess.GetFileAsString($"res://data/monsters/{id}.json"), NodeOptions, DocOptions).AsObject();
        if (node["base"]?.GetValue<string>() is not string baseId)
            return node;
        var merged = Load(baseId);
        foreach (var (key, value) in node)
            if (key != "base")
                merged[key] = value?.DeepClone();
        return merged;
    }
}
