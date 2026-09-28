using Godot;
using PixelMmo.Core;

namespace PixelMmo.Dev;

/// <summary>
/// 주인공이 들어오기 전 임시 둘러보기: 방향키 이동, Shift 빠르게, [ ] 시간, V 전망 모드.
/// </summary>
public partial class FreeLook : Node
{
    public override void _Process(double delta)
    {
        var root = GameRoot.Instance;
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D)) dir.X += 1;
        if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S)) dir.Y += 1;
        float speed = Input.IsKeyPressed(Key.Shift) ? 24f : 7f;
        var t = root.Rig.Target + new Vector3(dir.X, 0, dir.Y).Normalized() * speed * (float)delta;
        t.Y = root.World.HeightAt(t.X, t.Z);
        root.Rig.Target = t;

        if (Input.IsKeyPressed(Key.Bracketright)) root.DayCycle.Hour = (root.DayCycle.Hour + (float)delta * 2f) % 24f;
        if (Input.IsKeyPressed(Key.Bracketleft)) root.DayCycle.Hour = (root.DayCycle.Hour + 24f - (float)delta * 2f) % 24f;
        bool vista = Input.IsKeyPressed(Key.V);
        root.Rig.Pitch = vista ? 14f : Px.PitchDeg;
        root.Rig.Distance = vista ? 30f : Px.FocusDistance;
    }
}
