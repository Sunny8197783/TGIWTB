using Godot;
using PixelMmo.Data;

namespace PixelMmo.Combat;

/// <summary>스킬 판정 범위. 도형 하나로 끝나므로 물리 Area2D 를 쓰지 않고 직접 계산한다.</summary>
public static class Hitbox
{
    /// <summary>data/skills 의 shape 정의대로 대상이 범위 안인지 본다.</summary>
    public static bool Overlaps(SkillShape shape, Vector2 origin, Vector2 facing,
        Vector2 target, float targetRadius)
    {
        if (shape == null)
            return false;

        return shape.Kind switch
        {
            "cone" => InCone(origin, facing, shape.AngleDeg, shape.RangePx, target, targetRadius),
            "circle" => InCircle(origin, shape.RangePx, target, targetRadius),
            _ => false,
        };
    }

    /// <summary>전방 부채꼴. angleDeg 는 전체 각도(90 이면 좌우 45도씩).</summary>
    public static bool InCone(Vector2 origin, Vector2 facing, float angleDeg, float range,
        Vector2 target, float targetRadius)
    {
        Vector2 delta = target - origin;
        float distance = delta.Length();

        // 대상 반지름만큼은 후하게 쳐 준다 — 스치는 판정이 답답하지 않게.
        if (distance > range + targetRadius)
            return false;

        // 겹쳐 있으면 방향을 따질 수 없다. 무조건 맞은 것으로 본다.
        if (distance <= targetRadius || facing == Vector2.Zero)
            return true;

        float half = Mathf.DegToRad(angleDeg) * 0.5f;
        return facing.Normalized().AngleTo(delta / distance) is var angle
            && Mathf.Abs(angle) <= half;
    }

    public static bool InCircle(Vector2 origin, float range, Vector2 target, float targetRadius)
        => origin.DistanceTo(target) <= range + targetRadius;
}
