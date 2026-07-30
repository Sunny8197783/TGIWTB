using System.Collections.Generic;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>user://save_01.json 스키마 그대로. (§H)</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveSystem.CurrentVersion;
    public int Level { get; set; } = 1;
    public float Hp { get; set; } = 100f;
    public SavePosition Position { get; set; } = new();
    public Dictionary<string, int> BaseStats { get; set; } = new();
    public SaveJobState JobState { get; set; } = new();
    public Dictionary<string, float> Mastery { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
    public SaveAppearance Appearance { get; set; }
}

/// <summary>겉모습 색 슬롯. 각 필드는 "rrggbb" hex. null 이면 기본 겉모습으로 폴백.</summary>
public sealed class SaveAppearance
{
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
