using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

public sealed class EncounterDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Slimes { get; set; }
    public int Goblins { get; set; }
    public float Respawn { get; set; }
    public float RewardRatio { get; set; }
    public bool Continuous { get; set; }
    public Vector2 Center => WorldLayout.TileCenter(X, Y);
}

public sealed class EncounterLayout
{
    public List<EncounterDefinition> Camps { get; set; } = new();
    public List<int[][]> Routes { get; set; } = new();
}

/// <summary>Authored encounter spaces and paths share one layout with spawning and rewards.</summary>
public static class MeadowLayout
{
    public static EncounterLayout Data { get; } = JsonSerializer.Deserialize<EncounterLayout>(
        FileAccess.GetFileAsString("res://data/encounters/meadow.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    public static bool Reserved(Vector2 point)
    {
        foreach (var stone in ForestSecrets.Stones)
            if (point.DistanceTo(stone) < 100f) return true;
        if (point.DistanceTo(ForestSecrets.Shortcut) < 100f) return true;
        foreach (var camp in Data.Camps)
            if (point.DistanceTo(camp.Center) <= Mathf.Max(140f, (camp.Goblins-1)*32.5f+45f)) return true;
        foreach (var route in Data.Routes)
            for (int i = 1; i < route.Length; i++)
            {
                var a = WorldLayout.TileCenter(route[i - 1][0], route[i - 1][1]);
                var b = WorldLayout.TileCenter(route[i][0], route[i][1]);
                if (Geometry2D.GetClosestPointToSegment(point, a, b).DistanceTo(point) <= 65f) return true;
            }
        return false;
    }
}

public partial class MeadowEncounters : Node2D
{
    private sealed class Camp
    {
        public EncounterDefinition Def;
        public readonly List<MonsterBase> Mobs = new();
        public float Cooldown;
        public bool Cleared;
        public bool Announced;
        public bool HasSpawned;
    }
    private readonly List<Camp> _camps = new();
    private GameWorld _world;
    private float _redraw;
    public bool SupplyReady(string id) => _camps.Exists(c => c.Def.Id == id && c.Cleared);

    public override void _Ready()
    {
        _world = GetParent() as GameWorld;
        YSortEnabled = true;
        foreach (var def in MeadowLayout.Data.Camps)
        {
            var camp = new Camp { Def = def };
            _camps.Add(camp);
            Spawn(camp);
        }
    }

    private void Spawn(Camp camp)
    {
        camp.Mobs.Clear();
        int count = camp.Def.Slimes + camp.Def.Goblins;
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = i < camp.Def.Slimes
                ? new Vector2(-25f, (i - (camp.Def.Slimes - 1) * .5f) * 42f)
                : new Vector2(65f, (i - camp.Def.Slimes - (camp.Def.Goblins - 1) * .5f) * 65f);
            Vector2 position = camp.Def.Center + offset;
            if (!_world.Tiles.IsWalkable(position))
            { GD.PushError($"[Encounter] blocked spawn {camp.Def.Id}"); continue; }
            bool slime = i < camp.Def.Slimes;
            if (camp.Def.Continuous)
            {
                _world.Spawner.AddFixedSlot(() => slime ? new Slime() : new GoblinArcher
                    { PatrolRoute = new[] { position, position + new Vector2(-48, 0), position + new Vector2(0, 32) } },
                    position, camp.Def.Respawn);
                continue;
            }
            MonsterBase mob = slime ? new Slime() : new GoblinArcher();
            mob.Position = position;
            AddChild(mob);
            camp.Mobs.Add(mob);
        }
        camp.HasSpawned = camp.Mobs.Count == count;
        camp.Cleared = false;
        camp.Cooldown = camp.Def.Respawn;
    }

    public override void _PhysicsProcess(double delta)
    {
        IPlayerContext player = _world.Player;
        foreach (var camp in _camps)
        {
            float distance = player.WorldPosition.DistanceTo(camp.Def.Center);
            if (!camp.Announced && distance < 180f)
            { camp.Announced = true; _world.ShowAnnounce(camp.Def.Name); }
            if (camp.Def.Continuous) continue;
            bool alive = camp.Mobs.Exists(m => IsInstanceValid(m) && m.IsAlive);
            if (camp.HasSpawned && !alive && !camp.Cleared)
            {
                camp.Cleared = true;
                if (distance < 420f) _world.ShowAnnounce(player.HasFlag("supplies_" + camp.Def.Id)
                    ? $"{camp.Def.Name} 확보" : $"{camp.Def.Name} 확보 — 보급품을 조사하세요");
            }
            if (!camp.Cleared) continue;
            string flag = "supplies_" + camp.Def.Id;
            Vector2 supply = camp.Def.Center + new Vector2(0f, 80f);
            if (player.IsAlive && !player.InputBlocked && !player.HasFlag(flag) && player.WorldPosition.DistanceTo(supply) < 42f
                && Input.IsActionJustPressed(InputSetup.Interact))
            {
                player.RestoreSupplies(camp.Def.RewardRatio);
                player.SetFlag(flag);
                _world.ShowAnnounce("보급품 확보 — 체력과 마나 회복");
                SaveSystem.Instance?.Save(player, "field_supplies");
            }
            // An unclaimed reward cannot disappear while the player is approaching it.
            if (!player.HasFlag(flag)) continue;
            camp.Cooldown -= (float)delta;
            if (camp.Cooldown <= 0f && distance > 420f) Spawn(camp);
        }
        _redraw -= (float)delta;
        if (_redraw <= 0f) { _redraw = .1f; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (_world == null) return;
        IPlayerContext player = _world.Player;
        foreach (var camp in _camps)
        {
            if (camp.Def.Continuous || player.WorldPosition.DistanceTo(camp.Def.Center) > 450f) continue;
            Vector2 at = camp.Def.Center + new Vector2(0f, 80f);
            bool claimed = player.HasFlag("supplies_" + camp.Def.Id);
            Color color = claimed ? new Color(.38f, .34f, .25f) : new Color(.75f, .54f, .23f);
            DrawRect(new Rect2(at - new Vector2(9f, 5f), new Vector2(18f, 12f)), new Color(.12f, .1f, .08f));
            DrawRect(new Rect2(at - new Vector2(8f, 4f), new Vector2(16f, 10f)), color);
            DrawRect(new Rect2(at - new Vector2(1f, 3f), new Vector2(2f, 8f)), new Color(.85f, .77f, .5f));
            if (camp.Cleared && !claimed)
            {
                DrawArc(at, 16f, 0f, Mathf.Tau, 24, new Color(.95f, .85f, .5f, .8f), 1f);
                DrawString(ThemeDB.FallbackFont, at + new Vector2(-15f, -12f), "F 보급", fontSize: 8);
            }
        }
    }
}
