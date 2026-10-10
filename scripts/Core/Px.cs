using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 화면 규격. 픽셀 스프라이트가 1텍셀 = 1화면픽셀로 찍히도록 월드와 카메라를 여기에 맞춘다.
/// 이 비율이 깨지면(스프라이트가 1.3배 따위로 확대되면) 픽셀이 들쭉날쭉해져 조잡해 보인다.
/// </summary>
public static class Px
{
    /// <summary>월드 1m 에 들어가는 스프라이트 텍셀 수. 캐릭터(약 60px) ≈ 1.9m.</summary>
    public const float PerMeter = 32f;

    /// <summary>3D 월드를 그리는 내부 해상도. 창에는 정수배로 확대된다 (720p ×2, 1080p ×3).</summary>
    public static readonly Vector2I LowRes = new(640, 360);

    /// <summary>카메라가 내려다보는 각도. 고원 절벽의 남쪽 면과 지면이 둘 다 읽히는 각.</summary>
    public const float PitchDeg = 38f;

    /// <summary>좁은 화각 — 원근 왜곡이 작아서 멀고 가까운 스프라이트 크기가 크게 다르지 않다.</summary>
    public const float FovDeg = 30f;

    /// <summary>초점 거리: 이 거리의 평면에서 1m 가 정확히 32픽셀이 된다.</summary>
    public static float FocusDistance =>
        LowRes.Y / PerMeter / (2f * Mathf.Tan(Mathf.DegToRad(FovDeg) * 0.5f));

    /// <summary>
    /// 서 있는 스프라이트 판의 세로 보정. 비스듬히 내려다보면 세로가 cos(pitch) 만큼 줄어드니
    /// 그만큼 늘려 세워 둬야 화면에서 1:1 이 된다.
    /// </summary>
    public static float UprightStretch => 1f / Mathf.Cos(Mathf.DegToRad(PitchDeg));
}
