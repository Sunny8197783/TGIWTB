using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

/// <summary>
/// 플레이어 추적 카메라. 부드러운 lerp(weight = FollowWeight * delta) + 맵 경계 클램프. (§G)
/// 화면 흔들림도 여기서 처리한다. 흔들림은 히트스톱과 무관하게 실시간으로 흐른다. (§C-6)
/// </summary>
public partial class GameCamera : Camera2D
{
    /// <summary>§G: weight = 8 * delta</summary>
    public static readonly float FollowWeight = 8.0f;

    /// <summary>§G: 카메라 3배 확대</summary>
    public static readonly float ZoomLevel = 3.0f;

    private Node2D _target;
    private float _shakeAmplitude;
    private float _shakeTimer;
    private float _shakeDuration;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Name = "GameCamera";
        Zoom = new Vector2(ZoomLevel, ZoomLevel);
        PositionSmoothingEnabled = false; // lerp 를 직접 돌린다.
        MakeCurrent();
        _rng.Randomize();
    }

    public void Follow(Node2D target)
    {
        _target = target;
        if (target != null)
            GlobalPosition = Clamp(target.GlobalPosition);
    }

    /// <summary>진폭(px)과 지속 시간(초). 이미 흔들리는 중이면 더 센 쪽이 이긴다.</summary>
    public void Shake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;

        if (amplitude >= _shakeAmplitude)
        {
            _shakeAmplitude = amplitude;
            _shakeDuration = duration;
            _shakeTimer = duration;
        }
    }

    public void ShakeNormalHit()
        => Shake(CombatTuning.ShakeNormalAmp, CombatTuning.ShakeNormalTime);

    public void ShakeHeavyHit()
        => Shake(CombatTuning.ShakeHeavyAmp, CombatTuning.ShakeHeavyTime);

    public void ShakePlayerHurt()
        => Shake(CombatTuning.ShakeHurtAmp, CombatTuning.ShakeHurtTime);

    public override void _Process(double delta)
    {
        if (_target != null && IsInstanceValid(_target))
        {
            float weight = Mathf.Clamp(FollowWeight * (float)delta, 0f, 1f);
            GlobalPosition = Clamp(GlobalPosition.Lerp(_target.GlobalPosition, weight));
        }

        UpdateShake((float)delta);
    }

    private void UpdateShake(float delta)
    {
        if (_shakeTimer <= 0f)
        {
            Offset = Vector2.Zero;
            return;
        }

        _shakeTimer -= delta;
        if (_shakeTimer <= 0f)
        {
            _shakeAmplitude = 0f;
            Offset = Vector2.Zero;
            return;
        }

        // 남은 시간에 비례해 진폭이 줄어든다.
        float falloff = _shakeDuration > 0f ? _shakeTimer / _shakeDuration : 0f;
        float amp = _shakeAmplitude * falloff;
        Offset = new Vector2(_rng.RandfRange(-amp, amp), _rng.RandfRange(-amp, amp));
    }

    /// <summary>카메라가 맵 밖을 비추지 않게 가둔다.</summary>
    private Vector2 Clamp(Vector2 position)
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 half = viewport * 0.5f / ZoomLevel;
        Rect2 bounds = WorldLayout.WorldBounds;

        float minX = bounds.Position.X + half.X;
        float maxX = bounds.End.X - half.X;
        float minY = bounds.Position.Y + half.Y;
        float maxY = bounds.End.Y - half.Y;

        // 맵이 화면보다 좁은 축은 중앙 고정.
        float x = minX <= maxX ? Mathf.Clamp(position.X, minX, maxX) : bounds.GetCenter().X;
        float y = minY <= maxY ? Mathf.Clamp(position.Y, minY, maxY) : bounds.GetCenter().Y;
        return new Vector2(x, y);
    }
}
