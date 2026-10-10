using Godot;
using PixelMmo.Combat;
using PixelMmo.Render;
using PixelMmo.World;

namespace PixelMmo.Core;

/// <summary>
/// 시작점. 화면(PixelView) → 빛과 하늘 → 지형·물·소품 → 카메라 순으로 조립한다.
/// </summary>
public partial class GameRoot : Node
{
    public static GameRoot Instance { get; private set; }

    public WorldData World { get; private set; }
    public PixelView View { get; private set; }
    public CameraRig Rig { get; private set; }
    public DayNight DayCycle { get; private set; }
    public Node3D Stage { get; private set; }

    /// <summary>셰이더 시계. 슬로우모션이면 같이 느려진다.</summary>
    public float WorldTime { get; private set; }

    /// <summary>
    /// 세계(적·물·입자·셰이더) 시간 배율. 히트스톱 때 0, 완벽 회피 슬로우 때 SlowScale.
    /// 주인공은 HeroScale 을 쓴다 — 슬로우 중에도 나는 정상 속도로 움직여야 한다 (Witch Time).
    /// </summary>
    public float WorldScale { get; private set; } = 1f;
    /// <summary>주인공 시간 배율. 히트스톱 때만 0.</summary>
    public float HeroScale { get; private set; } = 1f;
    /// <summary>0..1 슬로우 연출 세기 (셰이더 전역 time_slow). 들어갈 때 빠르게, 나올 때 천천히.</summary>
    public float SlowAmount { get; private set; }

    private float _hitStop;
    private float _slowLeft;
    private float _slowScale = 1f;
    private bool _wasSlow;
    private bool _slowTint = true;

    /// <summary>칼이 맞는 순간 모두 멈춘다 (실제 시간). 겹치면 긴 쪽.</summary>
    public void HitStop(float seconds) => _hitStop = Mathf.Max(_hitStop, seconds);

    /// <summary>세계만 느리게 (실제 시간 seconds 동안). tint = 완벽 회피 연출(보랏빛 무채색·먹먹한 소리)도 켤지.</summary>
    public void SlowWorld(float scale, float seconds, bool tint = true)
    {
        _slowScale = scale;
        _slowLeft = seconds;
        _slowTint = tint;
    }

    public bool WorldSlowed => _slowLeft > 0f;
    /// <summary>메뉴(모습 고르기)가 떠 있다 — 주인공은 입력을 받지 않는다</summary>
    public bool MenuOpen { get; set; }
    /// <summary>주인공이 조작을 받지 않는다: 메뉴가 떠 있거나 전망으로 둘러보는 중</summary>
    public bool InputLocked => MenuOpen || (Rig != null && Rig.VistaOn);

    private void TickClock(float real)
    {
        _hitStop -= real;
        _slowLeft -= real;
        bool stop = _hitStop > 0f;
        bool slow = _slowLeft > 0f;
        HeroScale = stop ? 0f : 1f;
        WorldScale = stop ? 0f : slow ? _slowScale : 1f;
        bool tinted = slow && _slowTint;
        float amount = Mathf.MoveToward(SlowAmount, tinted ? 1f : 0f, real * (tinted ? 8f : 2.5f));
        // 바뀔 때만 넣는다: 하늘 셰이더가 이 값을 읽어서, 넣을 때마다 하늘 광원 맵을 다시 굽는다
        if (amount != SlowAmount)
            RenderingServer.GlobalShaderParameterSet(Uniform.TimeSlow, amount);
        SlowAmount = amount;
        if (tinted != _wasSlow)
            Sfx.SetMuffled(tinted);
        _wasSlow = tinted;
        // 세계의 입자도 같이 느려진다
        foreach (var n in GetTree().GetNodesInGroup(WorldParticles))
            ((GpuParticles3D)n).SpeedScale = WorldScale;
    }

    /// <summary>이 그룹의 GpuParticles3D 는 세계 시간을 따른다.</summary>
    public const string WorldParticles = "world_particles";

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        ulong t0 = Time.GetTicksMsec();
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0)
        {
            int code = 0;
            try { Combat.Mastery.SelfCheck(); PixelMmo.World.Footprint.SelfCheck(); }
            catch (System.Exception e) { GD.PrintErr(e.Message); code = 1; }
            ProcessMode = ProcessModeEnum.Disabled; // 나머지를 만들지 않았으니 한 프레임도 돌지 않게
            GetTree().Quit(code);
            return;
        }
        Controls.Register();
        AddChild(new Sfx());
        World = WorldData.Load();

        var ui = new CanvasLayer { Name = "ScreenLayer", Layer = -1 };
        AddChild(ui);
        View = new PixelView();
        ui.AddChild(View);

        Stage = new Node3D { Name = "Stage" };
        View.Viewport.AddChild(Stage);

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            // 분할(cascade) 경계가 땅에 가로줄로 보였다. 플레이 화면은 30m 남짓이라 한 장으로 충분하다.
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 55f,
            // 카메라를 향한 스프라이트 판이 자기 그림자를 받아 줄무늬(섀도 애크니)가 생긴다 — 넉넉히 띄운다.
            ShadowBias = 0.12f,
            ShadowNormalBias = 2.5f,
            ShadowBlur = 0.6f,
        };
        Stage.AddChild(sun);

        var skyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky { SkyMaterial = skyMat, RadianceSize = Godot.Sky.RadianceSizeEnum.Size32, ProcessMode = Godot.Sky.ProcessModeEnum.Incremental },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            // 선형 톤매핑: 픽셀아트 색을 그대로 둔다. Filmic 은 중간톤 채도를 눌러 화면이 바랬다.
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowStrength = 1.0f,
            GlowBloom = 0.04f,
            // 문턱이 낮으면 햇빛 받은 잔디(1.0 남짓)까지 번져서 화면 전체가 뿌옇게 된다. 반짝임·등불만 번지게.
            GlowHdrThreshold = 1.35f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogDensity = 0.0022f,
            FogSkyAffect = 0.0f,
            FogAerialPerspective = 0f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.08f,
            AdjustmentContrast = 1.04f,
        };
        env.SetGlowLevel(0, 0f);
        env.SetGlowLevel(1, 1f);
        env.SetGlowLevel(2, 0.6f);
        env.SetGlowLevel(3, 0.3f);
        Stage.AddChild(new WorldEnvironment { Environment = env });

        DayCycle = new DayNight();
        AddChild(DayCycle);
        DayCycle.Bind(sun, env, skyMat);

        // --no=grass,props,water,falls,shadows : 성능 A/B 용으로 한 덩어리씩 뺀다
        var off = Dev.DevCapture.Disabled();
        Stage.AddChild(TerrainBuilder.Build(World));
        if (!off.Contains("water")) Stage.AddChild(WaterBuilder.Build(World));
        if (!off.Contains("props")) Stage.AddChild(PropBuilder.Build(World));
        Stage.AddChild(BridgeBuilder.Build(World));
        if (!off.Contains("backdrop")) Stage.AddChild(BackdropBuilder.Build(World));
        if (!off.Contains("falls")) Stage.AddChild(WaterfallBuilder.Build(World));
        if (!off.Contains("grass")) Stage.AddChild(new GrassField(World));
        if (off.Contains("shadows")) sun.ShadowEnabled = false;
        if (off.Contains("sky")) { env.BackgroundMode = Godot.Environment.BGMode.Color; env.BackgroundColor = new Color(0.5f, 0.7f, 1f); }
        if (off.Contains("glow")) env.GlowEnabled = false;
        if (off.Contains("fog")) env.FogEnabled = false;
        if (off.Contains("terrain")) Stage.GetNode<Node3D>("Terrain").Visible = false;

        Rig = new CameraRig();
        AddChild(Rig);
        Rig.Bind(View);
        Stage.AddChild(new Dust());
        if (!off.Contains("weather")) Stage.AddChild(new Weather(World));
        if (!off.Contains("nightlights")) Stage.AddChild(new NightLights(World));
        AddChild(new CombatFx());

        Vector2 spawn = World.Vec2("spawn");
        var feet = new Vector3(spawn.X, World.HeightAt(spawn.X, spawn.Y), spawn.Y);
        Rig.Target = feet;
        Rig.SnapNext();
        var statues = new Statues(World);
        if (!off.Contains("hero"))
        {
            Stage.AddChild(new Hero());
            // 마지막으로 기도한 여신상 앞에서 시작한다 (처음이면 하루미 광장)
            Hero.Instance.Teleport(statues.RespawnFeet(Hero.Instance) ?? feet);
        }
        if (!off.Contains("monsters"))
            AddChild(new MonsterSpawner());
        var hud = new Hud();
        ui.AddChild(hud);
        if (Hero.Instance != null)
        {
            hud.Bind(Hero.Instance);
            ui.AddChild(statues);
            statues.Bind(Hero.Instance);
            var creator = new CharacterCreator();
            ui.AddChild(creator);
            creator.Bind(Hero.Instance);
            // 처음 켰으면 모습부터 (--creator: 캡처에서 화면 확인용)
            if (Hero.Instance.FirstLaunch || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--creator") >= 0)
                creator.Open(first: true);
            var scenery = new Scenery(World);
            AddChild(scenery);
            AddChild(new Ambience(World, scenery));
        }

        if (Dev.DevCapture.Requested())
            AddChild(new Dev.DevCapture());
        else
            AddChild(new Dev.FreeLook());

        GD.Print($"[Game] 준비 {Time.GetTicksMsec() - t0}ms — 렌더러 {RenderingServer.GetCurrentRenderingMethod()}");
    }

    public override void _Process(double delta)
    {
        TickClock((float)delta);
        float dt = (float)delta * WorldScale;
        WorldTime += dt;
        RenderingServer.GlobalShaderParameterSet(Uniform.WorldTime, WorldTime);
        DayCycle.Advance(dt);
    }
}
