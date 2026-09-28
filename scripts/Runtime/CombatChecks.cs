using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>Run with --combat-check. Uses the actual Godot physics space, no player save writes.</summary>
public partial class CombatChecks : Node2D
{
    private readonly List<string> _fail = new();
    private void Expect(bool condition, string message) { if (!condition) _fail.Add(message); }

    public override async void _Ready()
    {
        try
        {
            var target = Body(new Vector2(50f, 0f), CollisionLayers.Monster, new CircleShape2D { Radius = 7f });
            var wall = Body(new Vector2(100f, 0f), CollisionLayers.World, new RectangleShape2D { Size = new Vector2(1f, 50f) });
            var wallFirst = Body(new Vector2(50f, 100f), CollisionLayers.World, new RectangleShape2D { Size = new Vector2(1f, 50f) });
            Body(new Vector2(100f, 100f), CollisionLayers.Monster, new CircleShape2D { Radius = 7f });
            // Grazing contact must use the projectile radius, not just its center ray.
            var graze = Body(new Vector2(50f, 208f), CollisionLayers.Monster, new CircleShape2D { Radius = 7f });
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint mask = CollisionLayers.World | CollisionLayers.Monster;
            var exclude = new Godot.Collections.Array<Rid>();
            bool hit = CombatCollision.Sweep(this, Vector2.Zero, new Vector2(500f, 0f), 2.5f,
                mask, exclude, out var body, out var point);
            Expect(hit && body == target && point.X < 50f, "fast projectile must hit first target");
            exclude.Add(target.GetRid());
            hit = CombatCollision.Sweep(this, point, new Vector2(500f, 0f) - point, 2.5f,
                mask, exclude, out body, out point);
            Expect(hit && body == wall && point.X < 100f, "piercing projectile must stop at wall behind target");
            exclude.Clear();
            hit = CombatCollision.Sweep(this, new Vector2(0f, 100f), new Vector2(500f, 0f), 2.5f,
                mask, exclude, out body, out point);
            Expect(hit && body == wallFirst, "wall must block target behind it");
            hit = CombatCollision.Sweep(this, new Vector2(100f, 0f), new Vector2(500f, 0f), 2.5f,
                mask, exclude, out body, out point);
            Expect(hit && body == wall, "projectile born overlapping wall must stop");
            hit = CombatCollision.Sweep(this, new Vector2(0f, 200f), new Vector2(500f, 0f), 2.5f,
                mask, exclude, out body, out point);
            Expect(hit && body == graze, "radius grazing contact must register");
            Expect(!CombatCollision.ClearSight(this, new Vector2(0f, 100f), new Vector2(100f, 100f)), "melee through wall");
            Expect(CombatCollision.ClearSight(this, Vector2.Zero, new Vector2(50f, 0f)), "unblocked melee");
            Expect(!CombatCollision.Sweep(this, new Vector2(0f, 300f), new Vector2(500f, 0f), 2.5f,
                mask, exclude, out body, out point), "empty space must remain passable");
            var arrow = new Arrow { Position = new Vector2(0f, 100f) };
            arrow.Setup(Vector2.Right, 30000f, 1f, null);
            AddChild(arrow);
            arrow.SetPhysicsProcess(false);
            arrow._PhysicsProcess(1.0 / 60.0);
            Expect(arrow.IsQueuedForDeletion() && arrow.Position.X < 50f, "enemy arrow must stop at wall");
            var bolt = new SkillBolt { Position = new Vector2(0f, 100f) };
            bolt.Setup(Vector2.Right, new SkillProjectile { SpeedPx = 30000f, RadiusPx = 2.5f,
                LifeSeconds = 1f, Pierce = true }, 1f, null, null);
            AddChild(bolt);
            bolt.SetPhysicsProcess(false);
            bolt._PhysicsProcess(1.0 / 60.0);
            Expect(bolt.IsQueuedForDeletion() && bolt.Position.X < 50f, "player piercing bolt must stop at wall");
            Expect(Hitbox.InCone(Vector2.Zero, Vector2.Right, 90f, 28f, new Vector2(25f, 0f), 7f)
                && !Hitbox.InCone(Vector2.Zero, Vector2.Right, 90f, 28f, new Vector2(-25f, 0f), 7f),
                "contact attack must hit forward, not behind");
            foreach (var (clip, frames, contact) in new[] { ("hero_slash", 9, 5), ("hero_heavy", 9, 6), ("sword", 9, 4), ("sword_heavy", 9, 5), ("punch", 6, 3), ("heavy", 10, 6), ("shout", 6, 3) })
            {
                Expect((int)(CombatMotion.Progress(clip, SkillPhase.Windup, 1f) * frames) < contact, clip + " windup contact");
                Expect((int)(CombatMotion.Progress(clip, SkillPhase.Active, 0f) * frames) == contact, clip + " active contact");
            }
            foreach(bool female in new[]{false,true}) foreach(string clip in new[]{"hero_slash","hero_heavy"}) for(int direction=0;direction<8;direction++) {
                int contact=PixelHeroArt.ContactFrame(female,direction,clip);
                Expect((int)(CombatMotion.Progress(clip,SkillPhase.Windup,1f,contact)*9)<contact &&
                    (int)(CombatMotion.Progress(clip,SkillPhase.Active,0f,contact)*9)==contact,$"directional contact female={female} clip={clip} dir={direction}");
            }
            foreach (var definition in MeadowLayout.Data.Camps)
            {
                Expect(WorldLayout.Meadow.Contains(definition.Center), "encounter outside meadow");
                Expect(definition.Slimes + definition.Goblins > 0 && definition.Respawn > 0f, "empty/invalid encounter");
                Expect(definition.Continuous ? definition.RewardRatio == 0f
                    : definition.RewardRatio > 0f && definition.RewardRatio <= 1f, "invalid supply reward: " + definition.Id);
                if (definition.Continuous)
                    Expect((definition.Slimes > 0) != (definition.Goblins > 0), "continuous area must contain one species: " + definition.Id);
            }
            var tiles = new TileWorld();
            AddChild(tiles);
            if (tiles.Props != null) AddChild(tiles.Props);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (var camp in MeadowLayout.Data.Camps)
                Expect(tiles.IsWalkable(camp.Center) && tiles.IsWalkable(camp.Center + new Vector2(0f, 80f)),
                    "blocked camp or supply: " + camp.Id);
            foreach (var route in MeadowLayout.Data.Routes)
                for (int i = 1; i < route.Length; i++)
                {
                    var from = WorldLayout.TileCenter(route[i - 1][0], route[i - 1][1]);
                    var to = WorldLayout.TileCenter(route[i][0], route[i][1]);
                    Expect(!CombatCollision.Sweep(this, from, to - from, 7f, CollisionLayers.World,
                        exclude, out body, out point), $"blocked meadow route {route[i][0]},{route[i][1]}");
                }
        }
        catch (Exception e) { _fail.Add(e.ToString()); }
        foreach (string fail in _fail) GD.PrintErr("[combat-check] " + fail);
        GD.Print($"[combat-check] {(_fail.Count == 0 ? "PASS" : "FAIL")} failures={_fail.Count}");
        CallDeferred(nameof(Finish));
    }

    private void Finish()
    {
        // Release the large test world while Godot still owns its native resources.
        // Do this after the async test returns, so its local shape references are no longer live.
        foreach (var child in GetChildren()) child.Free();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_fail.Count == 0 ? 0 : 1);
    }

    private StaticBody2D Body(Vector2 at, uint layer, Shape2D shape)
    {
        var body = new StaticBody2D { Position = at, CollisionLayer = layer, CollisionMask = 0 };
        body.AddChild(new CollisionShape2D { Shape = shape });
        AddChild(body);
        return body;
    }
}
