using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>Small forest discoveries. Save flags retain changes without exposing hidden conditions.</summary>
public partial class ForestSecrets : Node3D
{
    public GameWorld World { get; set; }
    private readonly List<(Vector2 Point, string Flag, MeshInstance3D Stone)> _stones = new();
    private Node3D _door, _shortcut;
    private Label3D _prompt;
    private float _travelCooldown;
    public static readonly Vector2[] Stones = { WorldLayout.TileCenter(78,42), WorldLayout.TileCenter(60,69), WorldLayout.TileCenter(104,73) };
    public static readonly Vector2 Shortcut = WorldLayout.TileCenter(149,92);
    public bool DoorOpen => ((IPlayerContext)World.Player).HasFlag("forest_sanctum");
    public override void _Ready()
    {
        for(int i=0;i<Stones.Length;i++)
        {
            Vector3 at=ReferenceTerrain3D.Ground(Stones[i]);
            var stone=Rock(at+Vector3.Up*.42f,new(.55f,.85f,.45f),new(.38f,.46f,.39f));
            _stones.Add((Stones[i],"forest_stone_"+i,stone));
        }
        Vector3 door=ReferenceTerrain3D.Ground(WorldLayout.DungeonDoor);
        Rock(door+new Vector3(-1.1f,.9f,0),new(.65f,1.8f,.7f),new(.32f,.38f,.3f));
        Rock(door+new Vector3(1.1f,.9f,0),new(.65f,1.8f,.7f),new(.32f,.38f,.3f));
        Rock(door+new Vector3(0,1.9f,0),new(2.8f,.7f,.8f),new(.36f,.43f,.31f));
        _door=Rock(door+Vector3.Up*.8f,new(1.55f,1.6f,.35f),new(.22f,.29f,.2f));
        _shortcut=Rock(ReferenceTerrain3D.Ground(Shortcut)+Vector3.Up*.25f,new(2.8f,.5f,.55f),new(.32f,.22f,.12f));
        _prompt=new Label3D { Text="", FontSize=32, OutlineSize=6, PixelSize=.01f,
            Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest=true };
        AddChild(_prompt);
        AddChild(new OmniLight3D { Position=ReferenceTerrain3D.Ground(WorldLayout.TileCenter(-74,28))+Vector3.Up*3f,
            LightColor=new(.82f,.75f,.48f),LightEnergy=.65f,OmniRange=16f });
        // Warm lanterns define the small interior's return point and its central arena.
        foreach(var point in new[]{WorldLayout.DungeonArrival,WorldLayout.TileCenter(-84,24),WorldLayout.TileCenter(-64,24)})
        {
            Vector3 at=ReferenceTerrain3D.Ground(point);
            Rock(at+Vector3.Up*.25f,new(.5f,.5f,.5f),new(.3f,.36f,.32f));
            AddChild(new OmniLight3D { Position=at+Vector3.Up, LightColor=new(.65f,.9f,.72f),
                LightEnergy=1.5f, OmniRange=8f });
        }
    }
    private MeshInstance3D Rock(Vector3 at,Vector3 size,Color color)
    {
        var mesh=new MeshInstance3D { Position=at, Mesh=new BoxMesh { Size=size },
            MaterialOverride=new StandardMaterial3D { AlbedoColor=color,Roughness=1f } };
        var body=new StaticBody3D { CollisionLayer=CollisionLayers.World,CollisionMask=0 };
        body.AddChild(new CollisionShape3D { Shape=new BoxShape3D { Size=size } });
        mesh.AddChild(body); AddChild(mesh); body.AddToGroup("art_collision"); return mesh;
    }
    public override void _PhysicsProcess(double delta)
    {
        IPlayerContext player=World.Player;
        _travelCooldown=Mathf.Max(0,_travelCooldown-(float)delta);
        _prompt.Text="";
        if(!player.IsAlive) return;
        bool interact=!player.InputBlocked && Input.IsActionJustPressed(InputSetup.Interact);
        foreach(var (point,flag,stone) in _stones)
        {
            bool active=player.HasFlag(flag);
            if(active && !stone.HasMeta("lit"))
            { stone.SetMeta("lit",true); stone.MaterialOverride=new StandardMaterial3D { AlbedoColor=new(.55f,.85f,.67f),
                EmissionEnabled=true,Emission=new(.12f,.3f,.16f),Roughness=1f }; }
            if(active || player.WorldPosition.DistanceTo(point)>45f) continue;
            Prompt(point,"F 오래된 돌 조사");
            if(interact) { player.SetFlag(flag); World.ShowAnnounce("이끼 아래 문양이 희미하게 빛난다"); SaveSystem.Instance?.Save(player,"forest_stone"); }
        }
        if(!DoorOpen && _stones.TrueForAll(s=>player.HasFlag(s.Flag)))
        { player.SetFlag("forest_sanctum"); SaveSystem.Instance?.Save(player,"forest_sanctum"); }
        _door.Visible=!DoorOpen;
        ((StaticBody3D)_door.GetChild(0)).CollisionLayer=DoorOpen ? 0u : CollisionLayers.World;
        bool shortcutOpen=player.HasFlag("forest_shortcut"); _shortcut.Visible=!shortcutOpen;
        ((StaticBody3D)_shortcut.GetChild(0)).CollisionLayer=shortcutOpen ? 0u : CollisionLayers.World;
        if(!shortcutOpen && player.WorldPosition.DistanceTo(Shortcut)<52f)
        {
            Prompt(Shortcut,"F 쓰러진 통나무 조사");
            if(interact && player.HasFlag("supplies_goblin_village"))
            { player.SetFlag("forest_shortcut"); World.ShowAnnounce("숲길을 정리했다"); SaveSystem.Instance?.Save(player,"forest_shortcut"); }
            else if(interact) World.ShowAnnounce("근처에서 고블린의 발소리가 들린다");
        }
        if(_travelCooldown>0f) return;
        if(DoorOpen && player.WorldPosition.DistanceTo(WorldLayout.DungeonDoor)<48f)
        { Prompt(WorldLayout.DungeonDoor,"F 안으로 들어가기"); if(interact) Travel(WorldLayout.DungeonArrival,"뿌리 아래 성소"); }
        else if(player.WorldPosition.DistanceTo(WorldLayout.DungeonArrival)<48f)
        { Prompt(WorldLayout.DungeonArrival,"F 숲으로 돌아가기"); if(interact) Travel(WorldLayout.DungeonDoor+new Vector2(0,48),"새잎의 숲"); }
        else if(shortcutOpen && player.WorldPosition.DistanceTo(Shortcut)<42f)
        { Prompt(Shortcut,"F 샛길 내려가기"); if(interact) Travel(WorldLayout.TileCenter(143,107),"개울가 샛길"); }
    }
    private void Prompt(Vector2 at,string text) { _prompt.Position=ReferenceTerrain3D.Ground(at)+Vector3.Up*1.5f; _prompt.Text=text; }
    private void Travel(Vector2 to,string name)
    { World.Player.GlobalPosition=to; World.Player.Velocity=Vector2.Zero; _travelCooldown=1f; World.ShowAnnounce(name); }
}
