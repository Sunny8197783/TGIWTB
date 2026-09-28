using Godot;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>Layered pixel figure: one renderer for all eight directions, previews and combat poses.</summary>
public static class CharacterArt
{
    public const int Canvas = 128;
    public const float Scale = .38f;
    private static readonly Color Outline = new("25242b"), Leather = new("664a39"), Brass = new("cfaa66");

    public static Image Render(CharacterAppearance look, int direction=0, string clip="idle", int frame=0, bool weapon=true)
    {
        if(look.UsePixelLab && PixelHeroArt.Available(look.Female)) return PixelHeroArt.Render(look,direction,clip,frame);
        var p=new Pixels();
        bool back=direction is 3 or 4 or 5, side=direction is 2 or 6;
        int turn=direction==0 || direction==4 ? 0 : direction<4 ? 1 : -1;
        int shortness=look.Body==1 ? 7 : look.Body==2 ? -5 : look.Body==4 ? 4 : 0;
        int width=look.Body switch { 2=>10,3=>17,4=>16,5=>19,_=>13 };
        if(look.Female) width-=2;
        if(side) width=(int)(width*.72f);
        else if(direction is 1 or 3 or 5 or 7) width=(int)(width*.85f);
        float phase=frame*Mathf.Tau/8;
        bool walking=clip is "walk" or "run" or "dash" or "sword_walk";
        bool attacking=clip is "sword" or "sword_heavy";
        int step=walking ? Mathf.RoundToInt(Mathf.Sin(phase)*(clip=="run" ? 6:4)) : 0;
        int bob=walking ? Mathf.Abs(step)/3 : 0;
        int cy=43+shortness+bob, torso=cy+19, hip=79+shortness/3;
        int cx=64+(attacking ? Mathf.RoundToInt(Mathf.Sin(frame/8f*Mathf.Pi)*3)*turn : 0);
        Color skin=look.Skin, hair=look.Hair, cloth=look.Shirt;
        Hair(p,look,cx,cy,turn,back,side,false);
        // Boots and articulated legs stay planted at the same origin for every body preset.
        for(int leg=-1;leg<=1;leg+=2)
        {
            int x=cx+leg*(side?4:7), stride=step*leg;
            p.Line(x,hip,x+stride,93-Mathf.Max(0,stride),11,Outline);
            p.Line(x,hip,x+stride,91-Mathf.Max(0,stride),8,look.Pants);
            p.Box(x+stride-6,91-Mathf.Max(0,stride),12,9,Outline);
            p.Box(x+stride-5,92-Mathf.Max(0,stride),10,6,Leather);
            p.Box(x+stride-4,93-Mathf.Max(0,stride),3,3,Leather.Lightened(.22f));
        }
        p.Ellipse(cx,torso+8,width+2,16,Outline);
        p.Ellipse(cx,torso+7,width,15,cloth.Darkened(.20f));
        p.Ellipse(cx-3,torso+5,width-3,13,cloth);
        p.Box(cx-width+2,hip-3,width*2-4,5,Leather);
        p.Box(cx-2,hip-3,5,4,Brass);
        if(!back) { p.Line(cx-7,torso-4,cx+4,hip-3,3,Leather); p.Box(cx+3,hip-2,7,9,Leather); }
        else { p.Box(cx-width+4-turn*3,torso,Mathf.Max(6,width*2-8),18,new Color("59634d")); p.Box(cx-width+5-turn*3,torso,Mathf.Max(4,width*2-10),3,Brass.Darkened(.35f)); }
        int contact=clip=="sword_heavy"?5:4;
        float facingAngle=Mathf.Pi*.5f-direction*Mathf.Pi*.25f;
        float swing=attacking ? facingAngle+(frame<=contact?-1.6f+1.6f*frame/contact:(frame-contact)*.45f) : .7f;
        Vector2 hand=new(cx+width+7,torso+15+step);
        if(attacking) hand=new Vector2(cx,torso)+Vector2.FromAngle(swing)*23;
        if(clip is "guard" or "block") hand=new(cx+4,torso+2);
        for(int arm=-1;arm<=1;arm+=2)
        {
            Vector2 start=new(cx+arm*(width-2),torso);
            Vector2 end=arm==1 ? hand : new Vector2(cx-width-4,torso+15-step);
            p.Line((int)start.X,(int)start.Y,(int)end.X,(int)end.Y,9,Outline);
            p.Line((int)start.X,(int)start.Y,(int)end.X,(int)end.Y-3,6,cloth);
            p.Ellipse((int)end.X,(int)end.Y,4,5,skin.Darkened(.1f));
            p.Box((int)end.X-2,(int)end.Y-2,3,3,skin.Lightened(.12f));
        }
        p.Box(cx-4,cy+11,8,9,skin.Darkened(.20f));
        int faceWidth=side?11:look.Female?15:16;
        p.Ellipse(cx,cy,faceWidth+1,16,Outline);
        p.Ellipse(cx,cy,faceWidth,15,skin.Darkened(.19f));
        p.Ellipse(cx-2+turn*2,cy-2,faceWidth-2,13,skin);
        p.Ellipse(cx-5,cy-6,4,4,skin.Lightened(.11f));
        p.Ellipse(cx-faceWidth,cy+2,3,5,skin.Darkened(.1f));
        p.Ellipse(cx+faceWidth,cy+2,3,5,skin);
        if(!back)
        {
            int eyeWidth=5+look.EyeShape%6, kind=look.EyeShape/6;
            if(!side) Eye(p,cx-12+turn*2,cy,eyeWidth,kind,look.Female,false);
            Eye(p,cx+(side?1:3)+turn*2,cy,eyeWidth,kind,look.Female,true);
            p.Line(cx+turn*6,cy+5,cx+turn*6+2,cy+7,1,skin.Darkened(.3f));
            p.Line(cx-2+turn*5,cy+11,cx+2+turn*5,cy+11,1,new Color("995e52").Lerp(skin,.35f));
        }
        Hair(p,look,cx,cy,turn,back,side,true);
        if(weapon)
        {
            float angle=attacking?swing:clip is "guard" or "block" ? -1.55f:1.1f;
            Vector2 blade=Vector2.FromAngle(angle), cross=new(-blade.Y,blade.X), tip=hand+blade*29;
            p.Line((int)hand.X,(int)hand.Y,(int)tip.X,(int)tip.Y,5,Outline);
            p.Line((int)(hand.X+blade.X*5),(int)(hand.Y+blade.Y*5),(int)tip.X,(int)tip.Y,3,new Color("91aeb6"));
            p.Line((int)(hand.X+blade.X*5-1),(int)(hand.Y+blade.Y*5),(int)tip.X-1,(int)tip.Y,1,new Color("e5ece6"));
            Vector2 guard=hand+blade*5;
            p.Line((int)(guard.X-cross.X*6),(int)(guard.Y-cross.Y*6),(int)(guard.X+cross.X*6),(int)(guard.Y+cross.Y*6),3,Brass);
        }
        return p.Image;
    }

    private static void Eye(Pixels p,int x,int y,int width,int kind,bool female,bool right)
    {
        int[][] profiles={new[]{2,1,0,0,1,2},new[]{2,1,1,1,1,2},new[]{0,0,0,0,0,0},new[]{1,1,1,1,1,1},new[]{0,0,1,2,2,3},
            new[]{2,1,1,0,0,1},new[]{3,2,1,0,0,0},new[]{0,0,1,1,2,3},new[]{1,0,0,0,0,1},new[]{2,1,0,0,1,2}};
        for(int col=0;col<width;col++)
        {
            int u=right?width-1-col:col, top=profiles[kind][Mathf.Min(5,u*6/width)];
            int bottom=kind==9?top:kind==3?top+2:kind==8?6:5-(kind is 0 or 1 or 5?top/2:0);
            p.Box(x+col,y+top,1,bottom-top+1,Outline);
            if(bottom-top>1) p.Box(x+col,y+top+1,1,bottom-top-1,new Color("f0e9d8"));
        }
        if(kind!=9) {
            int top=profiles[kind][right?(width-1-width/2)*6/width:width/2*6/width];
            int irisY=y+top+1, irisHeight=kind==3?1:Mathf.Max(1,4-top);
            p.Box(x+width/2,irisY,2,irisHeight,new Color("4c665d"));p.Box(x+width/2,irisY,1,Mathf.Min(2,irisHeight),Outline);
            p.Box(x+width/2+1,irisY,1,1,Colors.White);
        }
        p.Line(x,y-3,x+width-1,y-3+(kind is 4 or 6?2:0),1,Outline.Lightened(.18f));
        if(female) p.Line(right?x+width-2:x+1,y+1,right?x+width+1:x-2,y-1,1,Outline);
    }

    private static void Hair(Pixels p,CharacterAppearance look,int x,int y,int turn,bool back,bool side,bool front)
    {
        int family=look.HairStyle/5, fringe=look.HairStyle%5, w=side?13:18;
        int length=look.Female?8:0;
        Color dark=look.Hair.Darkened(.32f), mid=look.Hair, light=look.Hair.Lightened(.23f);
        if(!front)
        {
            if(family is 1 or 2 or 3 or 4 or 9) {
                int h=family==1?17:family==2?20:family==9?22:30;
                p.Ellipse(x,y+9,w+1,h/2+length/2,Outline); p.Ellipse(x,y+8,w,h/2+length/2,dark);
                for(int strand=-w+3;strand<w;strand+=5) p.Line(x+strand,y+2,x+strand+(family is 2 or 4?3:0),y+h/2+length/2,2,mid);
            }
            if(family is 5 or 6 or 7 or 11) {
                for(int sign=-1;sign<=1;sign+=2) {
                    if(family is 5 or 11 && sign==-1) continue;
                    int px=x+sign*(w-1)+turn*4, end=family==6?y+24+length:y+32+length;
                    p.Line(px,y-5,px+sign*5,end,9,Outline); p.Line(px,y-6,px+sign*5,end-2,7,mid);
                    p.Box(px-4,y-3,8,3,Brass);
                    if(family is 7 or 11) for(int by=y+4;by<end;by+=5) p.Line(px-2,by,px+5,by+3,2,dark);
                }
            }
            return;
        }
        int crown=family==0?8:family is 2 or 4 or 10?12:10;
        p.Ellipse(x,y-12,w+1,crown+1,Outline); p.Ellipse(x,y-13,w,crown,mid);
        p.Line(x-w+5,y-17,x-4,y-20,2,light);
        p.Line(x-4,y-20,x+3,y-20,2,light);
        p.Line(x+6,y-19,x+w-5,y-17,1,light);
        if(family==8) { p.Ellipse(x+turn*7,y-23,9,8,Outline); p.Ellipse(x+turn*7-1,y-24,7,6,mid); p.Box(x-7+turn*7,y-19,14,3,Brass); }
        if(family==10) for(int a=0;a<9;a++) { float angle=a*Mathf.Pi/8; int xx=x+(int)(Mathf.Cos(angle)*w), yy=y-9-(int)(Mathf.Sin(angle)*12); p.Ellipse(xx,yy,5,5,Outline);p.Ellipse(xx,yy-1,4,4,mid);p.Box(xx-2,yy-3,2,2,light); }
        if(back) { p.Ellipse(x,y,w,13,dark); p.Ellipse(x-3,y-3,w-3,11,mid); for(int i=-w+4;i<w;i+=5) p.Line(x+i,y-10,x+i,y+7,1,light); return; }
        // Five authored fringe cuts change the silhouette, independently of the rear hairstyle.
        for(int i=-w+1;i<w;i++) {
            int bottom=fringe switch {0=>-3+(i%5==0?3:0),1=>-9+Mathf.Abs(i)/2,2=>-9+(i+w)/2,3=>-9+(Mathf.Abs(i)%4==0?2:0),_=>-7+(Mathf.Abs(i)%9)/2};
            if(fringe==1 && Mathf.Abs(i)<3) continue;
            p.Line(x+i,y-11,x+i,y+bottom,1,(i%9==0)?dark:mid);
        }
        p.Line(x-w,y-7,x-w+1,y+7+(family==0?0:4),3,dark);
        p.Line(x+w,y-7,x+w-1,y+7+(family==0?0:4),3,mid);
    }

    private sealed class Pixels
    {
        public readonly Image Image=Image.CreateEmpty(Canvas,Canvas,false,Image.Format.Rgba8);
        public void Box(int x,int y,int w,int h,Color color) { var rect=new Rect2I(x,y,w,h).Intersection(new Rect2I(0,0,Canvas,Canvas)); if(rect.HasArea()) Image.FillRect(rect,color); }
        public void Ellipse(int x,int y,int rx,int ry,Color color) {
            for(int row=-ry;row<=ry;row++) { int half=(int)(rx*Mathf.Sqrt(Mathf.Max(0,1-row*row/(float)(ry*ry)))); Box(x-half,y+row,half*2+1,1,color); }
        }
        public void Line(int x,int y,int endX,int endY,int width,Color color) {
            int dx=Mathf.Abs(endX-x),sx=x<endX?1:-1,dy=-Mathf.Abs(endY-y),sy=y<endY?1:-1,error=dx+dy;
            while(true) { Box(x-width/2,y-width/2,width,width,color); if(x==endX && y==endY) break; int e=error*2; if(e>=dy){error+=dy;x+=sx;} if(e<=dx){error+=dx;y+=sy;} }
        }
    }
}
