namespace PixelMmo.Combat;

/// <summary>물리 레이어 비트. project.godot 대신 여기서 이름을 관리한다.</summary>
public static class CollisionLayers
{
    /// <summary>타일맵 벽.</summary>
    public const uint World = 1 << 0;

    public const uint Player = 1 << 1;
    public const uint Monster = 1 << 2;
    public const uint Projectile = 1 << 3;
}
