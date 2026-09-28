using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

public static class CustomizationChecks
{
    public static async Task Run(Node host,IPlayerContext player,Action<bool,string> expect)
    {
        expect(CharacterAppearance.HairColors.Length>=30 && CharacterAppearance.SkinColors.Length>=10,"customization palette counts");
        foreach(bool female in new[]{false,true}) {
            var look=CharacterAppearance.Default();look.Female=female;look.UsePixelLab=false;
            var hair=new HashSet<string>();var eyes=new HashSet<string>();var body=new HashSet<string>();
            for(int i=0;i<CharacterAppearance.StyleCount;i++) {look.HairStyle=i;hair.Add(Hash(look));}
            look.HairStyle=0;
            for(int i=0;i<CharacterAppearance.EyeCount;i++) {look.EyeShape=i;eyes.Add(Hash(look));}
            for(int i=0;i<CharacterAppearance.BodyCount;i++) {look.Body=i;body.Add(Hash(look));}
            expect(hair.Count>=50 && eyes.Count>=50 && body.Count>=5,$"distinct rendered options female={female}: {hair.Count}/{eyes.Count}/{body.Count}");
            GD.Print($"[customization-check] female={female} unique hair={hair.Count} eyes={eyes.Count} body={body.Count}");
            look.UsePixelLab=true;body.Clear();
            for(int i=0;i<6;i++) {look.Body=i;body.Add(Hash(look));}
            expect(body.Count==6,$"PixelLab body proportions female={female}");
            foreach(string clip in new[]{"idle","hero_walk","hero_run","hero_slash","hero_heavy"}) for(int direction=0;direction<8;direction++) {
                expect(PixelHeroArt.Source(female,direction,clip).Length==PixelHeroArt.Count(clip),$"PixelLab frames female={female} clip={clip} dir={direction}");
                if(clip is "hero_slash" or "hero_heavy") {
                    int contact=PixelHeroArt.ContactFrame(female,direction,clip);
                    var tint=look.Copy();tint.Hair=CharacterAppearance.HairColors[0];string dark=Hash(tint,direction,clip,contact);
                    tint.Hair=CharacterAppearance.HairColors[8];
                    expect(Hash(tint,direction,clip,contact)!=dark,$"attack hair tint female={female} clip={clip} dir={direction}");
                }
            }
            foreach(string motion in new[]{"walk","run"})
            {
                using var directions=Image.CreateEmpty(8*128,8*128,false,Image.Format.Rgba8);
                directions.Fill(new Color("25322d"));
                for(int direction=0;direction<8;direction++)
                {
                    var poses=new HashSet<string>();
                    for(int frame=0;frame<8;frame++)
                    {
                        using var pose=CharacterArt.Render(look,direction,motion,frame);
                        var bounds=pose.GetUsedRect();
                        expect(bounds.Position.X>0 && bounds.Position.Y>0 && bounds.End.X<128 && bounds.End.Y<128,
                            $"movement must not clip female={female} {motion}/{direction}/{frame}");
                        poses.Add(System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pose.GetData())));
                        directions.BlendRect(pose,new Rect2I(0,0,128,128),new Vector2I(frame*128,direction*128));
                    }
                    expect(poses.Count>=6,$"movement requires distinct poses female={female} {motion}/{direction}");
                }
                directions.SavePng($"res://docs/review/{(female?"female":"male")}-{motion}-runtime.png");
            }
            var colors=new HashSet<string>();
            foreach(var color in CharacterAppearance.HairColors) {look.Hair=color;colors.Add(Hash(look));}
            expect(colors.Count==30,$"PixelLab hair palette female={female}");colors.Clear();
            foreach(var color in CharacterAppearance.SkinColors) {look.Skin=color;colors.Add(Hash(look));}
            expect(colors.Count==12,$"PixelLab skin palette female={female}");
            using var sheet=Image.CreateEmpty(11*CharacterArt.Canvas,4*CharacterArt.Canvas,false,Image.Format.Rgba8);
            sheet.Fill(new Color("25322d"));look=CharacterAppearance.Default();look.Female=female;
            string[] clips={"hero_walk","hero_run","hero_slash","hero_heavy"};
            for(int row=0;row<clips.Length;row++) for(int frame=0;frame<PixelHeroArt.Count(clips[row]);frame++) {
                using var pose=CharacterArt.Render(look,1,clips[row],frame);
                sheet.BlendRect(pose,new Rect2I(0,0,128,128),new Vector2I(frame*128,row*128));
            }
            sheet.SavePng($"res://docs/review/hero-{(female?"female":"male")}-motions.png");
        }
        var invalid=CharacterAppearance.FromSave(new SaveAppearance { HairStyle=int.MaxValue,EyeShape=-4,Body=99,Hair="invalid",Skin="zzzzzz" });
        expect(invalid.HairStyle==59 && invalid.EyeShape==0 && invalid.Body==5 && invalid.Hair==CharacterAppearance.Default().Hair,"invalid appearance values must be sanitized");
        expect(!CharacterAppearance.FromSave(null).Confirmed,"old saves must retain first-time creation eligibility");
        var before=player.CaptureSave();bool wasPaused=host.GetTree().Paused;
        try {
            var fieldSprite=((Node)player).GetNode<PlayerSprite>("PlayerSprite");
            foreach(bool female in new[]{false,true}) {
                var appearance=CharacterAppearance.Default();appearance.Female=female;
                player.ApplyAppearance(appearance);appearance=player.Appearance;
                int builds=PlayerSprite.ImageBuildCount;
                for(int direction=0;direction<8;direction++) {
                    float angle=direction*Mathf.Pi/4f;Vector2 facing=new(Mathf.Sin(angle),Mathf.Cos(angle));
                    fieldSprite.UpdateFrame(facing,"breathe",null,0f,appearance);
                    var idle=fieldSprite.Texture;
                    fieldSprite.UpdateFrame(facing,"walk",null,.385f,appearance); // middle of frame 3
                    fieldSprite.UpdateFrame(facing,"run",null,0f,appearance);
                    var actual=fieldSprite.Texture;
                    fieldSprite.UpdateFrame(facing,"run",3.5f/8f,0f,appearance);
                    expect(actual==fieldSprite.Texture,
                        $"walk/run must preserve stride phase female={female} dir={direction}");
                    var sprint=fieldSprite.Texture;
                    fieldSprite.UpdateFrame(facing,"dash",3.5f/8f,0f,appearance);
                    expect(fieldSprite.Texture==sprint,"dash reuses prepared sprint texture");
                    fieldSprite.UpdateFrame(facing,"guard",null,0f,appearance);
                    expect(fieldSprite.Texture==idle,"guard reuses prepared armed idle texture");
                    foreach(string clip in new[]{"hero_slash","hero_heavy"})
                        fieldSprite.UpdateFrame(facing,clip,CombatMotion.Progress(clip,SkillPhase.Active,0f,PixelHeroArt.ContactFrame(female,direction,clip)),0f,appearance);
                }
                expect(PlayerSprite.ImageBuildCount==builds,$"all movement/contact textures prepared before play female={female}");
            }
            CharacterCreator.Open(host,player);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            var creator=CharacterCreator.Instance;
            expect(creator!=null && host.GetTree().Paused && player.InputBlocked,"creator must pause world and block combat");
            creator.SetArtMode(false);creator.SetFemale(true);creator.ShowCategory(0);creator.Select(59);creator.ShowCategory(1);creator.Select(58);creator.ShowCategory(2);creator.Select(5);
            creator.ShowCategory(3);creator.Select(29);creator.Select(41);
            expect(creator.Apply(),"creator apply must complete without writing test saves");
            expect(!host.GetTree().Paused && !CharacterCreator.IsOpen,"creator apply must resume world");
            var chosen=player.Appearance;
            expect(chosen.Confirmed && chosen.Female && chosen.HairStyle==59 && chosen.EyeShape==58 && chosen.Body==5,"chosen appearance must reach player");
            var save=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(player.CaptureSave()));player.RestoreSave(save);
            expect(player.Appearance.ToSave().Hair==chosen.ToSave().Hair && player.Appearance.EyeShape==58 && player.Appearance.Female,"appearance save round trip");
            CharacterCreator.Open(host,player);CharacterCreator.Instance.SetFemale(false);CharacterCreator.Instance.Cancel();
            expect(player.Appearance.Female,"cancel must preserve original appearance");
            CharacterCreator.Open(host,player);creator=CharacterCreator.Instance;creator.SetArtMode(true);creator.ShowCategory(0);creator.Select(0);
            expect(creator.Draft.HairStyle==59,"PixelLab mode preserves legacy part selection");creator.Apply();
            save=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(player.CaptureSave()));player.RestoreSave(save);
            expect(player.Appearance.UsePixelLab,"PixelLab mode save round trip");
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.PhysicsFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.PhysicsFrame);
            var sprite=((Node)player).GetNode<PlayerSprite>("PlayerSprite");
            expect(sprite.Texture.GetWidth()==CharacterArt.Canvas && Mathf.IsEqualApprox(sprite.Scale.X,CharacterArt.Scale),"field sprite must render saved appearance");
        }
        finally {
            CharacterCreator.Instance?.Cancel();host.GetTree().Paused=wasPaused;player.RestoreSave(before);
        }
    }
    private static string Hash(CharacterAppearance look,int direction=0,string clip="idle",int frame=0)
    {
        using var image=CharacterArt.Render(look,direction,clip,frame,false);
        return Convert.ToHexString(SHA256.HashData(image.GetData()));
    }
}
