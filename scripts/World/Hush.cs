using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Data;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 무명지와 이름비 (data/world/hush.json, 설계 docs/design/NAMELESS.md).
/// 이름이 지워진 땅은 색이 없다 — 256x256 지도(4m = 1텍셀)에 세기를 칠해 셰이더 전역 hush_map 으로 넘긴다.
/// 한가운데 이름비에 손을 얹으면(F) 무명이 몰려오고, 모두 물리치면 색이 이름비에서 바깥으로 물결처럼 돌아온다.
/// 깨운 무명지는 세이브 값 hush:&lt;id&gt; = 1, 행적 stele. 이야기(Story)는 Restored·BossSpawned 사건을 듣는다.
/// </summary>
public partial class Hush : Control
{
    public static Hush Instance { get; private set; }

    public sealed class WaveEntry
    {
        public string Id { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class ZoneDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        public float Radius { get; set; } = 36f;
        public int Level { get; set; } = 1;
        public List<List<WaveEntry>> Waves { get; set; } = new();
        /// <summary>무리를 다 물리친 뒤 나타나는 우두머리 (monster id)</summary>
        public string Boss { get; set; }
        public int Exp { get; set; }
        public int Shards { get; set; }
        /// <summary>깨우려면 먼저 서 있어야 하는 이야기 표시 (없으면 언제든)</summary>
        public string Needs { get; set; }
        /// <summary>깨어나는 순간 한 줄</summary>
        public string Line { get; set; }
    }

    private sealed class Zone
    {
        public ZoneDef Def;
        public bool Restored;
        public float Front = -1f;     // 색이 돌아오는 물결의 반지름 (m). 음수면 아직
        public Vector3 Stele;
        public WorldSprite Sprite;
        public OmniLight3D Light;
    }

    private enum Phase { None, Fight, Boss, Restore }

    private const int MapSize = 256;
    private const float EdgeSoft = 6f;         // 가장자리가 풀리는 폭 (m)
    private const float Reach = 3.2f;          // 이름비에 손이 닿는 거리 (m)
    private const float RestoreTime = 5.5f;    // 색이 다 돌아오는 시간 (초, 330f)
    private const float WaveGap = 1.4f;        // 무리 사이 숨 고르기 (초)
    private static readonly Color Gold = new(1f, 0.9f, 0.55f);
    private static readonly Color Back = new(0.06f, 0.05f, 0.09f, 0.86f);
    private static readonly Color Dim = new(0.72f, 0.68f, 0.62f);

    private readonly WorldData _world;
    private readonly List<Zone> _zones = new();
    private readonly List<Enemy> _alive = new();
    private readonly RandomNumberGenerator _rng = new();
    private IPlayerContext _player;
    private Image _image;
    private ImageTexture _tex;
    private readonly byte[] _pixels = new byte[MapSize * MapSize];
    private Zone _event;
    private Phase _phase;
    private int _wave;
    private float _gap;
    private float _restoreT;
    private int _near = -1;
    private float _age;
    private float _pitch0, _dist0;
    private Vector3 _offset0;
    private Enemy _boss;

    /// <summary>이름비가 깨어났다 (무명지 id)</summary>
    public event Action<string> Restored;
    /// <summary>이름비 싸움의 우두머리가 나타났다 (이야기가 연출을 건다)</summary>
    public event Action<string, Enemy> BossSpawned;
    /// <summary>이름비 싸움이 시작됐다 / 실패로 물러났다</summary>
    public event Action<string> EventStarted;
    public event Action<string> EventFailed;

    /// <summary>이름비 싸움 중인 무명지 id (없으면 null)와 진행 글 — HUD 가 띄운다</summary>
    public string EventZone => _event?.Def.Id;
    public string EventText { get; private set; }
    public Enemy Boss => _boss != null && IsInstanceValid(_boss) && _boss.Alive ? _boss : null;

    public Hush(WorldData world)
    {
        Name = "Hush";
        _world = world;
        Instance = this;
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var defs = JsonSerializer.Deserialize<List<ZoneDef>>(FileAccess.GetFileAsString("res://data/world/hush.json"), opts);
        foreach (var d in defs)
            _zones.Add(new Zone { Def = d, Stele = new Vector3(d.X, world.WalkHeightAt(d.X, d.Z), d.Z) });
        _image = Image.CreateEmpty(MapSize, MapSize, false, Image.Format.R8);
        _tex = ImageTexture.CreateFromImage(_image);
        // 처음부터 넣어 둔다 — 빈 전역 표본은 흰색으로 읽혀 온 세상이 잿빛이 된다
        RenderingServer.GlobalShaderParameterSet("hush_map", _tex);
    }

    public Hush() { }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        var stage = GameRoot.Instance.Stage;
        var art = GD.Load<Texture2D>("res://art/env/name_stele_0.png");
        foreach (var z in _zones)
        {
            z.Sprite = new WorldSprite(art);
            stage.AddChild(z.Sprite);
            z.Sprite.GlobalPosition = z.Stele;
            z.Light = new OmniLight3D
            {
                LightColor = Gold, LightEnergy = 0f, OmniRange = 9f, ShadowEnabled = false,
                Position = z.Stele + Vector3.Up * 1.8f,
            };
            stage.AddChild(z.Light);
            // 지나가다 몸이 겹치지 않게
            var body = new StaticBody3D { Name = "Stele_" + z.Def.Id };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.7f, Height = 3f }, Position = Vector3.Up * 1.5f });
            stage.AddChild(body);
            body.GlobalPosition = z.Stele;
        }
    }

    public void Bind(IPlayerContext player)
    {
        _player = player;
        foreach (var z in _zones)
        {
            z.Restored = player.HasFlag("hush:" + z.Def.Id) || Dev.DevCapture.Disabled().Contains("hush");
            Glow(z, z.Restored ? 1f : 0f);
        }
        Paint();
    }

    /// <summary>이 자리를 덮은, 아직 깨우지 않은 무명지 id (없으면 null)</summary>
    public string ActiveZoneAt(float x, float z)
    {
        foreach (var zone in _zones)
        {
            if (zone.Restored)
                continue;
            float d = new Vector2(x - zone.Def.X, z - zone.Def.Z).Length();
            if (d < zone.Def.Radius && d > zone.Front)
                return zone.Def.Id;
        }
        return null;
    }

    public string ZoneName(string id) => _zones.Find(z => z.Def.Id == id)?.Def.Name;
    public bool IsRestored(string id) => _zones.Find(z => z.Def.Id == id)?.Restored ?? false;

    /// <summary>지도·길잡이용: (id, 이름, 자리, 반지름, 깨웠나)</summary>
    public IEnumerable<(string Id, string Name, Vector2 At, float Radius, bool Restored)> All()
    {
        foreach (var z in _zones)
            yield return (z.Def.Id, z.Def.Name, new Vector2(z.Def.X, z.Def.Z), z.Def.Radius, z.Restored);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_player == null)
            return;
        Vector3 at = _player.WorldPosition;
        switch (_phase)
        {
            case Phase.None:
                FindNear(at);
                if (_near >= 0 && _player.IsAlive && !GameRoot.Instance.InputLocked && Input.IsActionJustPressed(Controls.Interact))
                    Begin(_zones[_near]);
                break;
            case Phase.Fight:
            case Phase.Boss:
                TickFight(dt, at);
                break;
            case Phase.Restore:
                TickRestore(dt);
                break;
        }
        QueueRedraw();
    }

    private void FindNear(Vector3 at)
    {
        _near = -1;
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (z.Restored)
                continue;
            if (new Vector2(at.X - z.Stele.X, at.Z - z.Stele.Z).Length() < Reach)
                _near = i;
        }
    }

    // ── 이름비 싸움 ─────────────────────────────────────

    private void Begin(Zone z)
    {
        if (z.Def.Needs != null && !_player.HasFlag(z.Def.Needs))
        {
            Hud.Announce("이름비가 대답하지 않는다", "아직 때가 아니다", Dim);
            Sfx.Play("error", -10f, 0.8f, 0f, "UI");
            return;
        }
        _event = z;
        _phase = Phase.Fight;
        _wave = -1;
        _gap = 0.8f;
        _alive.Clear();
        _boss = null;
        CombatFx.Shockwave(z.Stele, 5f, 0.7f, CombatFx.PaletteOf("hush"));
        CombatFx.Flash(new Color(0.8f, 0.82f, 0.9f), 0.35f);
        CombatFx.Shake(0.3f);
        Sfx.Play("impactBell_heavy", -3f, 0.5f, 0f, "UI");
        Hud.Announce("이름비에 손을 얹었다", "무명이 몰려온다 — 모두 물리쳐라", new Color(0.86f, 0.88f, 0.95f));
        EventText = "무명을 물리쳐라";
        EventStarted?.Invoke(z.Def.Id);
    }

    private void TickFight(float dt, Vector3 at)
    {
        var z = _event;
        // 쓰러지거나 멀리 달아나면 물러난 것 — 남은 무명은 흩어지고 처음부터 다시
        float away = new Vector2(at.X - z.Stele.X, at.Z - z.Stele.Z).Length();
        if (!_player.IsAlive || away > z.Def.Radius + 14f)
        {
            Fail();
            return;
        }
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.Alive);
        if (_alive.Count > 0)
        {
            EventText = _phase == Phase.Boss ? null : $"무명을 물리쳐라  {_wave + 1}/{z.Def.Waves.Count}  (남은 {_alive.Count})";
            return;
        }
        _gap -= dt;
        if (_gap > 0f)
            return;
        if (_phase == Phase.Boss)
        {
            StartRestore();
            return;
        }
        _wave++;
        if (_wave < z.Def.Waves.Count)
        {
            SpawnWave(z, z.Def.Waves[_wave]);
            _gap = WaveGap;
            if (_wave > 0)
                Hud.Announce($"무명이 다시 몰려온다  {_wave + 1}/{z.Def.Waves.Count}", null, new Color(0.86f, 0.88f, 0.95f));
            return;
        }
        if (z.Def.Boss != null && MonsterDef.Exists(z.Def.Boss))
        {
            _phase = Phase.Boss;
            var def = MonsterDef.Get(z.Def.Boss);
            Vector2 p = SpotAround(z, 9f, 11f);
            _boss = MonsterSpawner.SpawnAt(def, p, z.Def.Level, hushed: true);
            _alive.Add(_boss);
            _gap = WaveGap;
            EventText = null;
            CombatFx.Shake(0.6f);
            CombatFx.Flash(new Color(0.85f, 0.87f, 0.95f), 0.5f);
            Sfx.Play("impactBell_heavy", 0f, 0.4f, 0f, "UI");
            Hud.Announce(def.Name, def.Title, new Color(0.9f, 0.92f, 1f));
            BossSpawned?.Invoke(z.Def.Id, _boss);
            return;
        }
        StartRestore();
    }

    private void SpawnWave(Zone z, List<WaveEntry> wave)
    {
        foreach (var w in wave)
        {
            if (!MonsterDef.Exists(w.Id))
                continue;
            var def = MonsterDef.Get(w.Id);
            if (!ResourceLoader.Exists(def.Art + "/rot.png"))
                def = MonsterDef.Get("hush_shade");   // 그림이 아직 없는 종은 그림자로 대신
            for (int i = 0; i < w.Count; i++)
                _alive.Add(MonsterSpawner.SpawnAt(def, SpotAround(z, 5.5f, 9.5f), z.Def.Level, hushed: true));
        }
        CombatFx.Shockwave(z.Stele, 9f, 0.6f, CombatFx.PaletteOf("hush"));
        Sfx.Play("impactSoft_heavy", -2f, 0.6f);
    }

    /// <summary>이름비 둘레의 설 수 있는 자리 (물·가파른 곳을 피해 몇 번 다시 고른다)</summary>
    private Vector2 SpotAround(Zone z, float r0, float r1)
    {
        var p = new Vector2(z.Stele.X, z.Stele.Z + r0);
        for (int tries = 0; tries < 12; tries++)
        {
            float a = _rng.Randf() * Mathf.Tau;
            float r = _rng.RandfRange(r0, r1);
            p = new Vector2(z.Stele.X + Mathf.Cos(a) * r, z.Stele.Z + Mathf.Sin(a) * r);
            float h = _world.HeightAt(p.X, p.Y);
            if (_world.WaterAt(p.X, p.Y) <= h + 0.05f && Mathf.Abs(h - z.Stele.Y) < 3f)
                break;
        }
        return p;
    }

    private void Fail()
    {
        foreach (var e in _alive)
            if (IsInstanceValid(e) && e.Alive)
                e.Despawn();
        _alive.Clear();
        string id = _event.Def.Id;
        _event = null;
        _boss = null;
        _phase = Phase.None;
        EventText = null;
        Hud.Announce("이름비가 다시 잠들었다", null, Dim);
        EventFailed?.Invoke(id);
    }

    // ── 색이 돌아온다 ───────────────────────────────────

    private void StartRestore()
    {
        _phase = Phase.Restore;
        _restoreT = 0f;
        EventText = null;
        var root = GameRoot.Instance;
        root.Cinematic = true;
        var rig = root.Rig;
        _pitch0 = rig.Pitch;
        _dist0 = rig.Distance;
        _offset0 = rig.Offset;
        // 고개를 들고 물러서서 색이 퍼져 나가는 것을 본다
        rig.Pitch = 16f;
        rig.Distance = 30f;
        rig.Offset = new Vector3(0f, 3f, -9f);
        var z = _event;
        var pal = CombatFx.PaletteOf("lantern");
        CombatFx.Flash(Colors.White, 0.8f);
        CombatFx.Shockwave(z.Stele, 12f, 1.2f, pal);
        CombatFx.SparkleBurst(z.Stele + Vector3.Up * 2f, 80, 3.5f, pal);
        Sfx.Play("impactBell_heavy", 0f, 0.8f, 0f, "UI");
        Sfx.Play("confirmation", -4f, 0.7f, 0f, "UI");
        Hud.Announce($"「{z.Def.Name}」의 이름이 돌아왔다", z.Def.Line ?? "색이 물결처럼 번져 나간다", Gold);
    }

    private void TickRestore(float dt)
    {
        var z = _event;
        _restoreT += dt;
        float k = Mathf.Clamp(_restoreT / RestoreTime, 0f, 1f);
        // 처음엔 천천히, 갈수록 빠르게 번진다
        z.Front = (z.Def.Radius + EdgeSoft * 2f) * k * k * (3f - 2f * k);
        Glow(z, Mathf.Min(1f, _restoreT / 1.2f));
        Paint();
        // 물결이 지나가는 박자마다 종소리·반짝임
        if (Mathf.PosMod(_restoreT, 1.1f) < dt)
        {
            var pal = CombatFx.PaletteOf("lantern");
            CombatFx.Shockwave(z.Stele, Mathf.Max(4f, z.Front), 0.9f, pal);
            CombatFx.SparkleBurst(z.Stele + Vector3.Up * 2.2f, 30, 2.5f, pal);
            Sfx.Play("impactBell_heavy", -8f, 1.3f + k * 0.5f, 0f, "UI");
        }
        if (_restoreT < RestoreTime + 0.8f)
            return;
        z.Restored = true;
        z.Front = -1f;
        Paint();
        var root = GameRoot.Instance;
        root.Rig.Pitch = _pitch0;
        root.Rig.Distance = _dist0;
        root.Rig.Offset = _offset0;
        root.Cinematic = false;
        _phase = Phase.None;
        _event = null;
        _boss = null;
        _player.SetVar("hush:" + z.Def.Id, 1);
        _player.CountDeed("stele");
        _player.AddShards(z.Def.Shards);
        if (z.Def.Shards > 0)
            Hud.Float(_player.WorldPosition + Vector3.Up * 2.6f, $"+{z.Def.Shards} 이름 조각", Gold);
        _player.GrantExp(z.Def.Exp);
        _player.Save();
        Restored?.Invoke(z.Def.Id);
    }

    private static void Glow(Zone z, float k)
    {
        z.Sprite?.SetFlash(0.32f * k, Gold);
        if (z.Light != null)
            z.Light.LightEnergy = 2.2f * k;
    }

    /// <summary>무명지 지도를 다시 칠한다: 깨우지 않은 원 안은 1, 돌아오는 물결 안쪽은 0</summary>
    private void Paint()
    {
        Array.Clear(_pixels);
        float cell = _world.Width / (float)MapSize;
        foreach (var z in _zones)
        {
            if (z.Restored)
                continue;
            float r = z.Def.Radius;
            int x0 = Mathf.Max(0, (int)((z.Def.X - r - EdgeSoft) / cell)), x1 = Mathf.Min(MapSize - 1, (int)((z.Def.X + r + EdgeSoft) / cell) + 1);
            int y0 = Mathf.Max(0, (int)((z.Def.Z - r - EdgeSoft) / cell)), y1 = Mathf.Min(MapSize - 1, (int)((z.Def.Z + r + EdgeSoft) / cell) + 1);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float d = new Vector2((x + 0.5f) * cell - z.Def.X, (y + 0.5f) * cell - z.Def.Z).Length();
                    float v = Mathf.SmoothStep(r + EdgeSoft, r - EdgeSoft, d);
                    if (z.Front >= 0f)
                        v *= Mathf.SmoothStep(z.Front - EdgeSoft, z.Front + EdgeSoft, d);
                    byte b = (byte)Mathf.RoundToInt(v * 255f);
                    int i = y * MapSize + x;
                    if (b > _pixels[i])
                        _pixels[i] = b;
                }
            }
        }
        _image.SetData(MapSize, MapSize, false, Image.Format.R8, _pixels);
        _tex.Update(_image);
    }

    public override void _Draw()
    {
        if (_player == null || _phase != Phase.None || _near < 0 || GameRoot.Instance.InputLocked)
            return;
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;
        Ui.Begin(this, px);
        Npcs.Prompt(this, view, px, _player.WorldPosition, "이름비에 손을 얹는다");
        Ui.End(this);
    }
}
