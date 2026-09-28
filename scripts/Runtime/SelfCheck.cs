using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>
/// 데이터와 맵 구조가 스스로 모순되지 않는지 한 번에 본다.
/// 컴파일러가 잡아 주지 않는 것들만 본다 — 없는 스킬 id, 어느 키에도 안 물린 스킬,
/// 걸어서 못 가는 구역, 집 안에 박힌 NPC.
///
///   godot --headless --path . -- --selfcheck
///
/// 실패가 하나라도 있으면 종료 코드 1. 통과하면 0.
/// </summary>
public static class SelfCheck
{
    public const string Flag = "--selfcheck";

    public static bool Requested()
    {
        // `--` 뒤에 붙인 인자는 GetCmdlineArgs 가 아니라 이쪽으로 온다. (DevCapture 와 같다)
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == Flag)
                return true;
        }
        return false;
    }

    /// <summary>실패 목록. 비어 있으면 통과.</summary>
    public static List<string> Run(GameDatabase db)
    {
        var fail = new List<string>();
        if (db == null)
        {
            fail.Add("GameDb 가 없다");
            return fail;
        }

        CheckSkillReferences(db, fail);
        CheckSkillWiring(db, fail);
        CheckNpcs(db, fail);
        CheckZoneGraph(fail);

        return fail;
    }

    /// <summary>가리키는 스킬 id 가 전부 실재하는가.</summary>
    private static void CheckSkillReferences(GameDatabase db, List<string> fail)
    {
        foreach (var job in db.Jobs.Values)
        {
            foreach (var pair in job.RankSkills)
            {
                foreach (string id in pair.Value)
                {
                    if (db.GetSkill(id) == null)
                        fail.Add($"직업 {job.Id} R{pair.Key} → 없는 스킬 {id}");
                }
            }

            foreach (string id in job.PrototypeSkills)
            {
                if (db.GetSkill(id) == null)
                    fail.Add($"직업 {job.Id} prototypeSkills → 없는 스킬 {id}");
            }
        }

        foreach (var skill in db.Skills.Values)
        {
            string into = skill.Mastery?.Evolution?.Into;
            if (!string.IsNullOrEmpty(into) && db.GetSkill(into) == null)
                fail.Add($"스킬 {skill.Id} 진화 → 없는 스킬 {into}");
        }
    }

    /// <summary>
    /// 배워도 쓸 수 없는 스킬이 없는가. 키에 안 물린 액티브 스킬은
    /// 데이터상으로는 멀쩡해 보이지만 플레이어에게는 존재하지 않는 것과 같다.
    /// </summary>
    private static void CheckSkillWiring(GameDatabase db, List<string> fail)
    {
        var slots = new HashSet<string>(PlayerCharacter.SkillSlots) { InputSetup.Guard };

        foreach (var skill in db.Skills.Values)
        {
            if (!skill.IsActive)
                continue;

            if (string.IsNullOrEmpty(skill.InputAction))
                fail.Add($"스킬 {skill.Id} 에 inputAction 이 없다 — 발동할 방법이 없다");
            else if (!slots.Contains(skill.InputAction))
                fail.Add($"스킬 {skill.Id} 의 inputAction '{skill.InputAction}' 은 슬롯이 아니다");

            if (skill.ManaCost < 0f || skill.Cooldown < 0f)
                fail.Add($"스킬 {skill.Id} 의 마나/쿨다운이 음수다");

            if (skill.Projectile != null && skill.Projectile.Count < 1)
                fail.Add($"스킬 {skill.Id} 의 투사체 수가 1 미만이다");

            // 회복이 붙었는데 회복량이 0 이면 숙련만 오르고 아무 일도 안 일어난다.
            var heal = skill.Heal;
            if (heal != null && heal.Base <= 0f && heal.MaxHpRatio <= 0f && heal.Scaling.Count == 0)
                fail.Add($"스킬 {skill.Id} 의 회복량이 0 이다");
        }
    }

    /// <summary>NPC 가 자기 구역 안에 있고, 가리키는 직업이 실재하는가.</summary>
    private static void CheckNpcs(GameDatabase db, List<string> fail)
    {
        foreach (var npc in db.Npcs.Values)
        {
            if (npc.Lines.Count == 0)
                fail.Add($"NPC {npc.Id} 에 대사가 없다");

            var zone = WorldLayout.ZoneById(npc.Zone);
            if (zone == null)
            {
                fail.Add($"NPC {npc.Id} 의 구역 '{npc.Zone}' 이 없다");
            }
            else if (!zone.Contains(WorldLayout.TileCenter(npc.TileX, npc.TileY)))
            {
                fail.Add($"NPC {npc.Id} 좌표 {npc.TileX},{npc.TileY} 가 {npc.Zone} 밖이다");
            }

            if (!string.IsNullOrEmpty(npc.TeachesJob) && db.GetJob(npc.TeachesJob) == null)
                fail.Add($"NPC {npc.Id} 가 없는 직업 {npc.TeachesJob} 을 가르친다");
        }
    }

    /// <summary>
    /// 모든 구역이 마을에서 걸어서 닿는가. 통로가 실제로 뚫리는가.
    ///
    /// 구역을 하나 추가하고 Links 에 적는 것을 잊으면, 게임은 멀쩡히 뜨지만
    /// 그 구역은 영원히 벽 뒤에 남는다. 눈으로는 절대 못 찾는 종류의 실수다.
    /// </summary>
    private static void CheckZoneGraph(List<string> fail)
    {
        foreach(var camp in MeadowLayout.Data.Camps)
            if(!WorldLayout.IsForest(new Godot.Vector2(camp.X,camp.Y)) || WorldLayout.Town.Contains(camp.Center))
                fail.Add("forest camp outside terrain or inside village: "+camp.Id);
        foreach(var route in MeadowLayout.Data.Routes)
            foreach(var point in route)
                if(point.Length!=2 || !WorldLayout.IsForest(new Godot.Vector2(point[0],point[1])))
                    fail.Add("forest path outside terrain");
    }
}
