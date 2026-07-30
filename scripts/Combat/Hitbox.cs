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
        float offset = facing.Normalized().AngleTo(delta / distance);
        return Mathf.Abs(offset) <= half;
    }

    public static bool InCircle(Vector2 origin, float range, Vector2 target, float targetRadius)
        => origin.DistanceTo(target) <= range + targetRadius;

    /// <summary>
    /// 이번 프레임에 무기가 훑고 지나간 영역. 그리는 무기(선분)가 fromOffset → toOffset
    /// 으로 회전하면서 만드는 부채꼴 띠를 그대로 판정으로 쓴다.
    ///
    /// 각도는 baseAngle 기준 상대값(rad), 반지름은 자루 끝(inner) ~ 칼끝(outer).
    /// 무기 두께와 대상 반지름만큼은 후하게 쳐 준다 — 스치는 판정이 답답하지 않게.
    ///
    /// 프레임 사이의 회전 구간 전체를 보므로, 빠른 스윙이 대상을 뚫고 지나가지 않는다.
    /// </summary>
    public static bool SweptArc(Vector2 origin, float baseAngle, float fromOffset, float toOffset,
        float inner, float outer, float weaponWidth, Vector2 target, float targetRadius)
    {
        Vector2 delta = target - origin;
        float distance = delta.Length();
        float pad = targetRadius + weaponWidth * 0.5f;

        if (distance > outer + pad || distance < inner - pad)
            return false;

        // 겹쳐 있으면 방향을 따질 수 없다.
        if (distance <= pad)
            return true;

        float relative = Mathf.Wrap(Mathf.Atan2(delta.Y, delta.X) - baseAngle, -Mathf.Pi, Mathf.Pi);

        // 이 거리에서 대상이 차지하는 각도만큼 여유를 준다.
        float tolerance = Mathf.Atan2(pad, distance);
        float low = Mathf.Min(fromOffset, toOffset) - tolerance;
        float high = Mathf.Max(fromOffset, toOffset) + tolerance;

        return relative >= low && relative <= high;
    }
}
