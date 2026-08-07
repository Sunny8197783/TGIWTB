namespace PixelMmo.Runtime;

/// <summary>F1~F3 디버그 토글. 게임 로직에는 영향을 주지 않는다. (§I)</summary>
public static class DebugFlags
{
    /// <summary>F1 — 좌상단 정보 + 하단 로그 패널.</summary>
    public static bool ShowOverlay;

    /// <summary>F2 — 히트박스/판정 범위 반투명 표시.</summary>
    public static bool ShowHitbox;

    /// <summary>F3 — 손맛 튜닝 슬라이더.</summary>
    public static bool ShowTuning;

    /// <summary>애니메이션 상태 전이를 로그에 찍는다. F4.</summary>
    public static bool ShowAnimStates;
}
