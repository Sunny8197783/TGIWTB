using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.World;
using T = PixelMmo.Combat.CombatTuning;

namespace PixelMmo.Combat;

/// <summary>
/// 몬스터를 주인공 둘레의 '밀도'로 채운다 (data/world/spawns.json).
/// 지도에 못 박아 둔 무리 대신: 화면 밖 고리에 지역·밤낮·무명지에 맞는 것을 조금씩 세우고, 멀어지면 치운다.
///  - 둘레에 살아 있는 수가 상한(낮·밤·무명지)보다 적을 때만 세운다 → 어디를 가도 비지도 넘치지도 않는다.
///  - 마을·여신상·사람 둘레에는 세우지 않는다. 물·가파른 비탈도 피한다.
///  - 밤에는 무명지 밖에도 무명이 섞여 나온다 (밤에는 이름이 흐려진다).
/// 이름비 사건의 무리는 World/Hush 가 SpawnAt 으로 직접 세운다.
/// </summary>
public partial class MonsterSpawner : Node
{
    public static MonsterSpawner Instance { get; private set; }

    public sealed class Entry
    {
        public string Id { get; set; }
        public float W { get; set; } = 1f;
        /// <summary>[가장 낮은, 가장 높은] 레벨</summary>
        public int[] Lv { get; set; }
        /// <summary>[적게, 많게] 한 번에 세우는 수</summary>
        public int[] Group { get; set; }
        /// <summary>day · night · any</summary>
        public string Time { get; set; } = "any";
        public bool Hushed { get; set; }
    }

    public sealed class SpawnDef
    {
        public float Interval { get; set; } = 1.2f;
        public int CapDay { get; set; } = 6;
        public int CapNight { get; set; } = 9;
        public int CapHush { get; set; } = 8;
        public float CountRadius { get; set; } = 42f;
        public float[] Ring { get; set; } = { 20f, 30f };
        public float Despawn { get; set; } = 52f;
        public List<string> SafeZones { get; set; } = new();
        public float SafeStatue { get; set; } = 16f;
        public float SafeNpc { get; set; } = 12f;
        public float NightHushChance { get; set; } = 0.35f;
        public List<Entry> Night { get; set; } = new();
        public Dictionary<string, List<Entry>> Tables { get; set; } = new();
        public Dictionary<string, List<Entry>> Hush { get; set; } = new();
    }

    private SpawnDef _def;
    private float _timer = 2f;
    private readonly List<Enemy> _ambient = new();
    private readonly List<Vector2> _safeSpots = new();
    private readonly RandomNumberGenerator _rng = new();
    /// <summary>--no=spawn : 캡처에서 몬스터 없이 풍경만</summary>
    private readonly bool _off = Dev.DevCapture.Disabled().Contains("spawn");

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Name = "Monsters";
        _def = JsonSerializer.Deserialize<SpawnDef>(FileAccess.GetFileAsString("res://data/world/spawns.json"),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        var world = GameRoot.Instance.World;
        if (world.Meta.TryGetProperty("statues", out var statues))
            foreach (var s in statues.EnumerateArray())
                _safeSpots.Add(new Vector2(s.GetProperty("x").GetSingle(), s.GetProperty("z").GetSingle()));
    }

    public override void _Process(double delta)
    {
        if (_off)
            return;
        _timer -= (float)delta * GameRoot.Instance.WorldScale;
        if (_timer > 0f)
            return;
        _timer = _def.Interval;
        var hero = Hero.Instance;
        if (hero == null || !hero.IsAlive || GameRoot.Instance.Cinematic)
            return;
        Vector3 at = hero.GlobalPosition;

        // 멀어진 것은 치운다 (싸우는 중인 것은 둔다)
        int near = 0;
        for (int i = _ambient.Count - 1; i >= 0; i--)
        {
            var e = _ambient[i];
            if (!IsInstanceValid(e) || !e.Alive)
            {
                _ambient.RemoveAt(i);
                continue;
            }
            float d = new Vector2(e.GlobalPosition.X - at.X, e.GlobalPosition.Z - at.Z).Length();
            if (d > _def.Despawn && !e.Engaged)
            {
                e.Despawn();
                _ambient.RemoveAt(i);
            }
            else if (d < _def.CountRadius)
                near++;
        }

        bool night = GameRoot.Instance.DayCycle.IsNight;
        bool inHush = World.Hush.Instance?.ActiveZoneAt(at.X, at.Z) != null;
        int cap = inHush ? _def.CapHush : night ? _def.CapNight : _def.CapDay;
        if (near >= cap)
            return;

        for (int tries = 0; tries < 6; tries++)
        {
            float ang = _rng.Randf() * Mathf.Tau;
            float r = _rng.RandfRange(_def.Ring[0], _def.Ring[1]);
            var p = new Vector2(at.X + Mathf.Cos(ang) * r, at.Z + Mathf.Sin(ang) * r);
            if (!Standable(p) || !TrySpawnGroup(p, night, cap - near))
                continue;
            return;
        }
    }

    /// <summary>이 자리에 맞는 표에서 하나 골라 무리로 세운다. 세웠으면 true</summary>
    private bool TrySpawnGroup(Vector2 p, bool night, int room)
    {
        var world = GameRoot.Instance.World;
        string hush = World.Hush.Instance?.ActiveZoneAt(p.X, p.Y);
        List<Entry> table;
        bool forceHushed = false;
        string zone = world.ZoneAt(p.X, p.Y);
        if (hush != null)
        {
            table = _def.Hush.GetValueOrDefault(hush) ?? _def.Hush.GetValueOrDefault("default");
            forceHushed = true;
        }
        else
        {
            if (zone != null && _def.SafeZones.Contains(zone))
                return false;
            table = (zone != null ? _def.Tables.GetValueOrDefault(zone) : null) ?? _def.Tables.GetValueOrDefault("default");
        }
        if (table == null || table.Count == 0)
            return false;

        // 밤: 가끔 그 지역의 표 대신 무명이 나온다 (레벨은 그 지역 표의 범위)
        Entry pick;
        int lo = int.MaxValue, hi = 0;
        foreach (var e in table)
        {
            if (e.Lv is not { Length: 2 })
                continue;
            lo = Mathf.Min(lo, e.Lv[0]);
            hi = Mathf.Max(hi, e.Lv[1]);
        }
        if (lo > hi)
            lo = hi = 1;
        if (night && hush == null && _def.Night.Count > 0 && _rng.Randf() < _def.NightHushChance)
        {
            pick = Weighted(_def.Night, night);
            forceHushed = true;
        }
        else
            pick = Weighted(table, night);
        if (pick == null || !MonsterDef.Exists(pick.Id))
            return false;
        var def = MonsterDef.Get(pick.Id);
        if (!ResourceLoader.Exists(def.Art + "/rot.png"))
            return false;   // 그림이 아직 없다 (생성 중)

        int count = pick.Group is { Length: 2 } ? _rng.RandiRange(pick.Group[0], pick.Group[1]) : 1;
        count = Mathf.Clamp(count, 1, Mathf.Max(1, room));
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var q = i == 0 ? p : p + new Vector2(_rng.RandfRange(-3f, 3f), _rng.RandfRange(-3f, 3f));
            if (i > 0 && !Standable(q))
                continue;
            int level = pick.Lv is { Length: 2 } ? _rng.RandiRange(pick.Lv[0], pick.Lv[1]) : _rng.RandiRange(lo, hi);
            var e = SpawnAt(def, q, level, forceHushed || pick.Hushed);
            e.Ambient = true;
            _ambient.Add(e);
            made++;
        }
        return made > 0;
    }

    private Entry Weighted(List<Entry> table, bool night)
    {
        float total = 0f;
        foreach (var e in table)
            if (TimeOk(e, night))
                total += e.W;
        if (total <= 0f)
            return null;
        float roll = _rng.Randf() * total;
        foreach (var e in table)
        {
            if (!TimeOk(e, night))
                continue;
            roll -= e.W;
            if (roll <= 0f)
                return e;
        }
        return null;
    }

    private static bool TimeOk(Entry e, bool night) => e.Time == "any" || (e.Time == "night") == night;

    /// <summary>설 수 있는 자리인가: 지도 안, 마른 땅, 완만한 비탈, 마을·여신상·사람에게서 떨어진 곳</summary>
    private bool Standable(Vector2 p)
    {
        var world = GameRoot.Instance.World;
        if (p.X < 6f || p.Y < 6f || p.X > world.Width - 6f || p.Y > world.Height - 6f)
            return false;
        float h = world.HeightAt(p.X, p.Y);
        if (world.WaterAt(p.X, p.Y) > h + 0.05f)
            return false;
        if (Mathf.Abs(world.HeightAt(p.X + 1.5f, p.Y) - world.HeightAt(p.X - 1.5f, p.Y)) > T.MaxSlope * 1.5f
            || Mathf.Abs(world.HeightAt(p.X, p.Y + 1.5f) - world.HeightAt(p.X, p.Y - 1.5f)) > T.MaxSlope * 1.5f)
            return false;
        string zone = world.ZoneAt(p.X, p.Y);
        if (zone != null && _def.SafeZones.Contains(zone))
            return false;
        foreach (var s in _safeSpots)
            if (s.DistanceTo(p) < _def.SafeStatue)
                return false;
        foreach (var n in NpcDef.All)
            if (new Vector2(n.X, n.Z).DistanceTo(p) < _def.SafeNpc)
                return false;
        return true;
    }

    /// <summary>몬스터 하나를 세운다 (이름비 사건·이야기가 직접 부를 때도 쓴다)</summary>
    public static Enemy SpawnAt(MonsterDef def, Vector2 at, int level, bool hushed)
    {
        var world = GameRoot.Instance.World;
        var home = new Vector3(at.X, world.WalkHeightAt(at.X, at.Y), at.Y);
        var e = new Enemy(def, home, level, hushed);
        GameRoot.Instance.Stage.AddChild(e);
        e.GlobalPosition = home + Vector3.Up * 0.1f;
        Render.Dust.Puff(home, 6, Vector3.Zero);
        return e;
    }
}
