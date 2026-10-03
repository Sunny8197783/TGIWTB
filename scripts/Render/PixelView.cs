using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 3D 월드를 640x360 으로 그려 정수배로 확대한다. 저해상도라 폴리곤의 거친 면이 픽셀로 뭉개져
/// 스프라이트와 한 그림처럼 보이고, 이 노트북 GPU 부담도 1/4 이 된다.
///
/// 카메라를 텍셀 격자에 딱 맞춰 세우고, 버린 소수점만큼 화면 쪽에서 밀어 준다.
/// 안 그러면 카메라가 움직일 때마다 모든 픽셀이 한 칸씩 기어다닌다(픽셀 크리프).
/// </summary>
public partial class PixelView : Control
{
    /// <summary>가장자리 1픽셀씩 여유 — 밀어 준 만큼 빈 줄이 안 보이게.</summary>
    private const int Margin = 1;

    public SubViewport Viewport { get; private set; }
    public Camera3D Camera { get; private set; }

    /// <summary>후처리(색보정·비네트·슬로우모션 색) 셰이더가 걸린 표시면.</summary>
    public TextureRect Screen { get; private set; }

    private float _scale = 2f;

    public override void _Ready()
    {
        Name = "PixelView";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        Viewport = new SubViewport
        {
            Name = "World3D",
            Size = Px.LowRes + new Vector2I(Margin * 2, Margin * 2),
            OwnWorld3D = true,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            PhysicsObjectPicking = false,
        };
        AddChild(Viewport);

        Camera = new Camera3D
        {
            Name = "Camera",
            Fov = Px.FovDeg,
            Near = 0.5f,
            Far = 2600f,
            Current = true,
        };
        Viewport.AddChild(Camera);

        Screen = new TextureRect
        {
            Name = "Screen",
            Texture = Viewport.GetTexture(),
            TextureFilter = TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(Screen);
        Layout(Vector2.Zero);
    }

    /// <summary>
    /// 원하는 카메라 자리를 받아 텍셀 격자에 맞춰 세운다. 초점면에서 1텍셀 = 1/32m.
    /// </summary>
    public void PlaceCamera(Transform3D desired)
    {
        Basis b = desired.Basis;
        Vector3 p = desired.Origin;
        Vector3 snapped = Snap(p, b, out Vector2 rest);
        Camera.GlobalTransform = new Transform3D(b, snapped);

        // 카메라를 오른쪽으로 덜 옮겼으면(sr<pr) 그림은 왼쪽으로 덜 간 셈 → 화면을 왼쪽으로 민다.
        Layout(new Vector2(-rest.X, rest.Y));
    }

    /// <summary>
    /// 움직이는 스프라이트(캐릭터·몬스터)도 같은 격자에 세운다. 카메라와 같은 격자에 서야
    /// 텍셀 하나가 화면 픽셀 하나에 정확히 떨어진다 — 아니면 움직일 때 픽셀이 뭉개졌다 늘어났다 한다.
    /// </summary>
    public Vector3 SnapToTexel(Vector3 p) => Snap(p, Camera.GlobalTransform.Basis, out _);

    /// <param name="rest">버린 소수점 (텍셀 단위, 오른쪽·위).</param>
    private static Vector3 Snap(Vector3 p, Basis b, out Vector2 rest)
    {
        Vector3 right = b.X.Normalized();
        Vector3 up = b.Y.Normalized();
        float pr = p.Dot(right) * Px.PerMeter;
        float pu = p.Dot(up) * Px.PerMeter;
        float sr = Mathf.Round(pr), su = Mathf.Round(pu);
        rest = new Vector2(pr - sr, pu - su);
        return p + right * ((sr - pr) / Px.PerMeter) + up * ((su - pu) / Px.PerMeter);
    }

    /// <summary>화면 확대 배율 (720p ×2, 1080p ×3). HUD 픽셀도 이 배율로 찍는다.</summary>
    public float PixelScale => _scale;

    /// <summary>월드 좌표 → 창 좌표 (HUD 가 머리 위 체력바·피해 숫자를 붙일 때).</summary>
    public Vector2 WorldToScreen(Vector3 world) =>
        Screen.Position + Camera.UnprojectPosition(world) * _scale;

    private void Layout(Vector2 subPixel)
    {
        if (Screen == null)
            return;
        Vector2 logical = GetViewportRect().Size;
        _scale = Mathf.Max(1f, Mathf.Floor(logical.Y / Px.LowRes.Y));
        Vector2 size = (Vector2)(Px.LowRes + new Vector2I(Margin * 2, Margin * 2)) * _scale;
        Vector2 origin = (logical - (Vector2)Px.LowRes * _scale) * 0.5f - new Vector2(Margin, Margin) * _scale;
        Screen.Position = origin + subPixel * _scale;
        Screen.Size = size;
    }
}
