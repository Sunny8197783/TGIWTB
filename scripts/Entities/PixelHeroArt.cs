using System.Collections.Generic;
using Godot;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>PixelLab source frames; color and proportion edits preserve the original pixel shading.</summary>
public static class PixelHeroArt
{
    private static readonly string[] Directions={"south","south-east","east","north-east","north","north-west","west","south-west"};
    private static readonly Dictionary<(bool,string,int),Image[]> Frames=new();
    private static readonly Dictionary<(bool,int),(float X,float Foot,float Top,float Height)> Anchors=new();
    private static readonly Dictionary<Image,Vector2> HairCenters=new();
    private static readonly Dictionary<Image,byte[]> Pixels=new();
    // First visible strike, reviewed in the source sheets (south through south-west, zero-based).
    private static readonly int[] MaleSlash={5,8,7,6,6,8,7,7}, MaleHeavy={6,7,7,6,8,8,8,7};
    private static readonly int[] FemaleSlash={5,3,8,7,7,8,7,7}, FemaleHeavy={7,7,7,7,7,8,7,6};
    public static int ContactFrame(bool female,int direction,string clip) =>
        (Clip(clip)=="hero_heavy_fixed" ? female?FemaleHeavy:MaleHeavy : female?FemaleSlash:MaleSlash)[direction];
    public static bool Available(bool female) => ResourceLoader.Exists($"res://art/heroes/{(female?"female":"male")}/idle/south.png");
    public static string Clip(string clip) => clip switch {
        "walk" or "sword_walk" or "hero_walk" or "hero_walk_fixed"=>"hero_stride",
        "run" or "dash" or "hero_run"=>"hero_sprint",
        "sword" or "hero_slash"=>"hero_slash_fixed",
        "sword_heavy" or "hero_heavy"=>"hero_heavy_fixed",
        "hero_slash_fixed" or "hero_heavy_fixed" or "hero_stride" or "hero_sprint"=>clip,_=>"idle" };
    public static int Count(string clip) => Clip(clip) is "hero_slash_fixed" or "hero_heavy_fixed"?9:Clip(clip)=="idle"?1:8;
    public static Image[] Source(bool female,int direction,string clip)
    {
        clip=Clip(clip);var key=(female,clip,direction);
        if(Frames.TryGetValue(key,out var cached)) return cached;
        string root=$"res://art/heroes/{(female?"female":"male")}/{clip}/{Directions[direction]}";
        var frames=new List<Image>();
        if(clip=="idle") frames.Add(GD.Load<Texture2D>(root+".png").GetImage());
        else for(int i=0;i<16;i++) { string path=$"{root}/frame_{i:D3}.png";if(!ResourceLoader.Exists(path)) break;frames.Add(GD.Load<Texture2D>(path).GetImage()); }
        return Frames[key]=frames.Count>0?frames.ToArray():Source(female,direction,"idle");
    }
    public static Image Render(CharacterAppearance look,int direction,string clip,int frame)
    {
        var idle=Source(look.Female,direction,"idle")[0];
        if(!Anchors.TryGetValue((look.Female,direction),out var anchor))
        {
            var used=idle.GetUsedRect();float sum=0;int count=0;
            for(int y=used.Position.Y;y<used.Position.Y+Mathf.Max(4,used.Size.Y/5);y++) for(int x=0;x<idle.GetWidth();x++)
                if(idle.GetPixel(x,y).A>.3f) {sum+=x;count++;}
            float center=count>0?sum/count:idle.GetWidth()*.5f;int foot=used.End.Y;
            for(int y=idle.GetHeight()-1;y>used.Position.Y;y--) {
                bool found=false;for(int x=Mathf.Max(0,(int)center-14);x<Mathf.Min(idle.GetWidth(),(int)center+15);x++) if(idle.GetPixel(x,y).A>.3f) {found=true;break;}
                if(found) {foot=y+1;break;}
            }
            anchor=(center,foot,used.Position.Y,Mathf.Max(1,foot-used.Position.Y));Anchors[(look.Female,direction)]=anchor;
        }
        var source=Source(look.Female,direction,clip);var image=source[Mathf.Clamp(frame,0,source.Length-1)];
        int width=image.GetWidth(), height=image.GetHeight();
        if(!Pixels.TryGetValue(image,out var pixels)) {
            using var rgba=(Image)image.Duplicate();rgba.Convert(Image.Format.Rgba8);
            Pixels[image]=pixels=rgba.GetData();
        }
        float bodyWidth=look.Body switch {1=>.90f,2=>.91f,3=>1.16f,4=>1.14f,5=>1.25f,_=>1};
        float bodyHeight=look.Body switch {1=>.90f,2=>1.07f,3=>1.02f,4=>.95f,_=>1};
        float scale=76f/anchor.Height;
        float shiftX=(image.GetWidth()-idle.GetWidth())*.5f,shiftY=(image.GetHeight()-idle.GetHeight())*.5f;
        var hairCenter=HairCenter(image,anchor.X+shiftX,anchor.Top+shiftY,anchor.Height);
        bool sprint=Clip(clip)=="hero_sprint";
        float leanX=sprint?Mathf.Sin(direction*Mathf.Pi/4f)*.38f:0f;
        float leanY=sprint?Mathf.Cos(direction*Mathf.Pi/4f)*.16f:0f;
        var output=Image.CreateEmpty(CharacterArt.Canvas,CharacterArt.Canvas,false,Image.Format.Rgba8);
        for(int y=0;y<CharacterArt.Canvas;y++) for(int x=0;x<CharacterArt.Canvas;x++)
        {
            // Bend only above the waist: the stride and foot contact stay at the authored ground anchor.
            float poseY=y<66f ? 66f+(y-66f)/(1f-leanY) : y;
            float poseX=x-leanX*Mathf.Max(0f,66f-poseY);
            int sx=Mathf.RoundToInt((poseX-64)/(scale*bodyWidth)+anchor.X+shiftX);
            int sy=Mathf.RoundToInt((poseY-100)/(scale*bodyHeight)+anchor.Foot+shiftY);
            if(sx<0 || sy<0 || sx>=width || sy>=height) continue;
            int offset=(sy*width+sx)*4;
            Color color=new(pixels[offset]/255f,pixels[offset+1]/255f,pixels[offset+2]/255f,pixels[offset+3]/255f);
            if(color.A<.01f)continue;
            // Generated cloth hue must not jump from olive to neon green between movement frames.
            if(color.G>color.R*1.12f && color.G>color.B*1.18f)
                color=new Color(color.V*.70f,color.V*.88f,color.V*.44f,color.A);
            float relativeY=(sy-shiftY-anchor.Top)/anchor.Height;
            bool warm=color.R>color.G*1.20f && color.G>color.B*1.10f;
            // ponytail: masks target these auburn-haired outfits; export part masks before adding other outfits.
            bool hairWarm=color.R>color.G*1.20f && color.R>color.B*1.15f;
            if(hairWarm && Mathf.Abs(sy-hairCenter.Y)<anchor.Height*.14f && (color.G<.45f || color.B<.24f) && Mathf.Abs(sx-hairCenter.X)<anchor.Height*.22f)
                color=Shade(look.Hair,Mathf.Clamp(color.V/.55f,.24f,1.45f),color.A);
            else if(warm && color.R>.65f && color.G>.40f && color.B>.20f &&
                ((sy>hairCenter.Y && sy<hairCenter.Y+anchor.Height*.24f && Mathf.Abs(sx-hairCenter.X)<anchor.Height*.22f) ||
                (relativeY>.25f && relativeY<.90f && Mathf.Abs(sx-shiftX-anchor.X)>anchor.Height*.18f)))
                color=Shade(look.Skin,Mathf.Clamp(color.V/.95f,.36f,1.10f),color.A);
            output.SetPixel(x,y,color);
        }
        return output;
    }
    private static Vector2 HairCenter(Image image,float x,float top,float height)
    {
        if(HairCenters.TryGetValue(image,out var cached)) return cached;
        int first=Mathf.Max(0,(int)top-8),last=Mathf.Min(image.GetHeight(),(int)(top+height*.5f));
        int span=Mathf.Max(6,(int)(height*.16f));float best=0;Vector2 center=new(x,top+height*.1f);
        var rows=new Vector3[last-first];
        for(int y=first;y<last;y++) for(int px=Mathf.Max(0,(int)(x-height*.35f));px<Mathf.Min(image.GetWidth(),(int)(x+height*.35f));px++) {
            var c=image.GetPixel(px,y);
            if(c.A>.3f && c.R>.48f && c.R>c.G*1.35f && c.R>c.B*2.4f) rows[y-first]+=new Vector3(px,y,1);
        }
        // Track the dense auburn hair cluster so a deep lunge cannot switch back to the source hair color.
        for(int start=0;start<rows.Length;start++) {
            Vector3 sum=Vector3.Zero;for(int row=start;row<Mathf.Min(rows.Length,start+span);row++) sum+=rows[row];
            if(sum.Z>best) {best=sum.Z;center=new Vector2(sum.X/sum.Z,sum.Y/sum.Z);}
        }
        HairCenters[image]=center;return center;
    }
    private static Color Shade(Color target,float shade,float alpha) => new(Mathf.Min(1,target.R*shade),Mathf.Min(1,target.G*shade),Mathf.Min(1,target.B*shade),alpha);
}
