using Godot;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// §C-6 화면 연출의 단일 창구. 히트스톱·넉백은 각 엔티티가 처리하고,
/// 화면 흔들림 / 데미지 팝업 / 처치 파티클은 전부 여기를 거친다.
/// </summary>
public partial class CombatFeedback : Node2D
{
    public static CombatFeedback Instance { get; private set; }

    private GameCamera _camera;

    public override void _EnterTree()
    {
        Instance = this;
        Name = "CombatFeedback";
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Bind(GameCamera camera) => _camera = camera;

    /// <summary>플레이어가 적을 때렸을 때.</summary>
    public void OnHit(Vector2 worldPosition, float damage, bool heavy, bool killed)
    {
        Popup(worldPosition, damage, heavy, onPlayer: false);

        if (heavy)
            _camera?.ShakeHeavyHit();
        else
            _camera?.ShakeNormalHit();

        if (killed)
            _camera?.ShakeHeavyHit();
    }

    /// <summary>플레이어가 맞았을 때. 흔들림이 가장 크다. (§C-6)</summary>
    public void OnPlayerHurt(Vector2 worldPosition, float damage)
    {
        Popup(worldPosition, damage, heavy: false, onPlayer: true);
        _camera?.ShakePlayerHurt();
    }

    public void Popup(Vector2 worldPosition, float damage, bool heavy, bool onPlayer)
    {
        var popup = new DamagePopup { Position = worldPosition };
        popup.Setup(damage, heavy, onPlayer);
        AddChild(popup);
    }

    public void DeathBurstAt(Vector2 worldPosition, Color color)
    {
        var burst = new DeathBurst { Position = worldPosition };
        AddChild(burst);
        burst.Setup(color);
    }

    /// <summary>화면 중앙 한 줄. 조건 설명은 하지 않는다. (CLAUDE.md 규칙 4)</summary>
    public void Announce(string message)
    {
        if (GetParent() is GameWorld world)
            world.ShowAnnounce(message);
    }
}
