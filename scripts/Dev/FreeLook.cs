using Godot;
using PixelMmo.Core;

namespace PixelMmo.Dev;

/// <summary>
/// 개발용 키: [ ] 시간. 주인공이 없으면(--no=hero) 방향키로 카메라를 옮긴다(Shift 빠르게). (V 전망은 CameraRig)
/// </summary>
public partial class FreeLook : Node
{
    public override void _Process(double delta)
    {
        var root = GameRoot.Instance;
        if (Combat.Hero.Instance == null)
            MoveRig(root, (float)delta);

        if (Input.IsKeyPressed(Key.Bracketright)) root.DayCycle.Hour = (root.DayCycle.Hour + (float)delta * 2f) % 24f;
        if (Input.IsKeyPressed(Key.Bracketleft)) root.DayCycle.Hour = (root.DayCycle.Hour + 24f - (float)delta * 2f) % 24f;
    }

    private static void MoveRig(GameRoot root, float delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D)) dir.X += 1;
        if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S)) dir.Y += 1;
        float speed = Input.IsKeyPressed(Key.Shift) ? 24f : 7f;
        var t = root.Rig.Target + new Vector3(dir.X, 0, dir.Y).Normalized() * speed * delta;
        t.Y = root.World.HeightAt(t.X, t.Z);
        root.Rig.Target = t;
    }
}
