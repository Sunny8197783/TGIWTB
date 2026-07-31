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

    /// <summary>플레이어가 실제로 넣은 피해량. 디버그 오버레이의 DPS 가 이걸 센다. (§I)</summary>
    public event System.Action<float> DamageDealt;

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
        DamageDealt?.Invoke(damage);
        Popup(worldPosition, damage, heavy, onPlayer: false);
        SparkAt(worldPosition, heavy);

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
        var popup = new DamagePopup();
        popup.Setup(damage, heavy, onPlayer);
        AddChild(popup);
        popup.GlobalPosition = worldPosition;
    }

    public void DeathBurstAt(Vector2 worldPosition, Color color)
    {
        var burst = new DeathBurst();
        AddChild(burst);
        burst.GlobalPosition = worldPosition;
        burst.Setup(color);
    }

    // --- 스킬 이펙트 (VFX) -------------------------------------------------

    /// <summary>베기 궤적 초승달. 스윙이 판정에 들어갈 때 소환.</summary>
    public void SlashAt(Vector2 worldPosition, float baseAngle, float fromOff, float toOff,
        float inner, float outer, Color color)
    {
        var fx = new SlashArc();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(baseAngle, fromOff, toOff, inner, outer, color, CombatTuning.VfxSlashLife);
    }

    /// <summary>타격 스파크. 유효타마다.</summary>
    public void SparkAt(Vector2 worldPosition, bool heavy)
    {
        var fx = new HitSpark();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(heavy, CombatTuning.VfxSparkLife);
    }

    /// <summary>충격파 링. 강타·함성 같은 큰 한 방.</summary>
    public void ShockAt(Vector2 worldPosition, float radius, Color color)
    {
        var fx = new ShockRing();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(radius, color, CombatTuning.VfxShockLife);
    }

    /// <summary>화면 중앙 한 줄. 조건 설명은 하지 않는다. (CLAUDE.md 규칙 4)</summary>
    public void Announce(string message)
    {
        if (GetParent() is GameWorld world)
            world.ShowAnnounce(message);
    }
}
