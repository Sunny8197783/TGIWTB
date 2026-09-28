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
    private CombatAudio _audio;

    public override void _Ready()
    {
        _audio = new CombatAudio();
        AddChild(_audio);
    }

    public void PlaySound(string key, Vector2 at) => _audio?.Play(key, at);

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
    public void OnHit(Vector2 worldPosition, float damage, bool heavy, bool killed, Vector2 direction = default)
    {
        PlaySound(killed ? "kill" : heavy ? "heavy" : "hit", worldPosition);
        DamageDealt?.Invoke(damage);
        Popup(worldPosition, damage, heavy, onPlayer: false);
        SparkAt(worldPosition, heavy);

        // 카메라 기준 때린 방향으로 화면이 밀린다 — 무작위 지터보다 "부딪힌 방향"이 읽힌다.
        Vector2 dir = direction != Vector2.Zero ? direction
            : _camera != null ? worldPosition - _camera.GlobalPosition : Vector2.Zero;

        if (heavy)
            _camera?.ShakeHeavyHit(dir);
        else
            _camera?.ShakeNormalHit(dir);

        if (killed)
            _camera?.ShakeHeavyHit(dir);
    }

    /// <summary>플레이어가 맞았을 때. 흔들림이 가장 크다. (§C-6)</summary>
    public void OnPlayerHurt(Vector2 worldPosition, float damage, Vector2 direction)
    {
        PlaySound("hurt", worldPosition);
        SparkAt(worldPosition, false);
        Popup(worldPosition, damage, heavy: false, onPlayer: true);
        _camera?.ShakePlayerHurt(direction);
    }

    /// <summary>퍼펙트 가드 순간. 강타격 카운터와 같은 프레임에 겹쳐도 더 큰 쪽(Shake)이 이긴다.</summary>
    public void OnParry()
    {
        _camera?.ShakeParry();
        PlaySound("parry", _camera?.GlobalPosition ?? GlobalPosition);
    }

    public void Popup(Vector2 worldPosition, float damage, bool heavy, bool onPlayer)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Popup(worldPosition, damage, heavy, onPlayer); return; }
        var popup = new DamagePopup();
        popup.Setup(damage, heavy, onPlayer);
        AddChild(popup);
        popup.GlobalPosition = worldPosition;
    }

    public void DeathBurstAt(Vector2 worldPosition, Color color)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Ring(worldPosition, 25f, color, .35f); return; }
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
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Ring(worldPosition, outer, color, CombatTuning.VfxSlashLife, baseAngle + (fromOff + toOff) * .5f,
            Mathf.Abs(toOff - fromOff), outer > 0f ? inner / outer : .65f); return; }
        var fx = new SlashArc();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(baseAngle, fromOff, toOff, inner, outer, color, CombatTuning.VfxSlashLife);
    }

    /// <summary>타격 스파크. 유효타마다.</summary>
    public void SparkAt(Vector2 worldPosition, bool heavy)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Impact(worldPosition, heavy); return; }
        var fx = new HitSpark();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(heavy, CombatTuning.VfxSparkLife);
    }

    /// <summary>충격파 링. 강타·함성 같은 큰 한 방.</summary>
    public void ShockAt(Vector2 worldPosition, float radius, Color color)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Ring(worldPosition, radius, color, CombatTuning.VfxShockLife); return; }
        var fx = new ShockRing();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(radius, color, CombatTuning.VfxShockLife);
    }

    /// <summary>대시 바람 가르기.</summary>
    public void DashWindAt(Vector2 worldPosition, Vector2 direction, Color color)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Ring(worldPosition, 19f, color, CombatTuning.VfxDashWindLife, direction.Angle() + Mathf.Pi, Mathf.Pi); return; }
        var fx = new DashWind();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(direction, color, CombatTuning.VfxDashWindLife);
    }

    /// <summary>강타 원뿔(꼬깔). 돌진 방향으로 뻗는다.</summary>
    public void ConeAt(Vector2 worldPosition, Vector2 direction, Color color)
    {
        if (PixelMmo.Runtime.ReferenceWorld3D.Instance is { } view)
        { view.Ring(worldPosition, CombatTuning.VfxConeLength, color, CombatTuning.VfxConeLife, direction.Angle(), Mathf.Pi * .55f, .15f); return; }
        var fx = new BashCone();
        AddChild(fx);
        fx.GlobalPosition = worldPosition;
        fx.Setup(direction, CombatTuning.VfxConeLength, CombatTuning.VfxConeHalfWidth,
            color, CombatTuning.VfxConeLife);
    }

    /// <summary>화면 중앙 한 줄. 조건 설명은 하지 않는다. (CLAUDE.md 규칙 4)</summary>
    public void Announce(string message)
    {
        if (GetParent() is GameWorld world)
            world.ShowAnnounce(message);
    }
}
