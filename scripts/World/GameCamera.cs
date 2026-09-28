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

    /// <summary>
    /// 카메라 확대. 1280x720 / 32px 타일 기준 가로 13칸이 들어온다.
    ///
    /// 6.0 이었다 — 640x360 에 16px 타일이던 시절 값이고, 타일이 32px 로 커질 때
    /// 같이 내려오지 않았다. 그래서 화면에 6칸밖에 안 들어와 분수 하나가 화면
    /// 절반을 먹었다. 탑다운 MMO 는 최소한 건물 몇 채와 그 사이 길이 한 화면에
    /// 같이 보여야 어디로 갈지 정할 수 있다.
    /// </summary>
    public static readonly float ZoomLevel = 3.0f;

    /// <summary>맵 밖 테스트장(§F8)처럼 WorldBounds 바깥을 비출 때 클램프를 끈다.</summary>
    public bool ClampToWorld = true;

    private Node2D _target;
    private float _shakeAmplitude;
    private float _shakeTimer;
    private float _shakeDuration;
    private Vector2 _shakeDirection;

    public override void _Ready()
    {
        Name = "GameCamera";
        Zoom = new Vector2(ZoomLevel, ZoomLevel);
        PositionSmoothingEnabled = false; // lerp 를 직접 돌린다.
        MakeCurrent();
    }

    public void Follow(Node2D target)
    {
        _target = target;
        if (target != null)
            GlobalPosition = Clamp(target.GlobalPosition);
    }

    /// <summary>
    /// 진폭(px)과 지속 시간(초). 이미 흔들리는 중이면 더 센 쪽이 이긴다.
    /// direction 을 주면 그 방향으로 밀렸다가 돌아온다(예: 내리찍기는 아래로) —
    /// 순수 무작위 지터보다 "부딪힌 방향"이 읽혀야 타격감이 산다.
    /// </summary>
    public void Shake(float amplitude, float duration, Vector2 direction = default)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;

        float remaining = _shakeDuration > 0f ? _shakeAmplitude * _shakeTimer / _shakeDuration : 0f;
        if (amplitude >= remaining)
        {
            _shakeAmplitude = amplitude;
            _shakeDuration = duration;
            _shakeTimer = duration;
            _shakeDirection = direction;
        }
    }

    public void ShakeNormalHit(Vector2 direction = default)
        => Shake(CombatTuning.ShakeNormalAmp, CombatTuning.ShakeNormalTime, direction);

    public void ShakeHeavyHit(Vector2 direction = default)
        => Shake(CombatTuning.ShakeHeavyAmp, CombatTuning.ShakeHeavyTime, direction);

    public void ShakePlayerHurt(Vector2 direction = default)
        => Shake(CombatTuning.ShakeHurtAmp, CombatTuning.ShakeHurtTime, direction);

    /// <summary>퍼펙트 가드 순간. 강타격보다 확실히 크게 — 화면 전체가 흔들려야 "막았다"가 읽힌다.</summary>
    public void ShakeParry()
        => Shake(CombatTuning.ShakeParryAmp, CombatTuning.ShakeParryTime);

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
        if (_shakeTimer <= 0f || CombatTuning.ShakeStrength <= 0f)
        {
            _shakeTimer = _shakeAmplitude = 0f;
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
        float amp = _shakeAmplitude * falloff * falloff * CombatTuning.ShakeStrength;
        float phase = (_shakeDuration - _shakeTimer) * Mathf.Tau * CombatTuning.ShakeFrequency;
        Vector2 n = _shakeDirection == Vector2.Zero ? Vector2.Down : _shakeDirection.Normalized();
        Vector2 perp = new(-n.Y, n.X);
        Offset = (n * Mathf.Cos(phase) + perp * (Mathf.Sin(phase * .75f) * .25f)) * amp;
    }

    /// <summary>카메라가 맵 밖을 비추지 않게 가둔다. 테스트장에서는 ClampToWorld=false 로 끈다.</summary>
    private Vector2 Clamp(Vector2 position)
    {
        if (!ClampToWorld)
            return position;

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
