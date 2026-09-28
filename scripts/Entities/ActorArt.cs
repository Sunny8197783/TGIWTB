using System.Collections.Generic;
using Godot;

namespace PixelMmo.Combat;

/// <summary>Shared pixel sprites anchored to their opaque feet, regardless of source canvas size.</summary>
public static class ActorArt
{
    private static readonly string[] Directions =
        { "south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west" };
    private sealed record Frame(Texture2D Texture, Rect2 Bounds);
    private static readonly Dictionary<string, Frame[]> Cache = new();
    private static readonly Dictionary<string, Frame[][]> Animations = new();

    public static (Texture2D Texture, Rect2 Bounds, float ReferenceHeight) FrameFor(string key, Vector2 facing, string clip = null, float? progress = null)
    {
        if (!Cache.TryGetValue(key, out var frames))
        {
            frames = new Frame[8];
            for (int i = 0; i < 8; i++)
            {
                string path = $"res://art/actors/{key}/idle/{Directions[i]}.png";
                if (!ResourceLoader.Exists(path)) continue;
                var texture = GD.Load<Texture2D>(path);
                using var image = texture.GetImage();
                frames[i] = new Frame(texture, image.GetUsedRect());
            }
            Cache[key] = frames;
        }
        int dir = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(facing.X, facing.Y) / (Mathf.Pi / 4f)), 8);
        Frame f = frames[dir] ?? frames[0];
        if (f == null || f.Bounds.Size.Y < 1) return default;
        float reference=(frames[0] ?? f).Bounds.Size.Y;
        if(clip != null)
        {
            string cacheKey=key+"/"+clip;
            if(!Animations.TryGetValue(cacheKey,out var animation))
            {
                animation=new Frame[8][];
                for(int i=0;i<8;i++)
                {
                    var list=new List<Frame>();
                    for(int n=0;n<24;n++)
                    {
                        string path=$"res://art/actors/{key}/{clip}/{Directions[i]}/frame_{n:D3}.png";
                        if(!ResourceLoader.Exists(path)) break;
                        var texture=GD.Load<Texture2D>(path); using var image=texture.GetImage();
                        list.Add(new(texture,image.GetUsedRect()));
                    }
                    animation[i]=list.ToArray();
                }
                Animations[cacheKey]=animation;
            }
            Frame[] sequence=animation[dir];
            if(sequence.Length>0)
            {
                int index=progress.HasValue ? Mathf.Clamp((int)(progress.Value*sequence.Length),0,sequence.Length-1)
                    : (int)(Time.GetTicksMsec()/90 % (ulong)sequence.Length);
                f=sequence[index];
            }
        }
        return (f.Texture, f.Bounds, reference);
    }

    public static bool Draw(Node2D canvas, string key, Vector2 facing, float height,
        float footY, Color tint, Vector2 squash)
    {
        var actor=canvas as MonsterBase;
        var f = FrameFor(key, facing, actor?.ArtClip, actor?.ArtProgress);
        if (f.Texture == null) return false;
        // One scale per actor, not per direction: turning cannot pump the sprite's size.
        float referenceHeight = f.ReferenceHeight;
        Vector2 size = f.Bounds.Size * (height / referenceHeight) * squash;
        var rect = new Rect2(new Vector2(-size.X * .5f, footY - size.Y), size);
        canvas.DrawTextureRectRegion(f.Texture, rect, f.Bounds, tint);
        return true;
    }
}
