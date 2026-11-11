using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 이름(직업) — data/jobs/*.json (CLAUDE.md 규칙 1). 무명인으로 시작해 이름지기 NPC 에게서 이름을 받거나,
/// 세상이 행적을 보고 붙여 준다 (Requires·Hidden). 설계: docs/design/NAMELESS.md
/// 이름이 정하는 것: 평타(연속기 한 벌 — 근접 베기 또는 투사체), 스킬 칸, 원화(성별마다), 레벨당 체력·힘.
/// 이름의 나무: From(어느 이름에서 뻗나) + Tier. 아직 그림·기술이 없는 이름은 Sealed — 이름록에 실루엣으로만 보인다.
/// </summary>
public sealed class JobDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int Tier { get; set; }
    /// <summary>전직 조건: 이 레벨 이상이고 지금 직업이 From 중 하나. 견습생은 0</summary>
    public int RequiredLevel { get; set; }
    public List<string> From { get; set; } = new();
    /// <summary>전직시켜 주는 NPC id (data/npcs)</summary>
    public string Trainer { get; set; }
    /// <summary>레벨·From 말고 더 채워야 하는 조건 (행적·각인·거친 이름·이야기 표시)</summary>
    public Requirement Requires { get; set; }
    /// <summary>히든 이름: 얻기 전에는 이름록에 나오지 않고, 조건은 어디에도 보이지 않는다 (규칙 4)</summary>
    public bool Hidden { get; set; }
    /// <summary>아직 열리지 않은 이름 (다음 업데이트) — 될 수 없다</summary>
    public bool Sealed { get; set; }
    /// <summary>이름의 한 글자 (문장·이름록에 찍는다): 無 誓 墨 風 呼</summary>
    public string Glyph { get; set; } = "名";
    /// <summary>한 줄 뜻 (이름록)</summary>
    public string Motto { get; set; }
    /// <summary>빛의 무기 모양 — 평타·스킬 이펙트가 따른다: blade · twin · bow · tome</summary>
    public string Weapon { get; set; } = "blade";
    /// <summary>원화: 성별(m·f) → 시트 폴더와 옷 색 범위. 없으면 처음 고른 모습 그대로 (견습생)</summary>
    public Dictionary<string, AppearanceDef.BaseDef> Art { get; set; }
    public float Hp { get; set; } = 100f;
    public float HpPerLevel { get; set; }
    /// <summary>레벨당 피해 증가율 (0.05 = 레벨마다 +5%)</summary>
    public float PowerPerLevel { get; set; }
    /// <summary>평타 연속기 (마지막 타까지 이어 누르면 처음으로)</summary>
    public List<StepDef> Attack { get; set; } = new();
    /// <summary>스킬 칸: 순서대로 Q·E·R. 레벨이 차야 열린다</summary>
    public List<SkillSlot> Skills { get; set; } = new();
    /// <summary>평타 궤적·투사체 색 램프 (CombatFx.Palettes)</summary>
    public string Palette { get; set; } = "teal";
    /// <summary>이름마다 다른 손맛 (Combat/JobFx): 궤적·쏠 때·맞은 자리의 전용 이펙트</summary>
    public FxDef Fx { get; set; } = new();

    public sealed class FxDef
    {
        /// <summary>궤적: light(빛의 호) · ink(먹 붓질)</summary>
        public string Swing { get; set; } = "light";
        /// <summary>궤적 메아리: 이만큼(초) 뒤에 얇은 호가 한 번 더 따라 긋는다 (0 = 없음)</summary>
        public float Echo { get; set; }
        /// <summary>쏠 때: bow(빛의 활) · ring(발밑 부름 고리) · none</summary>
        public string Cast { get; set; } = "none";
        /// <summary>맞은 자리: seal(맹세의 인장) · blot(먹 튐) · gust(회오리) · chime(울림 고리) · none</summary>
        public string Hit { get; set; } = "none";
        /// <summary>강타가 맞으면 이름 글자(Glyph)가 찍힌다</summary>
        public bool Glyph { get; set; }
        /// <summary>평타를 내디딜 때 잔상을 남긴다</summary>
        public bool Ghost { get; set; }
    }

    public sealed class SkillSlot
    {
        public string Id { get; set; }
        public int Level { get; set; } = 1;
    }

    /// <summary>
    /// 연속기 한 타. 시간은 초. 원화는 준비 동작이 길어 '닿는 칸'(HitFrame)이 판정 순간(HitAt)에 오게 재생을 휜다.
    /// Projectile 이 있으면 HitAt 에 쏘고 근접 판정은 없다.
    /// </summary>
    public sealed class StepDef
    {
        public string Anim { get; set; }
        /// <summary>빚은 몸(무기를 들지 않은 원화)에 이 동작이 없을 때 대신 쓰는 동작 (cast·cast_big)</summary>
        public string AnimAlt { get; set; }
        public float Duration { get; set; }
        public float HitAt { get; set; }
        public float CancelAt { get; set; }
        /// <summary>판정 전까지 앞으로 내딛는 거리 (m)</summary>
        public float Lunge { get; set; }
        public float Damage { get; set; }
        public float Reach { get; set; }
        public float HalfArcDeg { get; set; } = 70f;
        public bool Heavy { get; set; }
        public int StartFrame { get; set; }
        public int HitFrame { get; set; }
        /// <summary>궤적 모양: horizontal · reverse · vertical · stab · none</summary>
        public string Slash { get; set; } = "horizontal";
        public float SlashScale { get; set; } = 1f;
        public string Sound { get; set; } = "swing";
        public ProjectileDef Projectile { get; set; }
    }

    public sealed class ProjectileDef
    {
        /// <summary>arrow(화살) · bolt(마법탄) · orb(터지는 큰 구슬)</summary>
        public string Kind { get; set; } = "arrow";
        public float Speed { get; set; } = 24f;
        public float Range { get; set; } = 14f;
        /// <summary>맞는 반지름 (m)</summary>
        public float Radius { get; set; } = 0.35f;
        /// <summary>뚫고 지나가는 적 수 (0 = 첫 적에서 멈춘다)</summary>
        public int Pierce { get; set; }
        public int Count { get; set; } = 1;
        public float SpreadDeg { get; set; }
        /// <summary>멈춘 자리에서 터지는 반지름 (0 = 안 터진다)</summary>
        public float Explode { get; set; }
        /// <summary>가까운 적 쪽으로 휘는 세기 (도/초)</summary>
        public float Homing { get; set; }
        /// <summary>하늘에서 떨어진다 (화살비) — 땅에 닿는 자리의 Radius 안을 친다</summary>
        public bool Fall { get; set; }
    }

    public AppearanceDef.BaseDef ArtFor(string gender) =>
        Art == null ? null : Art.TryGetValue(gender ?? "m", out var a) ? a : Art.Values.FirstOrDefault();

    public float MaxHp(int level) => Hp + HpPerLevel * (level - 1);
    public float Power(int level) => 1f + PowerPerLevel * (level - 1);

    public const string Novice = "novice";

    private static Dictionary<string, JobDef> _all;
    /// <summary>모든 직업 (파일 이름이 아니라 id 로)</summary>
    public static IReadOnlyDictionary<string, JobDef> All => _all ??= Load();

    public static JobDef Get(string id) => id != null && All.TryGetValue(id, out var j) ? j : All[Novice];

    private static Dictionary<string, JobDef> Load()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var map = new Dictionary<string, JobDef>();
        foreach (string f in DirAccess.GetFilesAt("res://data/jobs"))
        {
            if (!f.EndsWith(".json"))
                continue;
            var def = JsonSerializer.Deserialize<JobDef>(FileAccess.GetFileAsString("res://data/jobs/" + f), opts);
            map[def.Id] = def;
        }
        return map;
    }
}

/// <summary>
/// 각인 — data/player/engrave.json. 한 이름으로 쌓은 유효 처치(행적 engrave:&lt;이름&gt;)가 단계를 올리고,
/// 단계마다 그 이름의 힘·체력이 오른다. 한 이름만 깊이 파도 강해진다 (이름에 귀천이 없다).
/// </summary>
public sealed class EngraveDef
{
    /// <summary>index i = (i+1) 단계에 필요한 누적 점수</summary>
    public List<int> Thresholds { get; set; } = new();
    public float PowerPerStage { get; set; }
    public float HpPerStage { get; set; }
    /// <summary>단계 이름 (0 = 아직 새기지 않음)</summary>
    public List<string> Titles { get; set; } = new();

    public int MaxStage => Thresholds.Count;

    public int Stage(int points)
    {
        int s = 0;
        while (s < Thresholds.Count && points >= Thresholds[s])
            s++;
        return s;
    }

    /// <summary>다음 단계까지 0~1 (끝 단계면 1)</summary>
    public float Progress(int points)
    {
        int s = Stage(points);
        if (s >= Thresholds.Count)
            return 1f;
        int from = s == 0 ? 0 : Thresholds[s - 1];
        return (points - from) / (float)(Thresholds[s] - from);
    }

    public string Title(int stage) => stage >= 0 && stage < Titles.Count ? Titles[stage] : "";

    private static EngraveDef _i;
    public static EngraveDef Instance => _i ??= JsonSerializer.Deserialize<EngraveDef>(
        FileAccess.GetFileAsString("res://data/player/engrave.json"),
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
}

/// <summary>레벨 곡선 — data/player/levels.json</summary>
public sealed class LevelDef
{
    public int MaxLevel { get; set; }
    /// <summary>index i = 레벨 (i+1) 에서 다음 레벨까지 필요한 경험치</summary>
    public List<int> ExpToNext { get; set; } = new();

    public int Need(int level) => level >= MaxLevel ? 0 : ExpToNext[Mathf.Clamp(level - 1, 0, ExpToNext.Count - 1)];

    private static LevelDef _i;
    public static LevelDef Instance => _i ??= JsonSerializer.Deserialize<LevelDef>(
        FileAccess.GetFileAsString("res://data/player/levels.json"),
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
}
