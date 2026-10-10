using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 따라가는 카메라. 북쪽을 보고 38° 내려다본다.
/// 전망 지점에서는 고개를 들어(피치↓·거리↑) 수평선과 노을을 보여 준다.
/// V(전망)를 누르고 있으면 거의 수평까지 고개를 들고, 이동 키로 둘러본다 (주인공은 멈춘다).
/// 흔들림/킥은 여기서 더하고, 픽셀 격자 맞춤은 PixelView 가 한다.
/// </summary>
public partial class CameraRig : Node
{
    public Vector3 Target;
    public float Pitch = Px.PitchDeg;
    public float Distance = Px.FocusDistance;
    /// <summary>바라보는 점을 주인공에서 옮긴다 (m). 전망 지점에서 북쪽·위로 — 주인공은 화면 아래로, 풍경이 가운데로.</summary>
    public Vector3 Offset;

    /// <summary>화면 흔들림·킥 오프셋(월드 m). CombatFeedback 가 채운다.</summary>
    public Vector3 Shake;

    /// <summary>전망 중 (V 를 누르고 있다)</summary>
    public bool VistaOn { get; private set; }
    // 전망: 해가 수평선 위 몇 도에 있어도 화면에 들어오도록 거의 수평 (위 끝 = 피치 - 15°)
    private const float VistaPitch = 5f;
    private const float VistaDistance = 34f;
    private static readonly Vector3 VistaLook = new(0f, 3f, -14f);
    private const float PanSpeed = 22f;   // 둘러보기 (m/s)
    private const float PanReach = 70f;   // 주인공에서 이만큼까지 (m)

    private Vector3 _smoothed;
    private float _pitchNow = Px.PitchDeg;
    private float _distNow = Px.FocusDistance;
    private Vector3 _offsetNow;
    private Vector3 _pan;
    private float _uprightFix = -1f;
    private bool _snapNext = true;

    /// <summary>따라가는 부드러움. 클수록 빠르게 붙는다.</summary>
    public float FollowRate = 6f;

    private PixelView _view;

    public void Bind(PixelView view) => _view = view;

    /// <summary>다음 프레임에 보간 없이 바로 붙는다 (순간이동·캡처).</summary>
    public void SnapNext() => _snapNext = true;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var root = GameRoot.Instance;
        VistaOn = root != null && !root.MenuOpen && Input.IsActionPressed(Controls.Vista);
        if (VistaOn)
        {
            var stick = Input.GetVector(Controls.Left, Controls.Right, Controls.Up, Controls.Down);
            _pan += new Vector3(stick.X, 0f, stick.Y) * PanSpeed * dt;
            _pan = _pan.LimitLength(PanReach);
        }
        else
            _pan = _pan.Lerp(Vector3.Zero, 1f - Mathf.Exp(-3f * dt));
        float pitch = VistaOn ? VistaPitch : Pitch;
        float dist = VistaOn ? VistaDistance : Distance;
        Vector3 offset = (VistaOn ? VistaLook : Offset) + _pan;

        float k = 1f - Mathf.Exp(-FollowRate * dt);
        if (_snapNext)
        {
            _smoothed = Target;
            _pitchNow = pitch;
            _distNow = dist;
            _offsetNow = offset;
            _snapNext = false;
        }
        else
        {
            _smoothed = _smoothed.Lerp(Target, k);
            float kv = 1f - Mathf.Exp(-2.2f * dt);
            _pitchNow = Mathf.Lerp(_pitchNow, pitch, kv);
            _distNow = Mathf.Lerp(_distNow, dist, kv);
            _offsetNow = _offsetNow.Lerp(offset, kv);
        }

        // 서 있는 판은 38° 에서 1:1 이 되게 늘려 두었다 — 고개를 들면 그만큼 덜 늘린다 (sprite.gdshader)
        float fix = Mathf.Cos(Mathf.DegToRad(Px.PitchDeg)) / Mathf.Cos(Mathf.DegToRad(_pitchNow));
        if (!Mathf.IsEqualApprox(fix, _uprightFix))
        {
            _uprightFix = fix;
            RenderingServer.GlobalShaderParameterSet(Uniform.UprightFix, fix);
        }

        float rad = Mathf.DegToRad(_pitchNow);
        Vector3 back = new(0f, Mathf.Sin(rad), Mathf.Cos(rad));
        Vector3 eye = _smoothed + _offsetNow + back * _distNow + Shake;
        var basis = Basis.LookingAt(-back, Vector3.Up);
        _view?.PlaceCamera(new Transform3D(basis, eye));
    }
}
