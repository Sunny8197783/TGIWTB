using Godot;

namespace PixelMmo.Render;

/// <summary>
/// 매 프레임 넘기는 셰이더 변수 이름. 문자열을 그대로 넘기면 부를 때마다 StringName 이 새로 생기고,
/// 그 정리(종료자)가 GC 를 길게 만들어 전투 중 70~90ms 끊김이 났다. 한 번 만들어 두고 같이 쓴다.
/// </summary>
public static class Uniform
{
    public static readonly StringName
        AlbedoTex = "albedo_tex",
        Aurora = "aurora",
        Bright = "bright",
        Color = "color",
        Core = "core",
        Deep = "deep",
        DepthPull = "depth_pull",
        Dissolve = "dissolve",
        Fade = "fade",
        Fill = "fill",
        Flash = "flash",
        FlashColor = "flash_color",
        Frame = "frame",
        FrameCount = "frame_count",
        Glow = "glow",
        HeroScreen = "hero_screen",
        HeroZ = "hero_z",
        Intensity = "intensity",
        LightDir = "light_dir",
        Mid = "mid",
        MoonDir = "moon_dir",
        Night = "night",
        NormalTex = "normal_tex",
        Progress = "progress",
        Px = "px",
        PxSize = "px_size",
        RingCenter = "ring_center",
        RingRadius = "ring_radius",
        RingWidth = "ring_width",
        Seed = "seed",
        SkyHorizon = "sky_horizon",
        SkyZenith = "sky_zenith",
        SlowExempt = "slow_exempt",
        Softness = "softness",
        SunColor = "sun_color",
        SunDir = "sun_dir",
        Sunset = "sunset",
        Thickness = "thickness",
        TimeSlow = "time_slow",
        UprightFix = "upright_fix",
        Vignette = "vignette",
        VignetteColor = "vignette_color",
        WorldTime = "world_time";
}
