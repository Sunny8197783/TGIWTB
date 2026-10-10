using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 마을 사람 — data/npcs/*.json. 자리·원화·대사. 교관(role=trainer)은 Job 으로 전직시켜 주고,
/// 안내인(role=guide)은 견습생에게 길을 알려 준다. 대사는 상황 키마다 여러 줄:
///   교관: greet(아직 레벨이 모자란 견습생) · offer(전직할 수 있다 → 고르기) · accept(막 전직) · member(이미 이 직업) · other(다른 직업)
///   안내인: greet(견습생) · ready(전직할 레벨) · after(전직 뒤)
/// 줄 안의 {level} 은 그 직업의 전직 레벨, {name} 은 직업 이름으로 바뀐다.
/// </summary>
public sealed class NpcDef
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>이름 위에 작게 (전사 교관·견습생 안내인)</summary>
    public string Title { get; set; }
    public string Role { get; set; } = "guide";
    public string Art { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>교관이 가르치는 직업 id</summary>
    public string Job { get; set; }
    public Dictionary<string, List<string>> Lines { get; set; } = new();

    private static List<NpcDef> _all;
    public static IReadOnlyList<NpcDef> All => _all ??= Load();

    private static List<NpcDef> Load()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var list = new List<NpcDef>();
        foreach (string f in DirAccess.GetFilesAt("res://data/npcs"))
            if (f.EndsWith(".json"))
                list.Add(JsonSerializer.Deserialize<NpcDef>(FileAccess.GetFileAsString("res://data/npcs/" + f), opts));
        return list;
    }
}
