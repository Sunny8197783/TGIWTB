using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>Opaque image contours shared by static scenery and animated hurt areas.</summary>
public static class SpriteCollision3D
{
    private static readonly Dictionary<(Texture2D, float, float), ConvexPolygonShape3D[]> Cache = new();

    public static ConvexPolygonShape3D[] Shapes(Texture2D texture, float pixelSize, float depth)
    {
        var key = (texture, pixelSize, depth);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        using var image = texture.GetImage();
        using var bitmap = new Bitmap(); bitmap.CreateFromImageAlpha(image, .3f);
        var shapes = new List<ConvexPolygonShape3D>();
        // ponytail: pixel outlines simplified by 1 px; use authored 3D meshes if the camera gains free rotation.
        foreach (var outline in bitmap.OpaqueToPolygons(new Rect2I(Vector2I.Zero, image.GetSize()), 1f))
        {
            var contour = CleanContour(outline);
            if (contour.Length < 3) continue;
            foreach (var polygon in Geometry2D.DecomposePolygonInConvex(contour))
            {
                if (polygon.Length < 3) continue;
                var points = new Vector3[polygon.Length * 2];
                for (int i = 0; i < polygon.Length; i++)
                {
                    float x = (polygon[i].X - image.GetWidth() * .5f) * pixelSize;
                    float y = (image.GetHeight() - polygon[i].Y) * pixelSize;
                    points[i * 2] = new Vector3(x, y, -depth * .5f);
                    points[i * 2 + 1] = new Vector3(x, y, depth * .5f);
                }
                shapes.Add(new ConvexPolygonShape3D { Points = points });
            }
        }
        return Cache[key] = shapes.ToArray();
    }

    // Bitmap's 1px simplification can reverse a thin spike onto its own edge.
    // Removing zero-area collinear turns preserves the contour area and its concavities.
    internal static Vector2[] CleanContour(Vector2[] outline)
    {
        var contour=new List<Vector2>(outline);
        bool changed;
        do {
            changed=false;
            for(int i=contour.Count-1;i>=0 && contour.Count>=3;i--) {
                Vector2 a=contour[i]-contour[(i+contour.Count-1)%contour.Count];
                Vector2 b=contour[(i+1)%contour.Count]-contour[i];
                if(!Mathf.IsZeroApprox(a.Cross(b))) continue;
                contour.RemoveAt(i);changed=true;
            }
        } while(changed && contour.Count>=3);
        return contour.ToArray();
    }

    public static void Set(CollisionObject3D body, Texture2D texture, float pixelSize, float depth, bool flip = false)
    {
        if (!body.IsInGroup("art_collision")) body.AddToGroup("art_collision");
        var shapes = Shapes(texture, pixelSize, depth);
        while (body.GetChildCount() < shapes.Length) body.AddChild(new CollisionShape3D());
        for (int i = 0; i < body.GetChildCount(); i++)
        {
            var child = (CollisionShape3D)body.GetChild(i);
            child.Disabled = i >= shapes.Length;
            if (i < shapes.Length)
            {
                child.Shape = shapes[i];
                child.Rotation = flip ? new Vector3(0f, Mathf.Pi, 0f) : Vector3.Zero;
                if (child.GetNodeOrNull<MeshInstance3D>("Outline") is { } outline) outline.Mesh = child.Shape.GetDebugMesh();
            }
        }
        if (DebugFlags.ShowHitbox) ShowDebug(body, true);
    }

    public static void ShowDebug(CollisionObject3D body, bool visible)
    {
        visible &= body.CollisionLayer != 0;
        if (visible && ReferenceWorld3D.Instance is { } view)
            visible = body.GlobalPosition.DistanceSquaredTo(ReferenceTerrain3D.Ground(((IPlayerContext)view.World.Player).WorldPosition)) < 24f * 24f;
        if (!visible && !body.HasMeta("debug_outline")) return;
        if (visible) body.SetMeta("debug_outline", true);
        foreach (CollisionShape3D shape in body.GetChildren())
        {
            var mesh = shape.GetNodeOrNull<MeshInstance3D>("Outline");
            if (mesh == null && visible)
            {
                mesh = new MeshInstance3D { Name = "Outline", Mesh = shape.Shape.GetDebugMesh(),
                    MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = body is Area3D ? new Color(1f, .3f, .2f, .25f) : new Color(.2f, 1f, .7f, .2f) } };
                shape.AddChild(mesh);
            }
            if (mesh != null) mesh.Visible = visible && !shape.Disabled;
        }
    }
}
