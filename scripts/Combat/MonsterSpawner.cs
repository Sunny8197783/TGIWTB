using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 사냥터: data/world/spawns.json 의 무리(camps). 주인공 둘레의 무리만 세우고 멀어지면 거둔다 (1km 지도에 무리 80여 개).
/// 쓰러진 몬스터는 respawn 초 뒤 그 무리가 깨어 있으면 다시 선다. 지역 레벨대는 지역 이름 알림이 쓴다(ZoneLevels).
/// </summary>
public partial class MonsterSpawner : Node
{
    private sealed class Camp
    {
        public string[] Ids;
        public Vector2 Center;
        public float Radius, Respawn;
        public Enemy[] Members;
        public bool Awake;
    }

    private readonly List<Camp> _camps = new();
    private float _check;

    /// <summary>지역 이름 → 레벨대 (spawns.json zones.levels)</summary>
    public static readonly Dictionary<string, (int Min, int Max)> ZoneLevels = new();

    public override void _Ready()
    {
        Name = "Monsters";
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/world/spawns.json"), opts);
        var root = doc.RootElement;
        foreach (var zone in root.GetProperty("zones").EnumerateObject())
            if (zone.Value.TryGetProperty("levels", out var lv))
                ZoneLevels[zone.Name] = (lv[0].GetInt32(), lv[1].GetInt32());
        foreach (var c in root.GetProperty("camps").EnumerateArray())
        {
            var ids = new List<string>();
            foreach (var id in c.GetProperty("ids").EnumerateArray())
                ids.Add(id.GetString());
            _camps.Add(new Camp
            {
                Ids = ids.ToArray(),
                Center = new Vector2(c.GetProperty("x").GetSingle(), c.GetProperty("z").GetSingle()),
                Radius = c.GetProperty("radius").GetSingle(),
                Respawn = c.GetProperty("respawn").GetSingle(),
                Members = new Enemy[ids.Count],
            });
        }
    }

    public override void _Process(double delta)
    {
        _check -= (float)delta;
        if (_check > 0f || Hero.Instance == null)
            return;
        _check = T.CampCheckEvery;
        var at = new Vector2(Hero.Instance.GlobalPosition.X, Hero.Instance.GlobalPosition.Z);
        foreach (var camp in _camps)
        {
            float d = at.DistanceTo(camp.Center);
            if (!camp.Awake && d < T.CampWakeRange)
                Wake(camp);
            else if (camp.Awake && d > T.CampSleepRange)
                Sleep(camp);
        }
    }

    private void Wake(Camp camp)
    {
        camp.Awake = true;
        for (int i = 0; i < camp.Ids.Length; i++)
            if (camp.Members[i] == null)
                Spawn(camp, i);
    }

    /// <summary>멀어진 무리는 거둔다 (싸우는 중이면 둔다 — 쫓아오는 적이 사라지지 않게)</summary>
    private static void Sleep(Camp camp)
    {
        foreach (var e in camp.Members)
            if (e != null && IsInstanceValid(e) && e.Engaged)
                return;
        camp.Awake = false;
        for (int i = 0; i < camp.Members.Length; i++)
        {
            if (camp.Members[i] != null && IsInstanceValid(camp.Members[i]))
                camp.Members[i].QueueFree();
            camp.Members[i] = null;
        }
    }

    private void Spawn(Camp camp, int slot)
    {
        var world = GameRoot.Instance.World;
        var def = MonsterDef.Get(camp.Ids[slot]);
        float a = Mathf.Tau * slot / camp.Ids.Length;
        Vector2 at = camp.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * camp.Radius * (camp.Ids.Length == 1 ? 0f : 0.6f);
        var home = new Vector3(at.X, world.WalkHeightAt(at.X, at.Y), at.Y);
        var e = new Enemy(def, home);
        GameRoot.Instance.Stage.AddChild(e);
        e.GlobalPosition = home + Vector3.Up * 0.1f;
        camp.Members[slot] = e;
        e.Died += () =>
        {
            GetTree().CreateTimer(camp.Respawn).Timeout += () =>
            {
                if (camp.Members[slot] == e)
                    camp.Members[slot] = null;
                if (camp.Awake && camp.Members[slot] == null)
                    Spawn(camp, slot);
            };
        };
    }
}
