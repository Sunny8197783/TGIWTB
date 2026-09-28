using System;
using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>3D terrain/physics presentation; the existing skill, mastery and save model remains authoritative.</summary>
public partial class ReferenceWorld3D : Node3D
{
    public static ReferenceWorld3D Instance { get; private set; }
    public static bool Enabled => Array.IndexOf(OS.GetCmdlineUserArgs(), "--flat") < 0;
    public GameWorld World { get; set; }
    public static bool CheckRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--reference-check") >= 0;
    public CharacterBody3D PlayerBody => _actors[World.Player.GetInstanceId()].Body;
    public int ActorCount => _actors.Count;
    internal ulong PhysicsUsec, ArtUsec;
    private Camera3D _camera;
    private Godot.Environment _environment;
    private DirectionalLight3D _sun;
    private Vector3 _focus;
    private float _distance = 21f;
    private sealed class Actor
    {
        public Node2D Source;
        public CharacterBody3D Body;
        public Sprite3D Sprite;
        public PlayerSprite PlayerSprite;
        public MeshInstance3D Health;
        public float Height;
        public Area3D Hurt;
        public Texture2D HurtTexture;
    }
    private readonly Dictionary<ulong, Actor> _actors = new();
    private readonly Dictionary<Rid, Rid> _rids = new();
    private readonly Dictionary<Texture2D, AtlasTexture> _cropped = new();
    private readonly Dictionary<ulong, float> _projectileHeights = new();
    private double _elapsed;
    private bool _debugCollision;
    private float _debugRefresh;
    public bool CanCheckStanding { get; private set; }
    private const float Gravity = 18f;
    private MeadowEncounters _encounters;
    private readonly List<(EncounterDefinition Def, Sprite3D Sprite, Label3D Label)> _supplies = new();

    public override void _Ready()
    {
        Instance = this;
        Name = "Reference3D";
        ProcessPriority = 50;
        ProcessPhysicsPriority = 50;
        var terrain = new ReferenceTerrain3D(); AddChild(terrain); terrain.Build(World.Tiles);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.59f, .75f, .81f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(.77f, .84f, 1f), AmbientLightEnergy = .35f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true, FogLightColor = new Color(.38f, .52f, .43f), FogDensity = .002f,
        };
        _environment=environment;
        AddChild(new WorldEnvironment { Environment = environment });
        _sun=new DirectionalLight3D { RotationDegrees = new Vector3(-52f, -28f, 0f),
            LightColor = new Color(1f, .95f, .86f), LightEnergy = .85f, ShadowEnabled = true,
            DirectionalShadowMaxDistance = 45f };
        AddChild(_sun);
        _camera = new Camera3D { Fov = 36f, Near = .1f, Far = 85f, Current = true };
        if (DevCapture.IsRequested())
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--capture-zoom=") && float.TryParse(arg.Substring(15),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float zoom) && zoom > 0f)
                { _distance = 21f / Mathf.Clamp(zoom, .1f, 3f); _camera.Far = Mathf.Max(85f, _distance * 3f); }
        AddChild(_camera);
        _focus = ReferenceTerrain3D.Ground(((IPlayerContext)World.Player).WorldPosition);
        _camera.Position = _focus + new Vector3(0f, _distance * .66f, _distance * .75f);
        _camera.LookAt(_focus);
        foreach (var child in World.GetChildren())
            if (child is CanvasLayer layer) layer.Reparent(this);
        World.Visible = false;
        World.Camera.Enabled = false;
        var listener = new AudioListener2D(); World.Player.AddChild(listener); listener.MakeCurrent();
        WarmGoblinArt();
        World.Player.GetNode<PlayerSprite>("PlayerSprite").WarmFrames(((IPlayerContext)World.Player).Appearance);
        Scan(World);
        foreach (var node in World.GetChildren()) if (node is MeadowEncounters encounters) _encounters = encounters;
        foreach (var camp in MeadowLayout.Data.Camps)
        {
            if (camp.Continuous) continue;
            var at = ReferenceTerrain3D.Ground(camp.Center + new Vector2(0f, 80f));
            var chest = new Sprite3D { Texture = GD.Load<Texture2D>("res://art/objects32/crates.png"),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = .006f, Position = at + Vector3.Up * .3f,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Shaded = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
            var label = new Label3D { Position = at + Vector3.Up * .75f, Text = "F 보급품",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, OutlineSize = 6, PixelSize = .009f };
            AddChild(chest); AddChild(label); _supplies.Add((camp, chest, label));
        }
        GetTree().NodeAdded += OnNodeAdded;
        AddChild(new GestureSkillInput());
        AddChild(new ForestSecrets { World = World });
        GD.Print($"[Reference3D] ready actors={_actors.Count} camera=perspective native_3d_collision=true");
        if (CheckRequested) Callable.From(() => AddChild(new ReferenceChecks3D { View = this })).CallDeferred();
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        if (Instance == this) Instance = null;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(15f, _distance - 1.5f);
            else if (wheel.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(34f, _distance + 1.5f);
        }
    }

    private void WarmGoblinArt()
    {
        // Build the reusable silhouettes before combat: first-use image decoding and
        // convex decomposition otherwise stalls the frame when an archer turns or fires.
        for(int direction=0;direction<8;direction++)
        foreach(var clip in new (string Name,int Count)[]{(null,1),("walk",8),("attack",9)})
        for(int i=0;i<clip.Count;i++)
        {
            float angle=direction*Mathf.Pi/4f;
            var frame=ActorArt.FrameFor("goblin",new Vector2(Mathf.Sin(angle),Mathf.Cos(angle)),clip.Name,i/(float)clip.Count);
            if(frame.Texture==null) continue;
            if(!_cropped.TryGetValue(frame.Texture,out var texture))
                _cropped[frame.Texture]=texture=new AtlasTexture { Atlas=frame.Texture,Region=frame.Bounds };
            float scale=28f/frame.ReferenceHeight/ReferenceTerrain3D.Unit;
            SpriteCollision3D.Shapes(texture,scale,Mathf.Clamp(frame.Bounds.Size.X*scale*.45f,.18f,.7f));
        }
    }

    private void Scan(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child == this) continue;
            if (child is Node2D source && (source is CharacterBody2D or Npc or Arrow or SkillBolt)) Track(source);
            Scan(child);
        }
    }

    private void OnNodeAdded(Node node)
    {
        if (node is Node2D source && (source is CharacterBody2D or Npc or Arrow or SkillBolt))
            Callable.From(() => { if (IsInstanceValid(source) && !source.IsQueuedForDeletion()) Track(source); }).CallDeferred();
    }

    private void Track(Node2D source)
    {
        ulong id = source.GetInstanceId();
        if (_actors.ContainsKey(id)) return;
        if (source is CharacterBody2D && !World.Tiles.IsWalkable(source.GlobalPosition))
        {
            Vector2 at = source.GlobalPosition;
            if (World.FindStandingSpot(ref at, new HashSet<Vector2I>())) source.GlobalPosition = at;
        }
        bool projectile = source is Arrow or SkillBolt;
        float radius = source is MonsterBase monster ? monster.Stats.Radius / ReferenceTerrain3D.Unit
            : PixelMmo.Balance.PlayerTuning.BodySize * .5f / ReferenceTerrain3D.Unit;
        uint layer = source is CollisionObject2D co && !projectile ? co.CollisionLayer : 0;
        var body = new CharacterBody3D
        {
            Position = ReferenceTerrain3D.Ground(source.GlobalPosition) + Vector3.Up * .025f,
            CollisionLayer = layer, CollisionMask = source is CharacterBody2D ? CollisionLayers.World : 0,
            FloorSnapLength = .6f, FloorMaxAngle = Mathf.DegToRad(50f), SafeMargin = .005f,
        };
        float capsuleHeight = Mathf.Max(.5f, radius * 2f + .1f);
        body.AddChild(new CollisionShape3D { Position = Vector3.Up * (capsuleHeight * .5f),
            Shape = new CapsuleShape3D { Radius = radius, Height = capsuleHeight } });
        body.SetMeta("logic", source);
        if (source is CharacterBody2D movement) movement.CollisionMask = 0;
        AddChild(body);
        if (source is CollisionObject2D collider) _rids[collider.GetRid()] = body.GetRid();
        var sprite = new Sprite3D { Billboard = BaseMaterial3D.BillboardModeEnum.Disabled, Basis = ReferenceTerrain3D.ArtBasis,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Shaded = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard, AlphaScissorThreshold = .3f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
        body.AddChild(sprite);
        var actor = new Actor { Source = source, Body = body, Sprite = sprite,
            PlayerSprite = source is PlayerCharacter ? source.GetNodeOrNull<PlayerSprite>("PlayerSprite") : null };
        _actors[id] = actor;
        if (source is MonsterBase)
        {
            body.CollisionLayer = 0;
            actor.Hurt = new Area3D { CollisionLayer = CollisionLayers.Monster, CollisionMask = 0, Monitoring = false };
            actor.Hurt.SetMeta("logic", source); body.AddChild(actor.Hurt);
            if (source is CollisionObject2D monsterCollider) _rids[monsterCollider.GetRid()] = actor.Hurt.GetRid();
            var material = new StandardMaterial3D { AlbedoColor = new Color(.91f, .22f, .19f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled };
            actor.Health = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(.6f, .045f) },
                MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            body.AddChild(actor.Health);
        }
        if (source is Npc npc)
            body.AddChild(new Label3D { Text = npc.Def?.Name ?? "", Position = Vector3.Up * 1.05f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, OutlineSize = 6, PixelSize = .007f });
        if (source is TrainingDummy)
        {
            var texture = GD.Load<Texture2D>("res://art/objects32/training_dummy.png");
            using var image = texture.GetImage();
            Rect2 bounds = image.GetUsedRect();
            sprite.Texture = new AtlasTexture { Atlas = texture, Region = bounds };
            sprite.PixelSize = 1f / Mathf.Max(1f, bounds.Size.Y);
            sprite.Position = Vector3.Up * .5f;
            SpriteCollision3D.Set(actor.Hurt, sprite.Texture, sprite.PixelSize, .3f);
            if (actor.Health != null) actor.Health.Visible = false;
        }
        if (projectile)
        {
            sprite.Visible = false;
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.07f, .07f, .6f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = source is SkillBolt bolt ? bolt.VisualColor : PixelMmo.Balance.MonsterTuning.GoblinAttack.ArrowColor,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        }
        source.TreeExiting += () =>
        {
            _actors.Remove(id); _projectileHeights.Remove(id);
            if (source is CollisionObject2D c) _rids.Remove(c.GetRid());
            if (IsInstanceValid(body)) body.QueueFree();
        };
        UpdateArt(actor);
    }

    public override void _PhysicsProcess(double delta)
    {
        ulong started=Time.GetTicksUsec();
        if (!CanCheckStanding) { CanCheckStanding = true; World.Tiles.ReportBlockedRoads(); }
        float dt = (float)delta;
        foreach (Actor actor in _actors.Values)
        {
            if (!IsInstanceValid(actor.Source) || actor.Source.IsQueuedForDeletion()) continue;
            Vector2 requested = actor.Source.GlobalPosition;
            if (actor.Source is MonsterBase distant && requested.DistanceSquaredTo(((IPlayerContext)World.Player).WorldPosition) > 900f * 900f)
            { actor.Body.Velocity = Vector3.Zero; actor.Hurt.CollisionLayer = 0; continue; }
            Vector3 ground = ReferenceTerrain3D.Ground(requested);
            if (actor.Source is MonsterBase nearby) actor.Hurt.CollisionLayer = nearby.IsAlive ? CollisionLayers.Monster : 0;
            if (actor.Source is CharacterBody2D)
            {
                Vector3 motion = ground - actor.Body.Position;
                if (new Vector2(motion.X, motion.Z).Length() > 5f)
                {
                    Vector2 safe = requested;
                    if (World.FindStandingSpot(ref safe, new HashSet<Vector2I>())) ground = ReferenceTerrain3D.Ground(safe);
                    actor.Body.Position = ground + Vector3.Up * .03f;
                    motion = Vector3.Zero;
                }
                actor.Body.Velocity = new Vector3(motion.X / dt,
                    actor.Body.IsOnFloor() ? -.5f : actor.Body.Velocity.Y - Gravity * dt, motion.Z / dt);
                actor.Body.MoveAndSlide();
                // Resolve the requested horizontal step against real slopes/walls before the next combat tick.
                actor.Source.GlobalPosition = new Vector2(actor.Body.Position.X, actor.Body.Position.Z) * ReferenceTerrain3D.Unit;
            }
            else if (actor.Source is Arrow or SkillBolt)
            {
                float y = _projectileHeights.TryGetValue(actor.Source.GetInstanceId(), out float height) ? height : ground.Y + .45f;
                actor.Body.Position = new Vector3(ground.X, y, ground.Z);
                actor.Body.Rotation = new Vector3(0f, -actor.Source.Rotation + Mathf.Pi * .5f, 0f);
            }
            else if (actor.Body.Position != ground) actor.Body.Position = ground;
        }
        PhysicsUsec=Time.GetTicksUsec()-started;
    }

    public override void _Process(double delta)
    {
        ulong started=Time.GetTicksUsec();
        _debugRefresh -= (float)delta;
        if (_debugCollision != DebugFlags.ShowHitbox || (DebugFlags.ShowHitbox && _debugRefresh <= 0f))
        {
            _debugRefresh = .25f;
            _debugCollision = DebugFlags.ShowHitbox;
            foreach (var node in GetTree().GetNodesInGroup("art_collision"))
                if (node is CollisionObject3D body) SpriteCollision3D.ShowDebug(body, _debugCollision);
        }
        _elapsed += delta;
        IPlayerContext context = World.Player;
        bool indoor=WorldLayout.IronjawDen.Contains(context.WorldPosition);
        _sun.LightEnergy=Mathf.Lerp(_sun.LightEnergy,indoor ? .12f : .85f,Mathf.Min(1f,(float)delta*4f));
        _environment.AmbientLightEnergy=indoor ? .18f : .35f;
        _environment.BackgroundColor=indoor ? new Color(.055f,.08f,.065f) : new Color(.59f,.75f,.81f);
        _environment.FogLightColor=indoor ? new Color(.055f,.08f,.065f) : new Color(.38f,.52f,.43f);
        foreach (var (camp, chest, label) in _supplies)
        {
            bool claimed = context.HasFlag("supplies_" + camp.Id);
            chest.Modulate = claimed ? new Color(.5f, .5f, .5f) : Colors.White;
            label.Visible = !claimed && (_encounters?.SupplyReady(camp.Id) ?? false);
        }
        foreach (Actor actor in _actors.Values)
            if (IsInstanceValid(actor.Source) && !actor.Source.IsQueuedForDeletion()) UpdateArt(actor);
        if (_actors.TryGetValue(World.Player.GetInstanceId(), out var player))
        {
            Vector3 target = player.Body.Position + Vector3.Up * .5f;
            _focus = _focus.Lerp(target, 1f - Mathf.Exp(-8f * (float)delta));
            _camera.Position = _focus + new Vector3(0f, _distance * .66f, _distance * .75f);
            _camera.LookAt(_focus);
            // Shift the projection in screen space; looking back at the target cancels positional shake.
            float unitsPerPixel = 2f * _camera.Position.DistanceTo(_focus) * Mathf.Tan(Mathf.DegToRad(_camera.Fov * .5f))
                / Mathf.Max(1f, GetViewport().GetVisibleRect().Size.Y);
            Vector2 shake = World.Camera.Offset * GameCamera.ZoomLevel * unitsPerPixel;
            _camera.HOffset = shake.X;
            _camera.VOffset = -shake.Y;
        }
        ArtUsec=Time.GetTicksUsec()-started;
    }

    private void UpdateArt(Actor actor)
    {
        if (actor.Source is TrainingDummy) return;
        // Sleeping enemies keep their last pose. Refresh the exact silhouette on re-entry.
        if (actor.Source is MonsterBase && actor.HurtTexture != null &&
            actor.Source.GlobalPosition.DistanceSquaredTo(((IPlayerContext)World.Player).WorldPosition) > 900f * 900f) return;
        if (actor.PlayerSprite != null)
        {
            var source = actor.PlayerSprite;
            actor.Sprite.Texture = source.Texture;
            actor.Sprite.PixelSize = source.Scale.X / ReferenceTerrain3D.Unit;
            actor.Sprite.Position = ReferenceTerrain3D.ArtBasis.Y *
                (.42f - source.Offset.Y * actor.Sprite.PixelSize) + Vector3.Right * source.Offset.X * actor.Sprite.PixelSize;
            actor.Sprite.Modulate = source.Modulate;
            actor.Sprite.Visible = ((IPlayerContext)World.Player).IsAlive;
            return;
        }
        string key; Vector2 facing = Vector2.Down; float height;
        if (actor.Source is MonsterBase monster)
        {
            key = monster is Ironjaw ? "ironjaw" : monster is GoblinArcher ? "goblin" : "slime_blob";
            height = monster is Ironjaw ? 43f : monster is GoblinArcher ? 28f : 19f;
            facing = monster.ArtFacing;
            actor.Sprite.Modulate = monster.HitFlashing ? new Color(2f, 2f, 2f) : Colors.White;
            actor.Sprite.Visible = monster.IsAlive;
            float bob = monster.Velocity.Length() > 5f ? Mathf.Abs(Mathf.Sin((float)_elapsed * 12f)) * .055f : 0f;
            actor.Sprite.Position = Vector3.Up * (height / ReferenceTerrain3D.Unit * .5f + bob);
            if (actor.Health != null)
            {
                actor.Health.Visible = monster.IsAlive && monster.HpRatio < 1f;
                actor.Health.Scale = new Vector3(Mathf.Max(.001f, monster.HpRatio), 1f, 1f);
                actor.Health.Position = Vector3.Up * (height / ReferenceTerrain3D.Unit + .15f);
            }
        }
        else if (actor.Source is Npc npc)
        {
            string role = npc.Def?.Role is "merchant" or "guard" or "trainer" ? npc.Def.Role : "quest";
            key = "npc_" + role; height = 28f;
            actor.Sprite.Position = Vector3.Up * (height / ReferenceTerrain3D.Unit * .5f);
        }
        else return;
        var animated = actor.Source as MonsterBase;
        var frame = ActorArt.FrameFor(key, facing, animated?.ArtClip, animated?.ArtProgress);
        if (frame.Texture == null) return;
        if (!_cropped.TryGetValue(frame.Texture, out var texture))
            _cropped[frame.Texture] = texture = new AtlasTexture { Atlas = frame.Texture, Region = frame.Bounds };
        actor.Sprite.Texture = texture;
        actor.Sprite.PixelSize = height / frame.ReferenceHeight / ReferenceTerrain3D.Unit;
        actor.Height = frame.Bounds.Size.Y * actor.Sprite.PixelSize;
        actor.Sprite.Position = ReferenceTerrain3D.ArtBasis.Y * (actor.Height * .5f);
        if (actor.Hurt != null && actor.HurtTexture != texture)
        {
            actor.Hurt.Basis = ReferenceTerrain3D.ArtBasis;
            actor.HurtTexture = texture;
            float width = frame.Bounds.Size.X * actor.Sprite.PixelSize;
            SpriteCollision3D.Set(actor.Hurt, texture, actor.Sprite.PixelSize, Mathf.Clamp(width * .45f, .18f, .7f));
            var collider = (CollisionShape3D)actor.Body.GetChild(0);
            float radius = Mathf.Clamp(width * .24f, .1f, .45f);
            float bodyHeight = Mathf.Max(radius * 2f, actor.Height * .7f);
            collider.Shape = new CapsuleShape3D { Radius = radius, Height = bodyHeight };
            collider.Position = Vector3.Up * (bodyHeight * .5f);
        }
    }

    public bool CanStand(Vector2 point)
    {
        if (World.Tiles.GetCellSourceId(World.Tiles.LocalToMap(point)) < 0) return false;
        using var capsule = new CapsuleShape3D { Radius = PixelMmo.Balance.PlayerTuning.BodySize * .5f / ReferenceTerrain3D.Unit, Height = .6f };
        using var query = new PhysicsShapeQueryParameters3D { Shape = capsule, CollisionMask = CollisionLayers.World,
            Transform = new Transform3D(Basis.Identity, ReferenceTerrain3D.Ground(point) + Vector3.Up * (capsule.Height * .5f + .12f)) };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    public HashSet<Node2D> MeleeTargets(PixelMmo.Data.SkillShape shape, Vector2 origin, Vector2 facing)
    {
        var targets = new HashSet<Node2D>();
        if (shape == null || shape.RangePx <= 0f || (shape.Kind != "circle" && shape.Kind != "cone")) return targets;
        float range = shape.RangePx / ReferenceTerrain3D.Unit;
        float angle = shape.Kind == "circle" ? 360f : Mathf.Clamp(shape.AngleDeg, 1f, 360f);
        int sectors = Mathf.CeilToInt(angle / 90f);
        using var query = new PhysicsShapeQueryParameters3D { CollisionMask = CollisionLayers.Monster,
            CollideWithAreas = true, CollideWithBodies = false,
            Transform = new Transform3D(Basis.Identity, ReferenceTerrain3D.Ground(origin)) };
        for (int s = 0; s < sectors; s++)
        {
            var points = new List<Vector3> { Vector3.Zero, Vector3.Up * 2f };
            for (int i = 0; i <= 8; i++)
            {
                float a = facing.Angle() + Mathf.DegToRad(-angle * .5f + angle * (s + i / 8f) / sectors);
                var p = new Vector3(Mathf.Cos(a) * range, 0f, Mathf.Sin(a) * range);
                points.Add(p); points.Add(p + Vector3.Up * 2f);
            }
            using var volume = new ConvexPolygonShape3D { Points = points.ToArray() }; query.Shape = volume;
            foreach (var hit in GetWorld3D().DirectSpaceState.IntersectShape(query, 128))
                if (hit["collider"].AsGodotObject() is Area3D area && area.HasMeta("logic"))
                    targets.Add(area.GetMeta("logic").AsGodotObject() as Node2D);
        }
        return targets;
    }

    public Vector2 AimPoint()
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 from = _camera.ProjectRayOrigin(mouse);
        using var query = PhysicsRayQueryParameters3D.Create(from, from + _camera.ProjectRayNormal(mouse) * 150f, CollisionLayers.World);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return ((IPlayerContext)World.Player).WorldPosition;
        Vector3 at = hit["position"].AsVector3();
        return new Vector2(at.X, at.Z) * ReferenceTerrain3D.Unit;
    }

    public bool ClearSight(Vector2 from, Vector2 to)
    {
        Vector3 a = ReferenceTerrain3D.Ground(from) + Vector3.Up * .45f;
        Vector3 b = ReferenceTerrain3D.Ground(to) + Vector3.Up * .45f;
        if (Mathf.Abs(a.Y - b.Y) > 1.1f) return false;
        using var query = PhysicsRayQueryParameters3D.Create(a, b, CollisionLayers.World);
        query.HitFromInside = true;
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    public bool Sweep(Node2D owner, Vector2 from, Vector2 motion, float radius, uint mask,
        Godot.Collections.Array<Rid> excluded, out Node2D body, out Vector2 position)
    {
        ulong id = owner.GetInstanceId();
        if (!_projectileHeights.TryGetValue(id, out float y))
        {
            _projectileHeights[id] = y = ReferenceTerrain3D.HeightAt(from) + .45f;
            owner.TreeExiting += () => _projectileHeights.Remove(id);
        }
        var exclude3d = new Godot.Collections.Array<Rid>();
        foreach (Rid rid in excluded) if (_rids.TryGetValue(rid, out Rid mapped)) exclude3d.Add(mapped);
        using var sphere = new SphereShape3D { Radius = radius / ReferenceTerrain3D.Unit };
        var origin = new Vector3(from.X / ReferenceTerrain3D.Unit, y, from.Y / ReferenceTerrain3D.Unit);
        var travel = new Vector3(motion.X, 0f, motion.Y) / ReferenceTerrain3D.Unit;
        using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new Transform3D(Basis.Identity, origin),
            CollisionMask = mask, Exclude = exclude3d, Margin = .001f, CollideWithAreas = true };
        var space = GetWorld3D().DirectSpaceState;
        var hits = space.IntersectShape(query, 32);
        body = null; position = from;
        if (hits.Count == 0)
        {
            query.Motion = travel;
            var fractions = space.CastMotion(query);
            if (fractions.Length < 2 || fractions[0] >= 1f) { position = from + motion; return false; }
            position = from + motion * fractions[0];
            query.Transform = new Transform3D(Basis.Identity, origin + travel * fractions[1]);
            query.Motion = Vector3.Zero; query.Margin = .004f;
            hits = space.IntersectShape(query, 32);
        }
        foreach (var hit in hits)
        {
            var candidate = hit["collider"].AsGodotObject() as CollisionObject3D;
            if (candidate == null) continue;
            if ((candidate.CollisionLayer & CollisionLayers.World) != 0) { body = World.Tiles; return true; }
            if (candidate.HasMeta("logic")) body ??= candidate.GetMeta("logic").AsGodotObject() as Node2D;
        }
        return true;
    }

    public void Popup(Vector2 point, float damage, bool heavy, bool hurt)
    {
        var label = new Label3D { Text = Mathf.RoundToInt(damage).ToString(),
            Position = ReferenceTerrain3D.Ground(point) + Vector3.Up * 1.1f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = heavy ? 58 : 44,
            OutlineSize = 8, PixelSize = .009f, Modulate = hurt ? new Color(1f, .32f, .3f) : new Color(1f, .92f, .62f) };
        AddChild(label);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position:y", label.Position.Y + .8f, .6);
        tween.TweenProperty(label, "modulate:a", 0f, .6);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    public void Ring(Vector2 point, float radius, Color color, float seconds, float angle = 0f, float arc = Mathf.Tau, float innerRatio = .78f)
    {
        if (seconds <= 0f || radius <= 0f || arc <= 0f) return;
        using var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        float outer = radius / ReferenceTerrain3D.Unit;
        bool slash = arc < Mathf.Tau - .01f;
        Vector3 P(float t, float edge)
        {
            float theta = angle - arc * .5f + arc * t;
            float width = outer * (1f - Mathf.Clamp(innerRatio, 0f, 1f));
            if (slash) width *= Mathf.Sin(Mathf.Pi * t); // pointed ends, broad middle
            float r = outer - width * edge;
            Vector2 world = point + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * r * ReferenceTerrain3D.Unit;
            float height = ReferenceTerrain3D.HeightAt(world) - ReferenceTerrain3D.HeightAt(point);
            return new Vector3(Mathf.Cos(theta) * r, height, Mathf.Sin(theta) * r);
        }
        for (int i = 0; i < 32; i++)
        {
            float a = i / 32f, b = (i + 1) / 32f;
            foreach (var band in new[] { (Inner: 1f, Outer: .2f, Tint: color),
                (Inner: .2f, Outer: 0f, Tint: color.Lerp(Colors.White, .85f)) })
            {
                st.SetColor(band.Tint);
                foreach (var v in new[] { P(a, band.Inner), P(b, band.Inner), P(a, band.Outer),
                    P(a, band.Outer), P(b, band.Inner), P(b, band.Outer) }) st.AddVertex(v);
            }
        }
        var fx = FeedbackMesh(st, point, slash ? .45f : .12f, seconds, false);
        fx.Scale = new Vector3(slash ? .9f : .3f, 1f, slash ? .9f : .3f);
        CreateTween().TweenProperty(fx, "scale", new Vector3(1f, 1f, 1f), seconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    public void Impact(Vector2 point, bool heavy)
    {
        float seconds = CombatTuning.VfxSparkLife;
        float radius = (heavy ? CombatTuning.VfxHeavySparkRadius : CombatTuning.VfxSparkRadius) / ReferenceTerrain3D.Unit;
        if (seconds <= 0f || radius <= 0f) return;
        using var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        int rays = heavy ? 9 : 6;
        for (int i = 0; i < rays; i++)
        {
            Vector2 direction = Vector2.FromAngle(i * Mathf.Tau / rays + .2f);
            Vector2 side = new(-direction.Y, direction.X);
            float length = radius * (i % 2 == 0 ? 1f : .65f);
            st.SetColor(new Color(1f, .98f, .8f));
            st.AddVertex(new Vector3(side.X * radius * .11f, side.Y * radius * .11f, 0f));
            st.AddVertex(new Vector3(-side.X * radius * .11f, -side.Y * radius * .11f, 0f));
            st.SetColor(new Color(1f, .6f, .16f, .65f));
            st.AddVertex(new Vector3(direction.X * length, direction.Y * length, 0f));
        }
        var fx = FeedbackMesh(st, point, .55f, seconds, true);
        fx.Scale = Vector3.One * .45f;
        CreateTween().TweenProperty(fx, "scale", Vector3.One * 1.45f, seconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (heavy) Ring(point, CombatTuning.VfxHeavySparkRadius, new Color(1f, .72f, .3f, .7f), seconds);
    }

    private MeshInstance3D FeedbackMesh(SurfaceTool surface, Vector2 point, float height, float seconds, bool billboard)
    {
        var material = new StandardMaterial3D { AlbedoColor = Colors.White, VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled };
        var fx = new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material,
            Position = ReferenceTerrain3D.Ground(point) + Vector3.Up * height,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        fx.AddToGroup("combat_vfx");
        AddChild(fx);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(material, "albedo_color:a", 0f, seconds);
        tween.Chain().TweenCallback(Callable.From(fx.QueueFree));
        return fx;
    }
}
