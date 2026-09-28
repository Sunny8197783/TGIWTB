using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>Run the complete 3D world with --reference-check; never load or write the player's save.</summary>
public partial class ReferenceChecks3D : Node3D
{
    public ReferenceWorld3D View { get; set; }
    private readonly List<string> _fail = new();
    private void Expect(bool pass, string label) { if (!pass) _fail.Add(label); }
    public override async void _Ready()
    {
        try
        {
            View.World.Player.DebugInvulnerable = true;
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Expect(View.ActorCount >= 40, "missing 3D actors/NPCs");
            CheckMapConsistency();
            await CheckSilhouettes();
            await CheckFeedback();
            await CheckContinuousSpawns();
            await CheckRpgUi();
            await CheckInputTransitions();
            await CustomizationChecks.Run(this,View.World.Player,Expect);
            foreach (var camp in MeadowLayout.Data.Camps)
            {
                Vector3 expected = ReferenceTerrain3D.Ground(camp.Center);
                using var ray = PhysicsRayQueryParameters3D.Create(expected + Vector3.Up * 12f,
                    expected - Vector3.Up * 4f, CollisionLayers.World);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                Expect(hit.Count > 0 && Mathf.Abs(hit["position"].AsVector3().Y - expected.Y) < .03f,
                    "ground collider/winding mismatch: " + camp.Id);
            }
            Vector2 start = WorldLayout.TileCenter(140, 108);
            View.World.Player.GlobalPosition = start;
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Expect(View.PlayerBody.IsOnFloor(), "player must rest on the slope");
            Expect(Mathf.Abs(View.PlayerBody.Position.Y - ReferenceTerrain3D.HeightAt(((IPlayerContext)View.World.Player).WorldPosition)) < .08f,
                "floating/sinking player");
            Input.ActionPress(InputSetup.MoveRight);
            for (int i = 0; i < 22; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.MoveRight);
            Vector2 end = ((IPlayerContext)View.World.Player).WorldPosition;
            Expect(end.X > start.X + 8f && View.PlayerBody.IsOnFloor(), "walking must advance along a slope");
            Expect(View.World.Tiles.IsWalkable(end), "3D movement entered blocked 2D footprint");
            foreach (var direction in new[] { Vector2.Right, new Vector2(1, 1), Vector2.Down, new Vector2(-1, 1),
                Vector2.Left, new Vector2(-1, -1), Vector2.Up, new Vector2(1, -1) })
            {
                if (direction.X != 0) Input.ActionPress(direction.X > 0 ? InputSetup.MoveRight : InputSetup.MoveLeft);
                if (direction.Y != 0) Input.ActionPress(direction.Y > 0 ? InputSetup.MoveDown : InputSetup.MoveUp);
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                Expect(View.World.Player.Facing.Dot(direction.Normalized()) > .99f, "movement facing: " + direction);
                foreach (string action in new[] { InputSetup.MoveRight, InputSetup.MoveLeft, InputSetup.MoveUp, InputSetup.MoveDown }) Input.ActionRelease(action);
            }
            Expect(View.World.Player.CollisionMask == 0, "3D movement must not also collide in the hidden 2D world");
            foreach (var node in GetTree().GetNodesInGroup(MonsterBase.Group))
                if (node is Slime slime && slime.IsAlive && slime.GlobalPosition.DistanceTo(end) < 500f)
                {
                    var shape = new PixelMmo.Data.SkillShape { Kind = "circle", RangePx = 5f };
                    Expect(View.MeleeTargets(shape, slime.GlobalPosition, Vector2.Right).Contains(slime), "melee must contact actual monster silhouette");
                    Expect(!View.MeleeTargets(shape, slime.GlobalPosition + new Vector2(80f, 0f), Vector2.Right).Contains(slime), "melee must miss outside monster silhouette");
                    break;
                }
            View.World.Player.GlobalPosition = WorldLayout.SpawnPoint;
            for (int i = 0; i < 15; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Expect(View.World.Tiles.IsWalkable(((IPlayerContext)View.World.Player).WorldPosition) && View.PlayerBody.IsOnFloor(),
                "respawn/load teleport must resolve to a safe standing spot");
            var projectile = new Node2D(); AddChild(projectile);
            bool wall = View.Sweep(projectile, WorldLayout.TileCenter(124, 100), new Vector2(400f, 0f), 2.5f,
                CollisionLayers.World, new Godot.Collections.Array<Rid>(), out var body, out var contact);
            Expect(wall && contact.X < 138f * 32f, "projectile must stop at visible river bank");
            Expect(!View.World.Tiles.IsWalkable(WorldLayout.TileCenter((int)WorldLayout.RiverX(100),100)), "river must block spawning");
            await CheckDiscoveries();
            Expect(GestureSkillInput.Recognize(new[] { Vector2.Zero, new Vector2(40, 0), new Vector2(90, 0) }) == "line", "line gesture");
            var circle = new List<Vector2>();
            for (int i = 0; i <= 24; i++) circle.Add(Vector2.FromAngle(Mathf.Tau * i / 24f) * 60f);
            Expect(GestureSkillInput.Recognize(circle) == "circle", "circle gesture");
            Expect(GestureSkillInput.Recognize(new[] { Vector2.Zero, new Vector2(100, 0), new Vector2(0, 70), new Vector2(100, 70) }) == "zigzag", "zigzag gesture");
            Expect(GestureSkillInput.Recognize(new[] { Vector2.Zero, Vector2.One, new Vector2(2, 1) }) == "", "tiny gestures must not cast");
            Expect(SaveSystem.Instance == null || !SaveSystem.Instance.Save(View.World.Player, "reference_check"), "test must not overwrite live saves");
        }
        catch (Exception e) { _fail.Add(e.ToString()); }
        finally { Input.ActionRelease(InputSetup.MoveRight); }
        foreach (var fail in _fail) GD.PrintErr("[reference-check] " + fail);
        GD.Print($"[reference-check] {(_fail.Count == 0 ? "PASS" : "FAIL")} failures={_fail.Count}");
        GetTree().Quit(_fail.Count == 0 ? 0 : 1);
    }

    private async System.Threading.Tasks.Task CheckInputTransitions()
    {
        var actor=View.World.Player;
        IPlayerContext player=actor;
        var saved=player.CaptureSave();
        try {
            Input.ActionRelease(InputSetup.MoveRight);Input.ActionRelease(InputSetup.Attack);Input.ActionRelease(InputSetup.Dash);
            // Drain the previous check's just-pressed flags before starting a fresh profile.
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            actor.ResetToNewGame();
            Expect(actor.DashCooldownRemaining==0 && !actor.IsInvulnerable,"new game clears dodge cooldown and invulnerability");
            for(int i=0;i<6;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionPress(InputSetup.MoveRight);
            Input.ActionPress(InputSetup.Attack);
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.Attack);
            for(int i=0;i<20 && actor.Attack.Phase!=SkillPhase.Active;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Expect(actor.Attack.Phase==SkillPhase.Active,"input starts an attack");
            actor.ApplyHitstop(.06f); // 4 frames: press during contact freeze.
            Input.ActionPress(InputSetup.Dash);
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.Dash);
            bool dashed=false;
            for(int i=0;i<30;i++) {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                if(actor.IsDashing) { dashed=true;break; }
            }
            Expect(dashed,"contact hitstop must preserve dodge until recovery");
            Expect(player.Stamina<player.MaxStamina,"buffered dodge consumes stamina");
            Input.ActionPress(InputSetup.Attack);
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.Attack);
            bool counter=false;
            for(int i=0;i<25;i++) {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                if(actor.Attack.IsBusy) { counter=true;break; }
            }
            Expect(counter,"attack buffered during dodge must start after dodge");
            Expect(player.Mastery.Get("sk_slash")==0,"air attacks must not grant mastery");
            player.RestoreSave(saved);
            Expect(!actor.IsDashing && actor.DashCooldownRemaining==0 && !actor.IsInvulnerable,"load clears transient dodge state");
        }
        finally {
            Input.ActionRelease(InputSetup.MoveRight);Input.ActionRelease(InputSetup.Attack);Input.ActionRelease(InputSetup.Dash);
            player.RestoreSave(saved);
        }
    }

    private async System.Threading.Tasks.Task CheckDiscoveries()
    {
        IPlayerContext player=View.World.Player;
        foreach(var point in ForestSecrets.Stones)
        {
            View.World.Player.GlobalPosition=point+new Vector2(0,30);
            for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionPress(InputSetup.Interact);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.Interact);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        }
        Expect(player.HasFlag("forest_sanctum"),"discovery interactions must open the sanctuary");
        View.World.Player.GlobalPosition=WorldLayout.DungeonDoor+new Vector2(0,30);
        for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        Input.ActionPress(InputSetup.Interact);
        for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        Input.ActionRelease(InputSetup.Interact);
        Expect(WorldLayout.IronjawDen.Contains(player.WorldPosition),"hidden dungeon entry must teleport to playable interior");
        Expect(View.World.Tiles.SanctuaryMinimap.GetWidth()==WorldLayout.IronjawDen.Tiles.Grow(2).Size.X,
            "dungeon minimap must use local bounds");
        for(int i=0;i<70;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        View.World.Player.GlobalPosition=WorldLayout.DungeonArrival+new Vector2(0,25);
        for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        Input.ActionPress(InputSetup.Interact);
        for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        Input.ActionRelease(InputSetup.Interact);
        Expect(!WorldLayout.IronjawDen.Contains(player.WorldPosition),"dungeon must have a working return path");
    }

    private async System.Threading.Tasks.Task CheckRpgUi()
    {
        var hud=PlayerHud.Instance;
        IPlayerContext player=View.World.Player;
        var saved=player.CaptureSave();
        Expect(hud!=null,"RPG HUD must exist");
        if(hud==null)return;
        try
        {
            hud._Input(new InputEventAction { Action=InputSetup.CharacterSheet,Pressed=true });
            Expect(hud.MenuOpen && hud.CurrentPage=="status","P opens character sheet");
            Vector2 at=player.WorldPosition;
            Input.ActionPress(InputSetup.MoveRight); Input.ActionPress(InputSetup.Attack);
            for(int i=0;i<8;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.MoveRight); Input.ActionRelease(InputSetup.Attack);
            Expect(at.DistanceTo(player.WorldPosition)<1f,"book must block background movement/attack input");
            hud._Input(new InputEventAction { Action=InputSetup.SkillBook,Pressed=true });
            Expect(hud.MenuOpen && hud.CurrentPage=="skills","B switches to skills");
            hud.CloseMenu(); player.RestoreFull();
            for(int i=0;i<2;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionPress(InputSetup.Dash);
            for(int i=0;i<3;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Input.ActionRelease(InputSetup.Dash);
            Expect(player.Stamina<player.MaxStamina && player.Stamina>=0f,"dodge consumes real stamina");
            for(int i=0;i<120;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Expect(Mathf.IsEqualApprox(player.Stamina,player.MaxStamina),"stamina recovers after the delay");
            var depleted=player.CaptureSave(); depleted.Stamina=0f; player.RestoreSave(depleted);
            Input.ActionPress(InputSetup.Dash);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            Expect(!View.World.Player.IsDashing,"insufficient stamina blocks dodge");
            Input.ActionRelease(InputSetup.Dash);
            depleted.Stamina=null; player.RestoreSave(depleted);
            Expect(player.Stamina==player.MaxStamina,"old saves without stamina remain playable");
        }
        finally
        {
            Input.ActionRelease(InputSetup.MoveRight);Input.ActionRelease(InputSetup.Attack);Input.ActionRelease(InputSetup.Dash);
            hud.CloseMenu();player.RestoreSave(saved);
        }
    }

    private async System.Threading.Tasks.Task CheckFeedback()
    {
        var camera = View.World.Camera;
        var projection = GetViewport().GetCamera3D();
        Vector2 at = ((IPlayerContext)View.World.Player).WorldPosition;
        camera.SetProcess(false);
        View.SetProcess(false);
        float strength = CombatTuning.ShakeStrength;
        try
        {
            camera._Process(1.0); View._Process(0.0);
            Vector3 point = ReferenceTerrain3D.Ground(at) + Vector3.Up * .5f;
            Vector2 before = projection.UnprojectPosition(point);
            camera.ShakeNormalHit(Vector2.Right);
            camera._Process(1.0 / 120.0); View._Process(0.0);
            float normal = before.DistanceTo(projection.UnprojectPosition(point));
            Expect(normal > 3f, "normal impact must shift the actual 3D screen projection");
            camera._Process(1.0); View._Process(0.0);
            camera.ShakeHeavyHit(Vector2.Right);
            camera._Process(1.0 / 120.0); View._Process(0.0);
            float heavy = before.DistanceTo(projection.UnprojectPosition(point));
            Expect(heavy > normal * 1.5f, "heavy impact must exceed normal shake");
            float firstKick = camera.Offset.X;
            camera._Process(1.0 / 36.0);
            Expect(firstKick * camera.Offset.X < 0f, "impact must recoil in the opposite direction");
            camera._Process(1.0); View._Process(0.0);
            Expect(camera.Offset == Vector2.Zero && projection.HOffset == 0f && projection.VOffset == 0f,
                "shake must settle without camera drift");
            CombatTuning.ShakeStrength.Value = 0f;
            camera.ShakeHeavyHit(); camera._Process(1.0 / 120.0); View._Process(0.0);
            Expect(camera.Offset == Vector2.Zero, "zero strength must disable screen shake");
            int count = GetTree().GetNodesInGroup("combat_vfx").Count;
            CombatFeedback.Instance.SlashAt(at, 0f, -.7f, .7f, 12f, 30f, Colors.Cyan);
            CombatFeedback.Instance.ShockAt(at, 48f, Colors.Orange);
            CombatFeedback.Instance.SparkAt(at, true);
            Expect(GetTree().GetNodesInGroup("combat_vfx").Count >= count + 4, "3D slash, shock and impact meshes must spawn");
            await ToSignal(GetTree().CreateTimer(.6), SceneTreeTimer.SignalName.Timeout);
            Expect(GetTree().GetNodesInGroup("combat_vfx").Count == count, "transient effects must release their nodes");
        }
        finally
        {
            CombatTuning.ShakeStrength.Value = strength;
            camera.SetProcess(true);
            View.SetProcess(true);
        }
    }

    private async System.Threading.Tasks.Task CheckSilhouettes()
    {
        SpriteContourChecks.Verify(Expect);
        var archer = ActorArt.FrameFor("goblin", new Vector2(1, -1), "attack", 5f / 9f);
        var archerTexture = new AtlasTexture { Atlas = archer.Texture, Region = archer.Bounds };
        Expect(SpriteCollision3D.Shapes(archerTexture, .04f, .2f).Length > 0,
            "archer release frame with a folded pixel contour must retain its hurt shapes");
        using var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        image.FillRect(new Rect2I(2, 2, 10, 28), Colors.White);
        image.FillRect(new Rect2I(2, 22, 28, 8), Colors.White);
        var texture = ImageTexture.CreateFromImage(image);
        const uint layer = 1u << 15;
        var scenery = new StaticBody3D { Position = new Vector3(-40, 10, -40), CollisionLayer = layer, CollisionMask = 0 };
        var hurt = new Area3D { Position = new Vector3(-42, 10, -40), CollisionLayer = layer, CollisionMask = 0 };
        AddChild(scenery); AddChild(hurt);
        SpriteCollision3D.Set(scenery, texture, .04f, .2f);
        SpriteCollision3D.Set(hurt, texture, .04f, .2f);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        foreach (CollisionObject3D body in new CollisionObject3D[] { scenery, hurt })
        {
            foreach (var sample in new[] { (new Vector2(6, 7), true), (new Vector2(24, 7), false), (new Vector2(24, 25), true) })
            {
                Vector3 at = body.Position + new Vector3((sample.Item1.X - 16f) * .04f, (32f - sample.Item1.Y) * .04f, 0);
                using var ray = PhysicsRayQueryParameters3D.Create(at + Vector3.Back, at + Vector3.Forward, layer);
                ray.CollideWithAreas = true;
                bool hit = GetWorld3D().DirectSpaceState.IntersectRay(ray).Count > 0;
                Expect(hit == sample.Item2, "opaque/transparent silhouette: " + body.GetClass() + sample.Item1);
            }
        }
        scenery.QueueFree(); hurt.QueueFree();
    }

    private void CheckMapConsistency()
    {
        int samples = 0;
        using var shape = new CapsuleShape3D { Radius = .2f, Height = .8f };
        using var query = new PhysicsShapeQueryParameters3D { Shape = shape, CollisionMask = CollisionLayers.World };
        void CheckPoint(Vector2 point, string label)
        {
            label += " at=" + (point/32f);
            samples++;
            Expect(View.World.Tiles.IsWalkable(point), "map route footprint: " + label);
            query.Transform = new Transform3D(Basis.Identity, ReferenceTerrain3D.Ground(point) + Vector3.Up * .65f);
            Expect(GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0, "map route 3D obstruction: " + label);
        }
        foreach (var route in MeadowLayout.Data.Routes)
            for (int i = 1; i < route.Length; i++)
            {
                Vector2 a = WorldLayout.TileCenter(route[i - 1][0], route[i - 1][1]);
                Vector2 b = WorldLayout.TileCenter(route[i][0], route[i][1]);
                int steps = Mathf.CeilToInt(a.DistanceTo(b) / 16f);
                for (int n = 0; n <= steps; n++) CheckPoint(a.Lerp(b, n / (float)steps), $"{i}/{n}");
            }
        foreach (var (rect, a, b) in WorldLayout.Corridors())
        {
            Vector2I center = rect.Position + rect.Size / 2;
            CheckPoint(WorldLayout.TileCenter(center.X, center.Y), a.Id + "/" + b.Id);
        }
        foreach (var camp in MeadowLayout.Data.Camps) CheckPoint(camp.Center + new Vector2(0f, 80f), camp.Id + "/supply");
        foreach (var camp in MeadowLayout.Data.Camps)
        {
            if (!camp.Continuous) continue;
            Expect((camp.Slimes > 0) != (camp.Goblins > 0), "single-species hunting ground: " + camp.Id);
            for (int i=0;i<camp.Goblins;i++)
            {
                var home=camp.Center+new Vector2(65,(i-(camp.Goblins-1)*.5f)*65);
                CheckPoint(home,"patrol/home"); CheckPoint(home+new Vector2(-48,0),"patrol/work");
                CheckPoint(home+new Vector2(0,32),"patrol/return");
            }
        }
        for (float y=70;y<180;y+=3.25f)
        {
            if (WorldLayout.Bridge(new(WorldLayout.RiverX(y),y)) || WorldLayout.Bridge(new(WorldLayout.RiverX(y),y+1))) continue;
            foreach (int side in new[] {-1,1})
            {
                Vector2 dry=new(WorldLayout.RiverX(y)+side*(WorldLayout.RiverWidth(y)+.6f),y);
                Vector2 wet=new(WorldLayout.RiverX(y)+side*(WorldLayout.RiverWidth(y)-.6f),y);
                CheckPoint(dry*32,"curved bank");
                Expect(!View.World.Tiles.IsWalkable(wet*32),"curved river collision");
            }
        }
        var town = WorldLayout.Town.Tiles;
        for (int y = 0; y < town.Size.Y; y++)
        for (int x = 0; x < town.Size.X; x++)
            if (View.World.Tiles.Town.IsRoadCell(x, y))
                CheckPoint(WorldLayout.TileCenter(town.Position.X + x, town.Position.Y + y), $"town/{x}/{y}");
        var reached = new HashSet<Vector2I>();
        var queue = new Queue<Vector2I>();
        var start = (Vector2I)(WorldLayout.SpawnPoint / WorldLayout.TileSize);
        queue.Enqueue(start); reached.Add(start);
        while (queue.Count > 0)
        {
            Vector2I cell = queue.Dequeue();
            foreach (Vector2I step in new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right })
            {
                Vector2I next = cell + step;
                if (next.X < 0 || next.Y < 0 || next.X >= WorldLayout.WidthTiles || next.Y >= WorldLayout.HeightTiles || reached.Contains(next)) continue;
                if (!View.World.Tiles.IsWalkable(WorldLayout.TileCenter(next.X, next.Y))) continue;
                reached.Add(next); queue.Enqueue(next);
            }
        }
        foreach (var (rect, a, b) in WorldLayout.Corridors())
            Expect(reached.Contains(rect.Position + rect.Size / 2), "disconnected world gate: " + a.Id + "/" + b.Id);
        GD.Print($"[reference-check] connected walkable cells={reached.Count} zones={WorldLayout.Zones.Length}");
        foreach(var camp in MeadowLayout.Data.Camps)
            Expect(reached.Contains(new Vector2I(camp.X,camp.Y)), "disconnected forest clearing: "+camp.Id);
        foreach(var point in ForestSecrets.Stones)
            Expect(reached.Contains((Vector2I)(point/32f)+Vector2I.Down), "unreachable forest discovery");
        Expect(reached.Contains((Vector2I)(WorldLayout.DungeonDoor/32f)+Vector2I.Down*2), "unreachable hidden entrance");
        using var map = View.World.Tiles.Minimap.GetImage();
        foreach(var pool in WorldLayout.ForestPools)
            Expect(!View.World.Tiles.IsWalkable(pool.Center*32),"forest pond must block movement");
        int riverX=(int)WorldLayout.RiverX(100);
        Expect(map.GetPixel(riverX,100).B > map.GetPixel(riverX,100).R,"minimap river");
        Expect(WorldLayout.Zones.Length==4 && !WorldLayout.IsForest(Vector2.Zero),"retired rectangular regions must be absent");
        GD.Print($"[reference-check] map routes/gates/supplies checked={samples}");
    }

    private async System.Threading.Tasks.Task CheckContinuousSpawns()
    {
        var spawner=new MonsterSpawner(); View.World.AddChild(spawner);
        spawner.Configure(View.World.Tiles,View.World.Player);
        spawner.SetProcess(false);
        var born=new List<MonsterBase>();
        spawner.AddFixedSlot(()=> { var mob=new Slime();born.Add(mob);return mob; },WorldLayout.TileCenter(300,179),.5f);
        born[0].TakeDamage(new DamageInfo { Amount=10000f,Direction=Vector2.Zero });
        for(int i=0;i<100 && spawner.AliveCount>0;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        Expect(spawner.AliveCount==0,"continuous spawn waits for death");
        spawner._Process(.25); Expect(born.Count==1,"continuous respawn cooldown");
        spawner._Process(.30); Expect(born.Count==2 && born[1] is Slime,"continuous respawn without supply or leaving area");
        spawner.QueueFree();
        await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
    }
}
