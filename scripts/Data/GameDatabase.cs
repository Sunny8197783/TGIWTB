using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// Autoload "GameDb". data/**.json 을 읽어 직업·스킬 정의를 메모리에 올린다.
/// 로더는 여기 하나뿐이고, 다른 코드는 정의를 조회만 한다. (CLAUDE.md 규칙 1)
/// </summary>
public partial class GameDatabase : Node
{
    private const string JobsDir = "res://data/jobs";
    private const string SkillsDir = "res://data/skills";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static GameDatabase Instance { get; private set; }

    private readonly Dictionary<string, JobDefinition> _jobs = new();
    private readonly Dictionary<string, SkillDefinition> _skills = new();

    public IReadOnlyDictionary<string, JobDefinition> Jobs => _jobs;
    public IReadOnlyDictionary<string, SkillDefinition> Skills => _skills;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        Reload();
    }

    public void Reload()
    {
        _jobs.Clear();
        _skills.Clear();

        LoadDirectory(JobsDir, json =>
        {
            var def = JsonSerializer.Deserialize<JobDefinition>(json, JsonOptions);
            if (def == null || string.IsNullOrEmpty(def.Id))
                throw new InvalidOperationException("id 가 비어 있다");
            _jobs[def.Id] = def;
        });

        LoadDirectory(SkillsDir, json =>
        {
            var def = JsonSerializer.Deserialize<SkillDefinition>(json, JsonOptions);
            if (def == null || string.IsNullOrEmpty(def.Id))
                throw new InvalidOperationException("id 가 비어 있다");
            _skills[def.Id] = def;
        });

        GD.Print($"[GameDb] jobs={_jobs.Count} skills={_skills.Count}");
        WarnOnDanglingReferences();
    }

    public JobDefinition GetJob(string id)
    {
        if (id != null && _jobs.TryGetValue(id, out var job))
            return job;
        return null;
    }

    public SkillDefinition GetSkill(string id)
    {
        if (id != null && _skills.TryGetValue(id, out var skill))
            return skill;
        return null;
    }

    /// <summary>히든이 아닌 스킬만. UI 노출용. (규칙 4)</summary>
    public IEnumerable<SkillDefinition> VisibleSkills()
    {
        foreach (var skill in _skills.Values)
        {
            if (!skill.Hidden)
                yield return skill;
        }
    }

    private static void LoadDirectory(string dirPath, Action<string> parse)
    {
        using var dir = DirAccess.Open(dirPath);
        if (dir == null)
        {
            GD.PushWarning($"[GameDb] 디렉터리를 열 수 없다: {dirPath}");
            return;
        }

        foreach (string fileName in dir.GetFiles())
        {
            // 에디터에서는 원본 .json, 익스포트 빌드에서는 .json 그대로 팩에 들어간다.
            string name = fileName.EndsWith(".remap", StringComparison.Ordinal)
                ? fileName.Substring(0, fileName.Length - ".remap".Length)
                : fileName;

            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = $"{dirPath}/{name}";
            string json = Godot.FileAccess.GetFileAsString(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                GD.PushWarning($"[GameDb] 빈 파일 또는 읽기 실패: {path}");
                continue;
            }

            try
            {
                parse(json);
            }
            catch (Exception e)
            {
                GD.PushError($"[GameDb] 파싱 실패 {path}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 아직 파일이 없는 스킬 id 를 가리키는 참조를 한 줄로 요약한다.
    /// 마일스톤이 진행되는 동안에는 정상적으로 비어 있지 않으므로 경고가 아니라 정보로 찍는다.
    /// </summary>
    private void WarnOnDanglingReferences()
    {
        var missing = new SortedSet<string>();

        foreach (var job in _jobs.Values)
        {
            foreach (var pair in job.RankSkills)
            {
                foreach (string skillId in pair.Value)
                {
                    if (!_skills.ContainsKey(skillId))
                        missing.Add(skillId);
                }
            }
        }

        foreach (var skill in _skills.Values)
        {
            string into = skill.Mastery?.Evolution?.Into;
            if (!string.IsNullOrEmpty(into) && !_skills.ContainsKey(into))
                missing.Add(into);
        }

        if (missing.Count > 0)
            GD.Print($"[GameDb] 아직 없는 스킬 참조: {string.Join(", ", missing)}");
    }
}
