using System.Collections.Generic;
using System.Globalization;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Dev;

/// <summary>
/// 눈으로 확인하는 캡처 도구. 게임 로직은 건드리지 않는다.
///   godot --path . -- --capture --shot=112,150,9 --shot=112,90,19,20,34 --out=user://shots
/// shot = x,z,시각[,피치,거리]. 주인공도 그 자리로 옮긴다. 찍고 나면 종료한다.
///   --hold=초          각 장면에 이만큼 머물며 프레임 시간을 잰다
///   --settle=N         장면을 옮긴 뒤 N 프레임 뒤에 찍는다 (기본 24, 동작 한가운데를 찍을 때 줄인다)
///   --press=a,b        장면마다 그 조작을 누른다 (move_right 처럼 누르고 있기, attack 처럼 한 번)
///   --exp=N            장면마다 경험치 N (레벨업 연출)  ·  --level=N --job=id  레벨·직업을 정해 시작
///   --react=guard|dodge --after=N
///                      적의 공격 예고가 끝나기 직전에 그 조작을 누르고 N 프레임 뒤에 찍는다 (패링·완벽 회피 확인)
///   --novsync          60fps 상한을 풀어 실제 여유를 잰다
/// </summary>
public partial class DevCapture : Node
{
    private readonly List<float[]> _shots = new();
    private string _out = "user://shots";
    private int _index = -1;
    private int _wait;
    private int _settle = 24;
    private float _hold;

    private readonly List<string> _press = new();
    private int _exp;   // --exp=N : 장면마다 경험치 N (레벨업 연출 확인)
    private bool _pressPending;
    private string _react;
    private int _after = 6;
    private bool _reacted;

    private readonly List<float> _frameMs = new();
    private readonly List<float> _physMs = new();
    // 튀는 프레임이 C# GC 와 겹치는지 (전투 중 3초에 한 번쯤 25~40ms)
    private const float SpikeMs = 22f;
    private int _spikes, _gcSpikes, _gcCount, _lastGc;
    // 프레임마다 어디에 시간을 썼나 — 튄 프레임 앞뒤를 찍어 원인 칸을 가린다
    private readonly record struct FrameStat(float Ms, float Proc, float Phys, int Steps, float RenderCpu, float RenderGpu, int Gc);
    private readonly List<FrameStat> _stats = new();
    private int _steps;
    private ulong _last;

    public static HashSet<string> Disabled()
    {
        var set = new HashSet<string>();
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--no="))
                foreach (var part in a.Substring(5).Split(','))
                    set.Add(part);
        return set;
    }

    public static bool Requested()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--capture") return true;
        return false;
    }

    /// <summary>--mastery=sk_moon_crescent:29.5 — 진화 직전 상태로 시작해 진화를 확인한다.</summary>
    public static IEnumerable<(string id, float value)> MasterySeeds()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--mastery="))
                continue;
            foreach (var part in a.Substring(10).Split(','))
            {
                var kv = part.Split(':');
                yield return (kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>--look=warrior:2 — 모습을 정해 찍는다.</summary>
    public static void LookOverride(ref string baseId, ref int accent)
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--look="))
                continue;
            var kv = a.Substring(7).Split(':');
            baseId = kv[0];
            accent = kv.Length > 1 ? int.Parse(kv[1]) : 0;
        }
    }

    /// <summary>--forge=id 또는 --forge=latest — 빚은 모습(user://forge/&lt;id&gt;)을 입고 찍는다. 동작이 다 새겨진 것만.</summary>
    public static string ForgeOverride()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--forge="))
                continue;
            string id = a.Substring(8);
            if (id != "latest")
                return Forge.Ready(id) ? id : null;
            string best = null;
            ulong bestTime = 0;
            if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(Forge.Root)))
                return null;
            foreach (string dir in DirAccess.GetDirectoriesAt(Forge.Root))
            {
                if (!Forge.Ready(dir))
                    continue;
                ulong t = FileAccess.GetModifiedTime(Forge.Folder(dir) + "/meta.json");
                if (t >= bestTime)
                {
                    bestTime = t;
                    best = dir;
                }
            }
            return best;
        }
        return null;
    }

    /// <summary>--level=N --job=id : 레벨·직업을 정해 시작 (세이브와 무관). 스킬 칸도 그 레벨만큼 열린다</summary>
    public static void JobOverride(ref int level, ref Data.JobDef job)
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--level="))
                level = int.Parse(a.Substring(8));
            else if (a.StartsWith("--job="))
                job = Data.JobDef.Get(a.Substring(6));
        }
    }

    // "방금 눌렀다"는 물리 프레임 번호로 판정된다 — 주인공보다 먼저 도는 물리 프레임 안에서 눌러야 한다
    public override void _EnterTree() => ProcessPhysicsPriority = -100;

    public override void _Ready()
    {
        RenderingServer.ViewportSetMeasureRenderTime(GameRoot.Instance.View.Viewport.GetViewportRid(), true);
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot="))
            {
                var parts = a.Substring(7).Split(',');
                var v = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    v[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
                _shots.Add(v);
            }
            else if (a == "--novsync")
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            else if (a.StartsWith("--hold="))
                _hold = float.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--settle="))
                _settle = int.Parse(a.Substring(9));
            else if (a.StartsWith("--exp="))
                _exp = int.Parse(a.Substring(6));
            else if (a.StartsWith("--press="))
                _press.AddRange(a.Substring(8).Split(','));
            else if (a.StartsWith("--react="))
                _react = a.Substring(8);
            else if (a.StartsWith("--after="))
                _after = int.Parse(a.Substring(8));
            else if (a.StartsWith("--out="))
                _out = a.Substring(6);
            else if (a.StartsWith("--kill="))
                _killEvery = int.Parse(a.Substring(7));
            else if (a.StartsWith("--spawn="))
                _spawn.AddRange(a.Substring(8).Split(','));
            else if (a.StartsWith("--spawnat="))
            {
                var o = a.Substring(10).Split(',');
                _spawnAt = new Vector2(float.Parse(o[0], CultureInfo.InvariantCulture), float.Parse(o[1], CultureInfo.InvariantCulture));
            }
            else if (a.StartsWith("--delay="))
                _delay = int.Parse(a.Substring(8));
            else if (a == "--waitforge")
                _waitForge = true;
            else if (a.StartsWith("--keys="))
            {
                foreach (string item in a.Substring(7).Split(','))
                {
                    var kv = item.Split(':');
                    _keys.Add((int.Parse(kv[0]), kv[1]));
                }
            }
            else if (a.StartsWith("--steps="))
            {
                foreach (string n in a.Substring(8).Split(','))
                    _stepShots.Add(int.Parse(n));
                _stepShots.Sort();
                _shootAfter = _stepShots[^1];
            }
        }
        DirAccess.MakeDirRecursiveAbsolute(_out);
        GD.Print($"[Capture] {_shots.Count}장 → {ProjectSettings.GlobalizePath(_out)}");
        // 불러온 직후 몇 프레임은 셰이더 컴파일로 길다 — 동작 타이밍을 찍으려면 먼저 가라앉힌다
        GetTree().CreateTimer(1.0).Timeout += Next;
    }

    private void Next()
    {
        _index++;
        if (_index >= _shots.Count)
        {
            GetTree().Quit();
            return;
        }
        var s = _shots[_index];
        var root = GameRoot.Instance;
        var feet = new Vector3(s[0], root.World.HeightAt(s[0], s[1]), s[1]);
        root.Rig.Target = feet;
        Combat.Hero.Instance?.Teleport(feet);
        root.Rig.Pitch = s.Length > 3 ? s[3] : Px.PitchDeg;
        root.Rig.Distance = s.Length > 4 ? s[4] : Px.FocusDistance;
        root.Rig.SnapNext();
        root.DayCycle.Hour = s[2];
        root.DayCycle.Paused = true;
        root.DayCycle.RefreshSkyNow();
        _wait = _settle + (int)(_hold * 60f);
        _frameMs.Clear();
        _physMs.Clear();
        _stats.Clear();
        _spikes = _gcSpikes = _gcCount = 0;
        _lastGc = System.GC.CollectionCount(0);
        _pressPending = _press.Count > 0 || _exp > 0;
        _delayLeft = _delay;
        _keyTick = 0;
        if (_pressPending && _delay > 0)
            _wait = int.MaxValue - 1; // 누른 뒤부터 센다
        else if (!_pressPending && _shootAfter > 0)
        {
            // 누를 것이 없으면 장면이 시작된 때부터 센다
            _shootLeft = _shootAfter;
            _wait = int.MaxValue - 1;
        }
        _reacted = false;
        SpawnTest(feet);
    }

    // --spawn=id:수[:레벨[:h]],…  장면마다 주인공 북쪽 4m 둘레에 몬스터를 세운다 (h = 무명)
    private readonly List<string> _spawn = new();
    // --spawnat=dx,dz : 첫 몬스터를 세울 자리 (주인공 기준 m). --delay=N : 물리 N걸음(60 = 1초) 기다렸다가 누른다 (몬스터가 다 나타난 뒤)
    private Vector2 _spawnAt = new(-3f, -3.5f);
    private int _delay, _delayLeft;
    // --waitforge : 빚기(Core/Forge)가 일하는 동안은 찍지 않고 기다린다 (실시간 생성은 몇 분~수십 분)
    private bool _waitForge;
    // --keys=60:attack,200:appearance : 장면이 시작되고 물리 N걸음째에 그 조작을 톡 누른다 (첫 화면 → 모습 → 여는 글처럼 차례로 눌러야 하는 흐름)
    private readonly List<(int step, string action)> _keys = new();
    private int _keyTick;
    // --steps=N : 누른 뒤 물리 N걸음(60 = 1초)째에 찍는다. --settle 은 그리기 프레임이라 PC 마다 시간이 달라, 동작 타이밍은 이걸로 맞춘다
    // --steps=6,10,16 처럼 여럿이면 그 걸음마다 한 장씩 (shot_00_s06.png …) — 이펙트가 흘러가는 모습을 한 번에 본다
    private int _shootAfter, _shootLeft = -1;
    private readonly List<int> _stepShots = new();
    private readonly Queue<int> _grab = new();

    private void SpawnTest(Vector3 feet)
    {
        foreach (var e in Combat.Enemy.All.ToArray())
            e.Despawn();
        int k = 0;
        foreach (string item in _spawn)
        {
            var p = item.Split(':');
            if (!Data.MonsterDef.Exists(p[0]))
            {
                GD.PrintErr($"[Capture] 모르는 몬스터 {p[0]}");
                continue;
            }
            var def = Data.MonsterDef.Get(p[0]);
            int count = p.Length > 1 ? int.Parse(p[1]) : 1;
            int level = p.Length > 2 ? int.Parse(p[2]) : def.Level;
            bool hushed = p.Length > 3 && p[3] == "h";
            for (int i = 0; i < count; i++, k++)
                Combat.MonsterSpawner.SpawnAt(def, new Vector2(feet.X + _spawnAt.X + k * 2.2f, feet.Z + _spawnAt.Y - (k % 2) * 1.2f), level, hushed);
        }
    }

    // --kill=N : 물리 N걸음마다 살아 있는 적을 모두 쓰러뜨린다 (이름비 싸움을 끝까지 돌려 볼 때)
    private int _killEvery, _killTick;

    public override void _PhysicsProcess(double delta)
    {
        _steps++;
        if (_killEvery > 0 && ++_killTick % _killEvery == 0)
            foreach (var e in Combat.Enemy.All.ToArray())
                if (e.Alive)
                    e.TakeHit(999999f, Vector3.Forward, true);
        if (_index < 0 || _index >= _shots.Count)
            return;
        _keyTick++;
        foreach (var (step, action) in _keys)
        {
            if (_keyTick == step)
                Input.ActionPress(action);
            else if (_keyTick == step + 3)
                Input.ActionRelease(action);
        }
        if (_react != null && !_reacted)
        {
            _wait = int.MaxValue - 1; // 반응할 때까지 기다린다
            foreach (var e in Combat.Enemy.All)
            {
                if (e.WindupLeft > 0.05f)
                    continue;
                _reacted = true;
                Input.ActionPress(_react);
                if (_react == Controls.Dodge)
                    Input.ActionPress(Controls.Left); // 옆으로 빠진다
                _wait = _after;
                break;
            }
        }
        if (_shootLeft > 0)
        {
            _shootLeft--;
            int step = _shootAfter - _shootLeft;
            if (_shootLeft == 0)
                _wait = 1;
            else if (_stepShots.Contains(step))
                _grab.Enqueue(step);
        }
        if (_pressPending && --_delayLeft < 0)
        {
            _pressPending = false;
            if (_shootAfter > 0)
            {
                _shootLeft = _shootAfter;
                _wait = int.MaxValue - 1;
            }
            else if (_delay > 0)
                _wait = _settle;
            if (_exp > 0)
                Combat.Hero.Instance?.GrantExp(_exp);
            foreach (var action in _press)
                Input.ActionPress(action);
        }
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_last > 0 && _wait < _settle + (int)(_hold * 60f) - 20)
        {
            float ms = (now - _last) / 1000f;
            _frameMs.Add(ms);
            _physMs.Add((float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f);
            int gc = System.GC.CollectionCount(0);
            if (gc != _lastGc)
                _gcCount++;
            if (ms > SpikeMs)
            {
                _spikes++;
                if (gc != _lastGc)
                    _gcSpikes++;
            }
            var vp = GameRoot.Instance.View.Viewport.GetViewportRid();
            _stats.Add(new FrameStat(ms,
                (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f,
                (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f,
                _steps,
                (float)RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + (float)RenderingServer.GetFrameSetupTimeCpu(),
                (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp),
                gc - _lastGc));
            _lastGc = gc;
        }
        _steps = 0;
        _last = now;

        if (_index < 0 || _index >= _shots.Count)
            return;
        if (_grab.Count > 0)
            CallDeferred(nameof(Grab), _grab.Dequeue());
        if (_waitForge && Forge.Instance.Busy && _wait < 400 && _shootLeft <= 0)
            _wait = 400;   // 끝난 뒤에도 잠깐 더 (갈아입은 모습이 그려지게)
        if (--_wait > 0)
            return;
        CallDeferred(nameof(Shoot));
        _wait = int.MaxValue;
    }

    /// <summary>중간 걸음 한 장: 다음 장면으로 넘어가지 않고 화면만 남긴다.</summary>
    private void Grab(int step)
    {
        string path = $"{_out}/shot_{_index:D2}_s{step:D2}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
    }

    private void Shoot()
    {
        ReportPerf();
        // 주인공이 찍을 자리에 없으면 알린다 (가끔 광장에 남은 채 찍힌 적이 있다)
        var s = _shots[_index];
        var hero = Combat.Hero.Instance;
        if (hero != null && new Vector2(hero.GlobalPosition.X - s[0], hero.GlobalPosition.Z - s[1]).Length() > 3f)
            GD.PushWarning($"[Capture] shot{_index:D2}: 주인공이 {hero.GlobalPosition} 에 있다 (목표 {s[0]},{s[1]})");
        var img = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/shot_{_index:D2}.png";
        img.SavePng(path);
        GD.Print($"[Capture] {path}");
        Next();
    }

    private void ReportPerf()
    {
        if (_frameMs.Count < 10) return;
        float phys = 0f;
        foreach (float v in _physMs) phys += v;
        _frameMs.Sort();
        GD.Print($"[Perf] shot{_index:D2} 중앙 {_frameMs[_frameMs.Count / 2]:0.0}ms  p90 {_frameMs[(int)(_frameMs.Count * 0.9f)]:0.0}ms  최악 {_frameMs[^1]:0.0}ms"
            + $"  | 물리 평균 {phys / _physMs.Count:0.0}ms 드로우 {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}"
            + $"  | {SpikeMs:0}ms 넘게 튐 {_spikes}번 (GC 와 겹침 {_gcSpikes}) · GC {_gcCount}번");
        // 가장 크게 튄 프레임 다섯과 그 앞뒤: 전체 / 스크립트 / 물리(걸음 수) / 그리기 CPU / GPU
        var worst = new List<int>();
        for (int i = 0; i < _stats.Count; i++)
            if (_stats[i].Ms > SpikeMs)
                worst.Add(i);
        worst.Sort((a, b) => _stats[b].Ms.CompareTo(_stats[a].Ms));
        foreach (int i in worst.GetRange(0, Mathf.Min(5, worst.Count)))
        {
            var line = new System.Text.StringBuilder($"[Spike] #{i}");
            for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(_stats.Count - 1, i + 1); j++)
            {
                var f = _stats[j];
                line.Append($" | {(j == i ? "*" : "")}{f.Ms:0.0}ms 스크립트 {f.Proc:0.0} 물리 {f.Phys:0.0}x{f.Steps} 그리기 {f.RenderCpu:0.0}/{f.RenderGpu:0.0}{(f.Gc > 0 ? " GC" : "")}");
            }
            GD.Print(line.ToString());
        }
    }
}
