using System;
using Godot;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 하루의 빛. 해는 북북동에서 떠서 정오에 남쪽(카메라 쪽) — 그래서 낮에는 카메라를 향한 면이 밝다 —
/// 북북서 수평선으로 진다. 카메라가 북쪽을 보므로 지는 해가 화면 위쪽에 보인다 (V 전망).
/// 밤에는 같은 방향광이 달빛으로 바뀐다.
/// </summary>
public partial class DayNight : Node
{
    /// <summary>게임 속 하루 길이(실제 초). 16분.</summary>
    public const float DaySeconds = 960f;

    public float Hour { get; set; } = 9f;
    public bool Paused { get; set; }

    private DirectionalLight3D _light;
    private float _skyTimer;

    /// <summary>하늘 셰이더는 이 간격으로만 갱신한다. 매 프레임 갱신하면 하늘 광원 맵 재계산이 프레임 예산을 먹는다.</summary>
    private const float SkyRefresh = 0.25f;
    private Godot.Environment _env;
    private ShaderMaterial _sky;

    private readonly struct Key
    {
        public readonly float Hour;
        public readonly Color Horizon, Zenith, Light, Ambient;
        public readonly float LightEnergy, AmbientEnergy;

        public Key(float hour, Color horizon, Color zenith, Color light, float lightEnergy, Color ambient, float ambientEnergy)
        {
            Hour = hour; Horizon = horizon; Zenith = zenith; Light = light;
            LightEnergy = lightEnergy; Ambient = ambient; AmbientEnergy = ambientEnergy;
        }
    }

    // 색은 사진이 아니라 그림 기준: 노을은 과감하게, 밤은 검정이 아니라 남색.
    private static readonly Key[] Keys =
    {
        new(0.0f,  C(0.10f, 0.13f, 0.27f), C(0.02f, 0.03f, 0.10f), C(0.55f, 0.66f, 1.00f), 0.42f, C(0.30f, 0.36f, 0.62f), 0.55f),
        new(4.5f,  C(0.16f, 0.16f, 0.32f), C(0.04f, 0.05f, 0.14f), C(0.55f, 0.66f, 1.00f), 0.30f, C(0.32f, 0.36f, 0.58f), 0.55f),
        new(5.6f,  C(0.98f, 0.62f, 0.55f), C(0.30f, 0.34f, 0.62f), C(1.00f, 0.62f, 0.48f), 0.30f, C(0.52f, 0.50f, 0.72f), 0.50f),
        new(7.0f,  C(0.92f, 0.90f, 0.92f), C(0.42f, 0.62f, 0.95f), C(1.00f, 0.88f, 0.72f), 1.00f, C(0.52f, 0.62f, 0.86f), 0.32f),
        new(12.0f, C(0.74f, 0.88f, 1.00f), C(0.26f, 0.54f, 0.96f), C(1.00f, 0.96f, 0.86f), 1.10f, C(0.50f, 0.64f, 0.92f), 0.32f),
        new(16.0f, C(0.95f, 0.90f, 0.80f), C(0.34f, 0.56f, 0.92f), C(1.00f, 0.88f, 0.66f), 1.05f, C(0.52f, 0.58f, 0.86f), 0.34f),
        // 골든아워: 금빛 해가 낮게 길게 — 그림자는 푸르지만 화면을 덮지 않게 주변광은 낮게
        // 햇빛 색은 채도를 낮게: 주황을 초록 잔디에 곱하면 올리브색으로 탁해진다. 따뜻함은 하늘·하이라이트가 준다
        new(17.4f, C(1.00f, 0.80f, 0.52f), C(0.36f, 0.52f, 0.88f), C(1.00f, 0.86f, 0.64f), 1.08f, C(0.50f, 0.56f, 0.84f), 0.34f),
        new(18.3f, C(1.00f, 0.52f, 0.28f), C(0.32f, 0.36f, 0.70f), C(1.00f, 0.72f, 0.50f), 0.92f, C(0.44f, 0.48f, 0.80f), 0.40f),
        // 해진 직후: 분홍·보라 하늘, 빛은 약하게
        new(19.0f, C(0.86f, 0.42f, 0.46f), C(0.16f, 0.16f, 0.40f), C(0.86f, 0.58f, 0.64f), 0.32f, C(0.40f, 0.46f, 0.78f), 0.56f),
        // 블루아워
        new(19.7f, C(0.26f, 0.26f, 0.48f), C(0.05f, 0.06f, 0.18f), C(0.55f, 0.66f, 1.00f), 0.30f, C(0.30f, 0.36f, 0.62f), 0.52f),
        new(20.3f, C(0.14f, 0.16f, 0.32f), C(0.03f, 0.04f, 0.12f), C(0.55f, 0.66f, 1.00f), 0.38f, C(0.30f, 0.36f, 0.62f), 0.55f),
        new(24.0f, C(0.10f, 0.13f, 0.27f), C(0.02f, 0.03f, 0.10f), C(0.55f, 0.66f, 1.00f), 0.42f, C(0.30f, 0.36f, 0.62f), 0.55f),
    };

    private static Color C(float r, float g, float b) => new(r, g, b);

    public void Bind(DirectionalLight3D light, Godot.Environment env, ShaderMaterial sky)
    {
        _light = light;
        _env = env;
        _sky = sky;
        Apply();
    }

    /// <summary>시간을 흘린다. delta 는 월드 시간(슬로우모션이 걸리면 같이 느려진다).</summary>
    public void Advance(float delta)
    {
        if (!Paused)
            Hour = (Hour + delta * 24f / DaySeconds) % 24f;
        _skyTimer -= delta;
        Apply();
    }

    /// <summary>캡처처럼 시간을 순간 이동시켰을 때 하늘도 바로 따라오게.</summary>
    public void RefreshSkyNow() => _skyTimer = 0f;

    // 해의 길 (시각, 방위°, 고도°). 방위는 북=0 시계방향. 카메라가 북쪽(±25°)만 보므로
    // 해는 북북동(14°)에서 떠서 남쪽 하늘을 돌아 북북서(348°) 수평선으로 진다 — 저녁 한 시간은
    // 화면 왼쪽 위에서 수평선으로 내려가는 해가 보인다 (현실의 해 길이 아니라 그림을 위한 판타지 하늘).
    private static readonly (float hour, float az, float el)[] SunPath =
    {
        (4.8f, 8f, -6f), (5.6f, 14f, 0f), (6.4f, 30f, 7f), (8.0f, 90f, 30f), (12.0f, 180f, 60f),
        (16.0f, 268f, 32f), (17.3f, 322f, 14f), (18.0f, 340f, 7f), (18.8f, 348f, 0f), (19.4f, 352f, -5f),
    };

    /// <summary>해 쪽을 가리키는 방향. 뜨기 전/진 뒤에는 지평선 아래.</summary>
    public static Vector3 SunDirection(float hour)
    {
        if (hour <= SunPath[0].hour || hour >= SunPath[^1].hour)
        {
            var e = hour <= SunPath[0].hour ? SunPath[0] : SunPath[^1];
            return Dir(Mathf.DegToRad(e.az), Mathf.DegToRad(-8f));
        }
        int i = 0;
        while (hour > SunPath[i + 1].hour)
            i++;
        var (h0, az0, el0) = SunPath[i];
        var (h1, az1, el1) = SunPath[i + 1];
        float t = Mathf.SmoothStep(0f, 1f, (hour - h0) / (h1 - h0));
        return Dir(Mathf.DegToRad(Mathf.Lerp(az0, az1, t)), Mathf.DegToRad(Mathf.Lerp(el0, el1, t)));
    }

    public static Vector3 MoonDirection(float hour)
    {
        // 20시 동남동에서 떠서 새벽 5시 서쪽으로. 밤새 남쪽 하늘에 떠 있다.
        float h = hour < 12f ? hour + 24f : hour;
        float t = Mathf.Clamp((h - 19.5f) / (29.5f - 19.5f), 0f, 1f);
        float az = Mathf.DegToRad(Mathf.Lerp(110f, 250f, t));
        float el = Mathf.DegToRad(Mathf.Lerp(18f, 62f, Mathf.Sin(Mathf.Pi * t)));
        return Dir(az, el);
    }

    /// <summary>
    /// 하늘에 그려지고 물에 비치는 달. 카메라가 북쪽을 보므로 남쪽 달은 수면에 윤슬을 못 만든다 —
    /// 보이는 달은 북쪽 하늘을 낮게 지난다(노을을 북서쪽에 둔 것과 같은 이유).
    /// 방향광(그림자)은 MoonDirection 그대로 남쪽: 북쪽에서 비추면 스프라이트 뒤 그림자 캡슐이 제 몸을 가린다.
    /// </summary>
    public static Vector3 VisibleMoonDirection(float hour)
    {
        float h = hour < 12f ? hour + 24f : hour;
        float t = Mathf.Clamp((h - 19.5f) / (29.5f - 19.5f), 0f, 1f);
        float az = Mathf.DegToRad(Mathf.Lerp(65f, -65f, t));   // 동북동 → 북 → 서북서
        float el = Mathf.DegToRad(Mathf.Lerp(12f, 32f, Mathf.Sin(Mathf.Pi * t)));
        return Dir(az, el);
    }

    /// <summary>방위(북=0, 시계방향)와 고도 → 월드 방향. 북쪽이 -Z.</summary>
    private static Vector3 Dir(float az, float el)
        => new(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));

    /// <summary>0 = 한낮 ~ 1 = 한밤.</summary>
    public float Night
    {
        get
        {
            float h = Hour;
            if (h >= 7f && h <= 17.5f) return 0f;
            if (h > 17.5f && h < 20.3f) return Mathf.SmoothStep(18.3f, 20.3f, h);
            if (h >= 5f && h < 7f) return 1f - Mathf.SmoothStep(5f, 6.4f, h);
            return 1f;
        }
    }

    private void Apply()
    {
        if (_light == null)
            return;

        var (a, b, t) = Bracket(Hour);
        Color horizon = a.Horizon.Lerp(b.Horizon, t);
        Color zenith = a.Zenith.Lerp(b.Zenith, t);
        Color lightCol = a.Light.Lerp(b.Light, t);
        float energy = Mathf.Lerp(a.LightEnergy, b.LightEnergy, t);
        Color ambient = a.Ambient.Lerp(b.Ambient, t);
        float ambientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t);

        Vector3 sun = SunDirection(Hour);
        Vector3 moon = MoonDirection(Hour);
        Vector3 shownMoon = VisibleMoonDirection(Hour);
        bool sunUp = sun.Y > 0.02f;
        // 방향광 하나를 해/달이 번갈아 쓴다. 해가 지평선에 붙으면 그림자가 끝없이 길어지므로 고도를 바닥에 깐다.
        Vector3 lightDir = sunUp ? sun : moon;
        lightDir.Y = Mathf.Max(lightDir.Y, 0.18f);
        lightDir = lightDir.Normalized();
        // 해 ↔ 달 교대 순간에 빛을 0 으로 흘려 방향이 휙 바뀌는 게 안 보이게
        float swap = Mathf.Clamp(Mathf.Abs(sun.Y) * 12f, 0f, 1f);
        _light.LightEnergy = energy * swap;
        _light.LightColor = lightCol;
        _light.LookAtFromPosition(Vector3.Zero, -lightDir, Mathf.Abs(lightDir.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        RenderingServer.GlobalShaderParameterSet(Uniform.LightDir, lightDir);

        _env.AmbientLightColor = ambient;
        _env.AmbientLightEnergy = ambientEnergy;
        _env.FogLightColor = horizon;

        float night = Night;
        float sunset = sunUp ? Mathf.Clamp(1f - sun.Y * 4f, 0f, 1f) : 0f;
        if (_skyTimer <= 0f)
        {
            _skyTimer = SkyRefresh;
            _sky.SetShaderParameter(Uniform.MoonDir, shownMoon);
            _sky.SetShaderParameter(Uniform.Sunset, sunset);
            _sky.SetShaderParameter(Uniform.Night, night);
            _sky.SetShaderParameter(Uniform.SunDir, sunUp ? sun : shownMoon);
            _sky.SetShaderParameter(Uniform.SunColor, sunUp ? lightCol : new Color(0.75f, 0.85f, 1f));
            _sky.SetShaderParameter(Uniform.SkyHorizon, horizon);
            _sky.SetShaderParameter(Uniform.SkyZenith, zenith);
            _sky.SetShaderParameter(Uniform.WorldTime, Hour * 150f);
            _sky.SetShaderParameter(Uniform.Aurora, night * AuroraHere());
        }

        RenderingServer.GlobalShaderParameterSet(Uniform.SkyHorizon, horizon);
        RenderingServer.GlobalShaderParameterSet(Uniform.SkyZenith, zenith);
        // 밤에는 '해' 자리에 달을 넣는다 — 물 위 반사 길(윤슬)이 달빛 쪽으로 생긴다
        RenderingServer.GlobalShaderParameterSet(Uniform.SunDir, sunUp ? sun : shownMoon);
        RenderingServer.GlobalShaderParameterSet(Uniform.SunColor, sunUp ? lightCol : new Color(0.75f, 0.85f, 1f));
        RenderingServer.GlobalShaderParameterSet(Uniform.Night, night);
    }

    /// <summary>오로라 세기: 서리 고원에선 짙게, 다른 데선 북쪽 하늘에 옅게.</summary>
    private static float AuroraHere()
    {
        var root = Core.GameRoot.Instance;
        if (root?.Rig == null || root.World == null)
            return 0.3f;
        string zone = root.World.ZoneAt(root.Rig.Target.X, root.Rig.Target.Z);
        return zone == "서리 고원" || zone == "눈꽃 마을" ? 1f : 0.3f;
    }

    private static (Key a, Key b, float t) Bracket(float hour)
    {
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            if (hour >= Keys[i].Hour && hour <= Keys[i + 1].Hour)
            {
                float t = (hour - Keys[i].Hour) / Math.Max(0.001f, Keys[i + 1].Hour - Keys[i].Hour);
                return (Keys[i], Keys[i + 1], Mathf.SmoothStep(0f, 1f, t));
            }
        }
        return (Keys[0], Keys[0], 0f);
    }
}
