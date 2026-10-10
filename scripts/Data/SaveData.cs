using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 세이브 한 장 — user://save.json. 지금은 숙련·히든 습득·스킬 칸만.
/// ponytail: 통째로 덮어쓴다. 쓰는 도중 꺼지면 날아갈 수 있다 — 항목이 늘면 임시 파일에 쓰고 이름 바꾸기로.
/// </summary>
public sealed class SaveData
{
    public const string Path = "user://save.json";

    public Dictionary<string, float> Mastery { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public List<string> Learned { get; set; } = new();
    /// <summary>모습 (AppearanceDef). null 이면 아직 고르지 않았다 → 처음 켤 때 모습 고르기가 열린다</summary>
    public string LookBase { get; set; }
    public int LookAccent { get; set; }
    /// <summary>깨운 여신상 id (meta.statues). 빠른 이동 목록</summary>
    public List<string> Statues { get; set; } = new();
    /// <summary>마지막으로 기도한 여신상 — 쓰러지면, 다시 켜면 여기서 시작한다</summary>
    public string RespawnStatue { get; set; }
    /// <summary>레벨·경험치(다음 레벨까지 모은 것)·직업 id (data/jobs)</summary>
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public string Job { get; set; } = "novice";

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>없거나 깨졌으면 빈 세이브.</summary>
    public static SaveData Load()
    {
        if (!FileAccess.FileExists(Path))
            return new SaveData();
        try
        {
            return JsonSerializer.Deserialize<SaveData>(FileAccess.GetFileAsString(Path), Options) ?? new SaveData();
        }
        catch (JsonException e)
        {
            // 덮어쓰기 전에 옆으로 치워 둔다 — 고쳐 볼 여지를 남긴다
            DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(Path), ProjectSettings.GlobalizePath("user://save.broken.json"));
            GD.PushWarning($"[Save] 읽지 못해 save.broken.json 으로 옮기고 새로 시작: {e.Message}");
            return new SaveData();
        }
    }

    public void Write()
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, Options));
    }
}
