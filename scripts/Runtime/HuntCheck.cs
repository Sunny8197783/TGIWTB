using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>Opt-in real-time input soak. Authored roads, normal damage/death, no teleport or healing.</summary>
public partial class HuntCheck : Node
{
    internal static bool Requested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--hunt-check") >= 0;
    internal static bool Resume => Array.IndexOf(OS.GetCmdlineUserArgs(), "--hunt-resume") >= 0;
    internal static bool Passed { get; private set; }
    private static readonly string[] SlimeCamps={"slime_reeds","slime_basin","slime_grove"};
    private static readonly string[] GoblinCamps={"goblin_gate","goblin_homes","goblin_workyard"};
    private static readonly string[] Actions={InputSetup.MoveUp,InputSetup.MoveDown,InputSetup.MoveLeft,InputSetup.MoveRight,
        InputSetup.Attack,InputSetup.Dash,InputSetup.Run,InputSetup.Guard};
    private GameWorld _world;
    private IPlayerContext _player;
    private readonly AStar2D _roads = new();
    private readonly Dictionary<Vector2I,long> _roadIds = new();
    private Vector2[] _path = Array.Empty<Vector2>();
    private int _step, _slimes, _goblins, _campIndex;
    private Vector2 _goal, _lastPosition;
    private double _time, _nextLog, _nextAttack, _nextDodge, _waiting, _stuck;
    private ulong _start;
    private string _phase = "";
    private bool _saved, _evolved, _visitedSlime, _visitedGoblin;

    public override void _Ready()
    {
        _world = GetParent() as GameWorld;
        Passed=false;
        _player = _world.Player;
        ProcessPhysicsPriority = -100;
        foreach (var route in MeadowLayout.Data.Routes)
            for (int i=1;i<route.Length;i++) {
                Vector2 a=WorldLayout.TileCenter(route[i-1][0],route[i-1][1]);
                Vector2 b=WorldLayout.TileCenter(route[i][0],route[i][1]);
                int count=Mathf.CeilToInt(a.DistanceTo(b)/48f);
                long previous=RoadPoint(a);
                for(int n=1;n<=count;n++) {
                    long id=RoadPoint(a.Lerp(b,n/(float)count));
                    if(id!=previous && !_roads.ArePointsConnected(previous,id)) _roads.ConnectPoints(previous,id);
                    previous=id;
                }
            }
        foreach(Node node in GetTree().GetNodesInGroup(MonsterBase.Group)) Watch(node);
        GetTree().NodeAdded += Watch;
        _lastPosition=_player.WorldPosition;
        GD.Print($"[Hunt] ready resume={Resume} input-only=true teleport=false heal=false roads={_roadIds.Count}");
        if(Resume) {
            var expected=SaveSystem.Instance.Load();var actual=_player.CaptureSave();
            bool valid=expected!=null && _player.HasFlag("hunt_check_saved") && actual.Mastery.Count>0
                && expected.Level==actual.Level && Mathf.IsEqualApprox(expected.Hp,actual.Hp)
                && expected.Stamina==actual.Stamina
                && new Vector2(expected.Position.X,expected.Position.Y).DistanceTo(_player.WorldPosition)<.1f
                && expected.Mastery.OrderBy(p=>p.Key).SequenceEqual(actual.Mastery.OrderBy(p=>p.Key))
                && expected.BaseStats.OrderBy(p=>p.Key).SequenceEqual(actual.BaseStats.OrderBy(p=>p.Key))
                && expected.Counters.OrderBy(p=>p.Key).SequenceEqual(actual.Counters.OrderBy(p=>p.Key))
                && expected.Flags.OrderBy(p=>p).SequenceEqual(actual.Flags.OrderBy(p=>p))
                && JsonSerializer.Serialize(expected.JobState)==JsonSerializer.Serialize(actual.JobState)
                && JsonSerializer.Serialize(expected.Appearance)==JsonSerializer.Serialize(actual.Appearance);
            Passed=valid;
            GD.Print($"[HuntResume] {(valid?"PASS":"FAIL")} hp={_player.Hp:0} deaths={_player.GetCounter("deaths")} mastery={Mastery()} position={_player.WorldPosition}");
            if(!valid) GetTree().Quit(1);
        }
    }

    private long RoadPoint(Vector2 point)
    {
        Vector2I key=new(Mathf.RoundToInt(point.X),Mathf.RoundToInt(point.Y));
        if(_roadIds.TryGetValue(key,out long id)) return id;
        id=_roadIds.Count; _roadIds.Add(key,id); _roads.AddPoint(id,point); return id;
    }
    private void Watch(Node node)
    {
        if(node is MonsterBase mob) mob.Died += OnDeath;
    }
    private void OnDeath(MonsterBase mob) { if(mob is Slime) _slimes++; else if(mob is GoblinArcher) _goblins++; }
    public override void _ExitTree() { GetTree().NodeAdded-=Watch; _roads.Dispose(); Release(); }
    private static void Release()
    {
        foreach(string action in Actions) Input.ActionRelease(action);
    }
    private string Mastery() => string.Join(",", _player.Mastery.All());
    public override void _PhysicsProcess(double delta)
    {
        Release();
        if(ReferenceWorld3D.Instance?.CanCheckStanding != true) return;
        if(_start==0) _start=Time.GetTicksUsec();
        _time=(Time.GetTicksUsec()-_start)/1000000.0;
        if(Resume || _saved) return;
        string phase=_time<900?"slime":_time<1500?"goblin":"return";
        if(phase!=_phase) { _phase=phase;_campIndex=0;_path=Array.Empty<Vector2>();GD.Print($"[Hunt] phase={phase} t={_time:0.0}"); }
        string campId=(phase=="slime" ? SlimeCamps : GoblinCamps)[_campIndex%3];
        Vector2 camp=MeadowLayout.Data.Camps.Find(c=>c.Id==campId).Center;
        float distance=_player.WorldPosition.DistanceTo(camp);
        if(distance<180) { if(phase=="slime") _visitedSlime=true; else if(phase=="goblin") _visitedGoblin=true; }
        if(!_evolved && _player.Jobs.LearnedSkills.Contains("sk_slash_heavy")) { _evolved=true;GD.Print($"[Hunt] first-evolution t={_time:0.0}s"); }
        if(_time>=_nextLog) {
            _nextLog=_time+30;
            GD.Print($"[Hunt] t={_time:0.0} phase={phase} pos={_player.WorldPosition} hp={_player.Hp:0.0} stamina={_player.Stamina:0} kills={_slimes}/{_goblins} deaths={_player.GetCounter("deaths")} wait={_waiting:0.0} mastery={Mastery()}");
        }
        if(_time>=1800) {
            bool home=_player.WorldPosition.DistanceTo(WorldLayout.SpawnPoint)<100;
            _player.SetFlag("hunt_check_saved");
            bool saved=SaveSystem.Instance.Save(_player,"hunt-check");
            Passed=home && _player.IsAlive && _visitedSlime && _visitedGoblin && _slimes>0 && _goblins>0 && saved;
            GD.Print($"[HuntResult] {(Passed?"PASS":"FAIL")} duration={_time:0.0} visited={_visitedSlime}/{_visitedGoblin} home={home} alive={_player.IsAlive} kills={_slimes}/{_goblins} deaths={_player.GetCounter("deaths")} evolved={_evolved} saved={saved}");
            _saved=true; return;
        }
        if(!_player.IsAlive) { _path=Array.Empty<Vector2>();return; }
        if(phase=="return") { WalkTo(WorldLayout.SpawnPoint);return; }
        MonsterBase nearest=null;float best=distance<200?260f:90f;
        foreach(Node node in GetTree().GetNodesInGroup(MonsterBase.Group))
            if(node is MonsterBase mob && mob is not TrainingDummy && mob.IsAlive) {
                float d=mob.GlobalPosition.DistanceTo(_player.WorldPosition);
                if(d<best && ReferenceWorld3D.Instance.ClearSight(_player.WorldPosition,mob.GlobalPosition)) {nearest=mob;best=d;}
            }
        if(nearest!=null) {
            Vector2 direction=nearest.GlobalPosition-_player.WorldPosition;
            if(best>20f) Move(direction, best>60f);
            if(best<36f && _time>=_nextAttack) { Input.ActionPress(InputSetup.Attack);_nextAttack=_time+.38; }
            if(nearest.ArtClip=="attack" && best<80 && _time>=_nextDodge && _player.Stamina>=24) {
                Release();
                Move(direction.Orthogonal(),false);Input.ActionPress(InputSetup.Dash);_nextDodge=_time+1.2;
            }
            _path=Array.Empty<Vector2>();
        } else {
            if(distance<180) { _waiting+=delta;_campIndex++;_path=Array.Empty<Vector2>();return; }
            WalkTo(camp);
        }
        if(_player.WorldPosition.DistanceTo(_lastPosition)<.15f && (nearest==null ? distance>70 : best>50)) _stuck+=delta;
        else _stuck=0;
        _lastPosition=_player.WorldPosition;
        if(_stuck>3) { GD.Print($"[Hunt] blocked pos={_player.WorldPosition} goal={_goal}");_path=Array.Empty<Vector2>();_stuck=0; }
    }
    private void WalkTo(Vector2 destination)
    {
        Vector2 here=_player.WorldPosition;
        if(here.DistanceTo(destination)<12) return;
        if(_path.Length==0 || _goal!=destination) {
            _goal=destination;_step=0;
            _path=_roads.GetPointPath(_roads.GetClosestPoint(here),_roads.GetClosestPoint(destination));
        }
        while(_step<_path.Length && here.DistanceTo(_path[_step])<10) _step++;
        Vector2 target=_step<_path.Length?_path[_step]:destination;
        Move(target-here,true);
    }
    private static void Move(Vector2 direction,bool run)
    {
        if(direction.X>3) Input.ActionPress(InputSetup.MoveRight);else if(direction.X< -3) Input.ActionPress(InputSetup.MoveLeft);
        if(direction.Y>3) Input.ActionPress(InputSetup.MoveDown);else if(direction.Y< -3) Input.ActionPress(InputSetup.MoveUp);
        if(run) Input.ActionPress(InputSetup.Run);
    }
}
