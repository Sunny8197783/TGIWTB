using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

public static class SpriteContourChecks
{
    private static AtlasTexture Frame()
    {
        var frame=ActorArt.FrameFor("goblin",new Vector2(1,-1),"attack",5f/9f);
        return new AtlasTexture { Atlas=frame.Texture,Region=frame.Bounds };
    }
    public static bool ReproduceRaw()
    {
        using var texture=Frame();using var image=texture.GetImage();using var bitmap=new Bitmap();bitmap.CreateFromImageAlpha(image,.3f);
        bool failed=false;
        foreach(var raw in bitmap.OpaqueToPolygons(new Rect2I(Vector2I.Zero,image.GetSize()),1f))
            failed |= Geometry2D.DecomposePolygonInConvex(raw).Count==0;
        GD.Print($"[contour-repro] raw north-east attack frame_005 decomposition failed={failed} (intentional pre-fix path)");
        return failed;
    }
    public static void Verify(Action<bool,string> expect)
    {
        var texture=Frame();using var image=texture.GetImage();using var bitmap=new Bitmap();bitmap.CreateFromImageAlpha(image,.3f);
        var outlines=bitmap.OpaqueToPolygons(new Rect2I(Vector2I.Zero,image.GetSize()),1f);
        var cleaned=new List<Vector2[]>();int reversals=0,removed=0;
        foreach(var raw in outlines) {
            for(int i=0;i<raw.Length;i++) {
                Vector2 a=raw[i]-raw[(i+raw.Length-1)%raw.Length],b=raw[(i+1)%raw.Length]-raw[i];
                if(Mathf.IsZeroApprox(a.Cross(b)) && a.Dot(b)<0) reversals++;
            }
            var simple=SpriteCollision3D.CleanContour(raw);removed+=raw.Length-simple.Length;cleaned.Add(simple);
            expect(Mathf.IsEqualApprox(Area(raw),Area(simple)),"contour cleanup must preserve signed area");
        }
        expect(reversals>0 && removed>0,"regression asset must contain the original collinear reversal");
        var shapes=SpriteCollision3D.Shapes(texture,.04f,.2f);expect(shapes.Length>0,"regression asset must retain convex pieces");
        var projected=new List<Vector2[]>();
        foreach(var shape in shapes) {
            var vertices=shape.Points;var polygon=new Vector2[vertices.Length/2];
            for(int i=0;i<polygon.Length;i++) polygon[i]=new Vector2(Mathf.Round(vertices[i*2].X/.04f+image.GetWidth()*.5f),Mathf.Round(image.GetHeight()-vertices[i*2].Y/.04f));
            projected.Add(polygon);
        }
        int mismatch=0,samples=0;
        for(int y=0;y<image.GetHeight();y++) for(int x=0;x<image.GetWidth();x++) {
            var point=new Vector2(x+.5f,y+.5f);
            bool expected=cleaned.Exists(p=>Geometry2D.IsPointInPolygon(point,p)),actual=projected.Exists(p=>Geometry2D.IsPointInPolygon(point,p));
            if(expected!=actual) mismatch++;samples++;
        }
        expect(mismatch==0,"convex pieces must preserve concave silhouette coverage");
        expect(ReferenceEquals(shapes,SpriteCollision3D.Shapes(texture,.04f,.2f)),"collision cache must reuse shapes");
        GD.Print($"[contour-check] reversals={reversals} removed={removed} pieces={shapes.Length} coverage={samples} mismatches={mismatch}");
    }
    private static float Area(Vector2[] polygon) { float area=0;for(int i=0;i<polygon.Length;i++)area+=polygon[i].Cross(polygon[(i+1)%polygon.Length]);return area*.5f; }
}
