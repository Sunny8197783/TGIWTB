using System.Text.Json;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;

namespace PixelMmo.Combat;

/// <summary>지도 meta 의 "monsters" 무리를 세우고, 죽으면 respawn 초 뒤 다시 세운다.</summary>
public partial class MonsterSpawner : Node
{
    public override void _Ready()
    {
        Name = "Monsters";
        var world = GameRoot.Instance.World;
        if (!world.Meta.TryGetProperty("monsters", out JsonElement groups))
            return;
        foreach (var g in groups.EnumerateArray())
        {
            var def = MonsterDef.Get(g.GetProperty("id").GetString());
            var center = new Vector2(g.GetProperty("x").GetSingle(), g.GetProperty("z").GetSingle());
            float radius = g.GetProperty("radius").GetSingle();
            float respawn = g.GetProperty("respawn").GetSingle();
            int count = g.GetProperty("count").GetInt32();
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Tau * i / count;
                Spawn(def, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * 0.6f, respawn);
            }
        }
    }

    private void Spawn(MonsterDef def, Vector2 at, float respawn)
    {
        var world = GameRoot.Instance.World;
        var home = new Vector3(at.X, world.HeightAt(at.X, at.Y), at.Y);
        var e = new Enemy(def, home);
        GameRoot.Instance.Stage.AddChild(e);
        e.GlobalPosition = home + Vector3.Up * 0.1f;
        e.Died += () => GetTree().CreateTimer(respawn).Timeout += () => Spawn(def, at, respawn);
    }
}
