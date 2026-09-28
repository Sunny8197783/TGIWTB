using Godot;

namespace PixelMmo.Combat;

/// <summary>Animation contact frames are independent of the duration of each combat phase.</summary>
public static class CombatMotion
{
    public static float Progress(string clip, SkillPhase phase, float progress, int contactFrame=-1)
    {
        // Existing source clips: punch contacts at frame 3/6; heavy descends at 6/10.
        var (count, contact, recover) = clip switch
        {
            "hero_slash" => (9f, 5f, 8f),
            "hero_heavy" => (9f, 6f, 8f),
            "sword" => (9f, 4f, 7f),
            "sword_heavy" => (9f, 5f, 7f),
            "heavy" => (10f, 6f, 8f),
            "shout" => (6f, 3f, 5f),
            _ => (6f, 3f, 5f),
        };
        if(contactFrame>=0) contact=Mathf.Clamp(contactFrame,0f,count-1f);
        recover=Mathf.Max(contact,recover);
        float frame = phase switch
        {
            SkillPhase.Windup => Mathf.Lerp(0f, contact, Mathf.Clamp(progress, 0f, 0.999f)),
            SkillPhase.Active => Mathf.Lerp(contact, recover, Mathf.Clamp(progress, 0f, 0.999f)),
            SkillPhase.Recovery => Mathf.Lerp(recover, count - 0.001f, Mathf.Clamp(progress, 0f, 1f)),
            _ => 0f,
        };
        return frame / count;
    }
}
