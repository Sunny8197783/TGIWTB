using System;
using Godot;

namespace PixelMmo.World;

/// <summary>
/// 하루의 빛. 해는 동북동에서 떠서 정오에 남쪽(카메라 쪽) — 그래서 낮에는 카메라를 향한 면이 밝다 —
/// 서북서 바다 위로 진다. 카메라가 북쪽을 보므로 노을은 화면 위쪽 수평선에 걸린다.
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

    /// <summary>해가 떠 있는 동안의 방향(해 쪽을 가리킴). 뜨기 전/진 뒤에는 지평선 아래.</summary>
    public static Vector3 SunDirection(float hour)
    {
        // 5.6시 일출(방위 70°) → 12시 남중(180°, 고도 58°) → 18.8시 일몰(290°)
        float t = (hour - 5.6f) / (18.8f - 5.6f);
        float az = Mathf.DegToRad(Mathf.Lerp(70f, 290f, t));
        float el = Mathf.DegToRad(58f * Mathf.Sin(Mathf.Pi * t));
        return Dir(az, el);
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

        _env.AmbientLightColor = ambient;
        _env.AmbientLightEnergy = ambientEnergy;
        _env.FogLightColor = horizon;

        float night = Night;
        float sunset = sunUp ? Mathf.Clamp(1f - sun.Y * 4f, 0f, 1f) : 0f;
        if (_skyTimer <= 0f)
        {
            _skyTimer = SkyRefresh;
            _sky.SetShaderParameter("moon_dir", moon);
            _sky.SetShaderParameter("sunset", sunset);
            _sky.SetShaderParameter("night", night);
            _sky.SetShaderParameter("sun_dir", sunUp ? sun : moon);
            _sky.SetShaderParameter("sun_color", sunUp ? lightCol : new Color(0.75f, 0.85f, 1f));
            _sky.SetShaderParameter("sky_horizon", horizon);
            _sky.SetShaderParameter("sky_zenith", zenith);
            _sky.SetShaderParameter("world_time", Hour * 150f);
        }

        RenderingServer.GlobalShaderParameterSet("sky_horizon", horizon);
        RenderingServer.GlobalShaderParameterSet("sky_zenith", zenith);
        // 밤에는 '해' 자리에 달을 넣는다 — 물 위 반사 길(윤슬)이 달빛 쪽으로 생긴다
        RenderingServer.GlobalShaderParameterSet("sun_dir", sunUp ? sun : moon);
        RenderingServer.GlobalShaderParameterSet("sun_color", sunUp ? lightCol : new Color(0.75f, 0.85f, 1f));
        RenderingServer.GlobalShaderParameterSet("night", night);
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
