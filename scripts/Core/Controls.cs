using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 조작 키. 키보드(WASD/방향키 + J 공격, K 막기, Space·L 회피), 마우스(좌 공격, 우 막기), 패드(왼스틱, X 공격, LB 막기, B 회피).
/// project.godot 에 손으로 적는 대신 시작할 때 등록한다 — 한곳에서 읽히고 고치기 쉽다.
/// </summary>
public static class Controls
{
    public const string Left = "move_left", Right = "move_right", Up = "move_up", Down = "move_down";
    public const string Attack = "attack", Guard = "guard", Dodge = "dodge";

    public static void Register()
    {
        Action(Left, Key.A, Key.Left); Axis(Left, JoyAxis.LeftX, -1);
        Action(Right, Key.D, Key.Right); Axis(Right, JoyAxis.LeftX, 1);
        Action(Up, Key.W, Key.Up); Axis(Up, JoyAxis.LeftY, -1);
        Action(Down, Key.S, Key.Down); Axis(Down, JoyAxis.LeftY, 1);
        Action(Attack, Key.J); Mouse(Attack, MouseButton.Left); Pad(Attack, JoyButton.X);
        Action(Guard, Key.K); Mouse(Guard, MouseButton.Right); Pad(Guard, JoyButton.LeftShoulder);
        Action(Dodge, Key.Space, Key.L); Pad(Dodge, JoyButton.B);
    }

    private static void Action(string name, params Key[] keys)
    {
        if (!InputMap.HasAction(name))
            InputMap.AddAction(name, 0.25f);
        foreach (var k in keys)
            InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = k });
    }

    private static void Axis(string name, JoyAxis axis, float sign) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = sign });

    private static void Mouse(string name, MouseButton b) =>
        InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = b });

    private static void Pad(string name, JoyButton b) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = b });
}
