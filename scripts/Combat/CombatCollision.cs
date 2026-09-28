using Godot;

namespace PixelMmo.Combat;

public static class CombatCollision
{
    public static bool ClearSight(Node2D owner, Vector2 from, Vector2 to)
    {
        if (from.IsEqualApprox(to)) return true;
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view) return view.ClearSight(from, to);
        using var query = PhysicsRayQueryParameters2D.Create(from, to, CollisionLayers.World);
        query.HitFromInside = true;
        return owner.GetWorld2D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    /// <summary>Sweep the full projectile radius, including overlap at the starting point.</summary>
    public static bool Sweep(Node2D owner, Vector2 from, Vector2 motion, float radius,
        uint mask, Godot.Collections.Array<Rid> excluded, out Node2D body, out Vector2 position)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
            return view.Sweep(owner, from, motion, radius, mask, excluded, out body, out position);
        body = null;
        position = from + motion;
        var space = owner.GetWorld2D().DirectSpaceState;
        using var circle = new CircleShape2D { Radius = radius };
        using var query = new PhysicsShapeQueryParameters2D
        {
            Shape = circle, Transform = new Transform2D(0f, from),
            CollisionMask = mask, Exclude = excluded, Margin = 0.01f,
        };
        var overlaps = space.IntersectShape(query, 32);
        if (overlaps.Count == 0)
        {
            query.Motion = motion;
            var fractions = space.CastMotion(query);
            if (fractions.Length < 2 || fractions[0] >= 1f) return false;
            position = from + motion * fractions[0];
            query.Transform = new Transform2D(0f, from + motion * fractions[1]);
            query.Motion = Vector2.Zero;
            query.Margin = 0.05f;
            overlaps = space.IntersectShape(query, 32);
        }
        else position = from;

        // A wall wins ties. If the solver cannot resolve a contact, stop conservatively.
        foreach (var item in overlaps)
        {
            var candidate = item["collider"].AsGodotObject() as Node2D;
            if (candidate == null) continue;
            if (candidate is not CollisionObject2D co || (co.CollisionLayer & CollisionLayers.World) != 0)
            { body = candidate; return true; }
            body ??= candidate;
        }
        return true;
    }
}
