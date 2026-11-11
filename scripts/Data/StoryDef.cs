using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 이야기 줄기 — data/story/*.json (실행은 World/Story). 줄기 하나는 단계의 줄: 단계마다 목표 하나와, 시작·끝에 벌어지는 일들.
/// 여러 줄기가 따로 흐른다 — 꼭 하나만 따라갈 필요가 없다 (설계: docs/design/NAMELESS.md).
/// 목표(goal.type): talk(npc) · kill(monster, count) · restore(hush) · restore_any(any, count) · job · level · flag · reach(x, z, r) · engrave(count) · deed(deed, count)
/// 일(do): say(speaker, lines) · name · flag · exp · shards · announce(text, sub) · companion(join|leave|stay) · heal · flask
/// 글 안의 {name} 은 주인공 이름으로 바뀐다.
/// </summary>
public sealed class StoryDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
    /// <summary>이야기 목록에서의 차례 (본 줄기 0)</summary>
    public int Order { get; set; }
    /// <summary>이 표시가 서야 줄기가 열린다 (없으면 처음부터)</summary>
    public string Needs { get; set; }
    public List<StepDef> Steps { get; set; } = new();

    public sealed class StepDef
    {
        public string Id { get; set; }
        /// <summary>화면 오른쪽에 뜨는 할 일 한 줄</summary>
        public string Text { get; set; }
        public GoalDef Goal { get; set; } = new();
        /// <summary>길잡이가 가리키는 자리 [x, z]</summary>
        public float[] Marker { get; set; }
        /// <summary>talk 목표: 그 사람이 하는 말</summary>
        public List<string> Lines { get; set; }
        public List<ActionDef> OnStart { get; set; }
        public List<ActionDef> OnDone { get; set; }
    }

    public sealed class GoalDef
    {
        public string Type { get; set; } = "flag";
        public string Npc { get; set; }
        public string Monster { get; set; }
        public int Count { get; set; } = 1;
        public string Hush { get; set; }
        public List<string> Any { get; set; }
        public string Job { get; set; }
        public int Level { get; set; }
        public string Flag { get; set; }
        public string Deed { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        public float R { get; set; } = 6f;
    }

    public sealed class ActionDef
    {
        public string Do { get; set; }
        public string Speaker { get; set; }
        public List<string> Lines { get; set; }
        public string Flag { get; set; }
        public int Value { get; set; } = 1;
        public string Text { get; set; }
        public string Sub { get; set; }
        public string Mode { get; set; }
    }

    private static List<StoryDef> _all;
    public static IReadOnlyList<StoryDef> All => _all ??= Load();

    private static List<StoryDef> Load()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var list = new List<StoryDef>();
        foreach (string f in DirAccess.GetFilesAt("res://data/story"))
            if (f.EndsWith(".json"))
                list.Add(JsonSerializer.Deserialize<StoryDef>(FileAccess.GetFileAsString("res://data/story/" + f), opts));
        return list.OrderBy(s => s.Order).ToList();
    }
}
