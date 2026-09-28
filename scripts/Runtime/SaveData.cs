using System.Collections.Generic;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>user://save_01.json 스키마 그대로. (§H)</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveSystem.CurrentVersion;
    public string WorldRevision { get; set; } = "";
    public int Level { get; set; } = 1;
    public float Hp { get; set; } = 100f;
    public float? Stamina { get; set; }
    public SavePosition Position { get; set; } = new();
    public Dictionary<string, int> BaseStats { get; set; } = new();
    public SaveJobState JobState { get; set; } = new();
    public Dictionary<string, float> Mastery { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public SaveAppearance Appearance { get; set; }
}

/// <summary>외형 선택과 rrggbb 색상. 새 필드가 없는 이전 저장도 기본값으로 읽는다.</summary>
public sealed class SaveAppearance
{
    public bool UsePixelLab { get; set; }=true;
    public bool Female { get; set; }
    public bool Confirmed { get; set; }
    public int HairStyle { get; set; }
    public int EyeShape { get; set; }
    public int Body { get; set; }
    public string Skin { get; set; } = "";
    public string Hair { get; set; } = "";
    public string Shirt { get; set; } = "";
    public string Pants { get; set; } = "";
}

public sealed class SavePosition
{
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class SaveJobState
{
    public string CurrentJobId { get; set; } = "";
    public List<JobRecord> History { get; set; } = new();
    public List<string> LearnedSkills { get; set; } = new();
    public List<string> CarriedSkillSlots { get; set; } = new();
    public Dictionary<string, int> InheritedStats { get; set; } = new();
}
