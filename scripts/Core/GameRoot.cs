using Godot;
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

    /// <summary>월드 시간 배율(1 = 정상). 완벽 회피 때 떨어진다. 플레이어는 이 값을 따로 무시한다.</summary>
    public float WorldScale = 1f;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        ulong t0 = Time.GetTicksMsec();
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
        if (!off.Contains("falls")) Stage.AddChild(WaterfallBuilder.Build(World));
        if (!off.Contains("grass")) Stage.AddChild(GrassBuilder.Build(World));
        if (off.Contains("shadows")) sun.ShadowEnabled = false;
        if (off.Contains("sky")) { env.BackgroundMode = Godot.Environment.BGMode.Color; env.BackgroundColor = new Color(0.5f, 0.7f, 1f); }
        if (off.Contains("glow")) env.GlowEnabled = false;
        if (off.Contains("fog")) env.FogEnabled = false;
        if (off.Contains("terrain")) Stage.GetNode<Node3D>("Terrain").Visible = false;

        Rig = new CameraRig();
        AddChild(Rig);
        Rig.Bind(View);
        Vector2 spawn = World.Vec2("spawn");
        Rig.Target = new Vector3(spawn.X, World.HeightAt(spawn.X, spawn.Y), spawn.Y);
        Rig.SnapNext();

        if (Dev.DevCapture.Requested())
            AddChild(new Dev.DevCapture());
        else
            AddChild(new Dev.FreeLook());

        GD.Print($"[Game] 준비 {Time.GetTicksMsec() - t0}ms — 렌더러 {RenderingServer.GetCurrentRenderingMethod()}");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * WorldScale;
        WorldTime += dt;
        RenderingServer.GlobalShaderParameterSet("world_time", WorldTime);
        DayCycle.Advance(dt);
    }
}
