using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 따라가는 카메라. 북쪽을 보고 38° 내려다본다.
/// 전망 지점에서는 고개를 들어(피치↓·거리↑) 수평선과 노을을 보여 준다.
/// 흔들림/킥은 여기서 더하고, 픽셀 격자 맞춤은 PixelView 가 한다.
/// </summary>
public partial class CameraRig : Node
{
    public Vector3 Target;
    public float Pitch = Px.PitchDeg;
    public float Distance = Px.FocusDistance;

    /// <summary>화면 흔들림·킥 오프셋(월드 m). CombatFeedback 가 채운다.</summary>
    public Vector3 Shake;

    private Vector3 _smoothed;
    private float _pitchNow = Px.PitchDeg;
    private float _distNow = Px.FocusDistance;
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
        float k = 1f - Mathf.Exp(-FollowRate * dt);
        if (_snapNext)
        {
            _smoothed = Target;
            _pitchNow = Pitch;
            _distNow = Distance;
            _snapNext = false;
        }
        else
        {
            _smoothed = _smoothed.Lerp(Target, k);
            float kv = 1f - Mathf.Exp(-2.2f * dt);
            _pitchNow = Mathf.Lerp(_pitchNow, Pitch, kv);
            _distNow = Mathf.Lerp(_distNow, Distance, kv);
        }

        float pitch = Mathf.DegToRad(_pitchNow);
        Vector3 back = new(0f, Mathf.Sin(pitch), Mathf.Cos(pitch));
        Vector3 eye = _smoothed + back * _distNow + Shake;
        var basis = Basis.LookingAt(-back, Vector3.Up);
        _view?.PlaceCamera(new Transform3D(basis, eye));
    }
}
