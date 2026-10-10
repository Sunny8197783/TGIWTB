using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Combat;
using PixelMmo.World;

namespace PixelMmo.Core;

/// <summary>
/// 환경음 (M4) — data/world/ambience.json. 층(바람·새·귀뚜라미·파도·호수·폭포)을 늘 틀어 두고 음량만 옮긴다.
/// 지역(Scenery.CurrentZone)이 층 세기를 정하고, 새는 낮에·귀뚜라미는 밤에만, 폭포는 거리로.
/// SFX 버스에 태워 완벽 회피 슬로우 때 같이 먹먹해진다.
/// </summary>
public partial class Ambience : Node
{
    private sealed class Layer
    {
        public AudioStreamPlayer Player;
        public float Db;
        public string Time;
        public float Gain;   // 지금 세기 0~1 (목표로 천천히 옮긴다)
    }

    private const float Silent = -80f;
    private readonly Dictionary<string, Layer> _layers = new();
    private readonly Dictionary<string, Dictionary<string, float>> _zones = new();
    private readonly Dictionary<string, float> _default;
    private readonly List<Vector3> _falls = new();
    private readonly float _fallRange, _fade;
    private readonly Scenery _scenery;

    public Ambience(WorldData world, Scenery scenery)
    {
        Name = "Ambience";
        _scenery = scenery;
        var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/world/ambience.json"),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement;
        foreach (var l in doc.GetProperty("layers").EnumerateObject())
        {
            var stream = GD.Load<AudioStream>(l.Value.GetProperty("file").GetString());
            if (stream is AudioStreamWav wav)
            {
                // 합성한 고리는 이음매를 섞어 두었다 — 처음부터 끝까지 그대로 반복
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
            }
            var player = new AudioStreamPlayer { Stream = stream, Bus = "SFX", VolumeDb = Silent };
            AddChild(player);
            _layers[l.Name] = new Layer
            {
                Player = player,
                Db = l.Value.GetProperty("db").GetSingle(),
                Time = l.Value.TryGetProperty("time", out var t) ? t.GetString() : null,
            };
        }
        _default = Gains(doc.GetProperty("default"));
        foreach (var z in doc.GetProperty("zones").EnumerateObject())
            _zones[z.Name] = Gains(z.Value);
        _fallRange = doc.GetProperty("waterfall_range").GetSingle();
        _fade = doc.GetProperty("fade").GetSingle();
        foreach (var f in world.Meta.GetProperty("waterfalls").EnumerateArray())
            _falls.Add(new Vector3(f.GetProperty("x").GetSingle(), 0f, f.GetProperty("top_z").GetSingle()));
    }

    private static Dictionary<string, float> Gains(JsonElement e)
    {
        var d = new Dictionary<string, float>();
        foreach (var p in e.EnumerateObject())
            d[p.Name] = p.Value.GetSingle();
        return d;
    }

    public override void _Ready()
    {
        if (Sfx.Muted || Dev.DevCapture.Disabled().Contains("ambience"))
        {
            SetProcess(false);
            return;
        }
        // 층마다 시작점을 흩어 둔다 — 같은 순간에 새·바람이 함께 시작하면 고리가 티 난다
        foreach (var l in _layers.Values)
            l.Player.Play((float)GD.RandRange(0.0, l.Player.Stream.GetLength()));
    }

    public override void _Process(double delta)
    {
        var hero = Hero.Instance;
        if (hero == null)
            return;
        var zone = _scenery.CurrentZone is string z && _zones.TryGetValue(z, out var g) ? g : _default;
        float night = GameRoot.Instance.DayCycle.Night;
        float fall = 0f;
        foreach (var f in _falls)
        {
            float d = new Vector2(hero.GlobalPosition.X - f.X, hero.GlobalPosition.Z - f.Z).Length();
            fall = Mathf.Max(fall, Mathf.SmoothStep(_fallRange, 0f, d));
        }
        float step = (float)delta / Mathf.Max(_fade, 0.01f);
        foreach (var (name, l) in _layers)
        {
            float target = name == "waterfall" ? fall : zone.GetValueOrDefault(name, 0f);
            if (l.Time == "day") target *= 1f - night;
            else if (l.Time == "night") target *= night;
            l.Gain = Mathf.MoveToward(l.Gain, target, step);
            l.Player.VolumeDb = l.Gain > 0.001f ? l.Db + Mathf.LinearToDb(l.Gain) : Silent;
        }
    }
}
