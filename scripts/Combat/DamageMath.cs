using PixelMmo.Data;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// 데미지 = (base + Σ stat*계수) * 숙련 배수 * 공격력 버프.
/// 계수는 전부 data/skills/*.json 에서 온다. (CLAUDE.md 규칙 1)
/// </summary>
public static class DamageMath
{
    public static float Compute(IPlayerContext player, SkillDefinition skill, float attackPowerMultiplier = 1f)
    {
        if (skill?.Damage == null)
            return 0f;

        float value = skill.Damage.Base;
        foreach (var pair in skill.Damage.Scaling)
            value += player.GetStat(pair.Key) * pair.Value;

        return value * player.Mastery.DamageMultiplier(skill) * attackPowerMultiplier;
    }
}
