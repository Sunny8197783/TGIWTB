using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// Autoload "InputSetup". §C-8 키맵을 코드 한 곳에서 등록한다.
/// project.godot 의 [input] 섹션을 손으로 관리하지 않기 위한 것.
/// </summary>
public partial class InputSetup : Node
{
    // §C-8 키 맵
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Attack = "attack";        // J  — sk_slash
    public const string Guard = "guard";          // K  — sk_guard (홀드)
    public const string Bash = "bash";            // L  — sk_bash
    public const string Warcry = "warcry";        // U  — sk_warcry

    // 직업이 다섯이 되면서 스킬 키가 셋으로는 모자란다. 직업마다 네 개 이상을
    // 쓰므로 슬롯을 다섯으로 늘렸다 — J/L/U/I/O 가 한 손에 들어온다.
    public const string Skill4 = "skill_4";       // I
    public const string Skill5 = "skill_5";       // O

    /// <summary>NPC 에게 말을 건다.</summary>
    public const string Interact = "interact";    // F

    public const string Dash = "dash";            // Space
    public const string Run = "run";               // Shift — 달리기(홀드)
    public const string LockOn = "lock_on";        // Tab — 시선 고정 토글 (마우스 좌클릭으로 대상 지정)
    public const string Customize = "customize";   // C — 캐릭터 외형 편집
    public const string CharacterSheet = "character_sheet";
    public const string SkillBook = "skill_book";
    public const string DebugOverlay = "debug_overlay";   // F1
    public const string DebugHitbox = "debug_hitbox";     // F2
    public const string DebugTuning = "debug_tuning";     // F3
    public const string DebugAnim = "debug_anim";         // F4 — 애니메이션 상태 전이 로그
    public const string QuickSave = "quick_save";         // F5
    public const string QuickLoad = "quick_load";         // F9
    public const string DebugTestArena = "debug_test_arena"; // F8 — 손맛 테스트장(무적 + 고블린 무한 리스폰)
    public const string DebugTestArenaExit = "debug_test_arena_exit"; // F7 — 테스트장 나가기

    public override void _EnterTree()
    {
        Register();
    }

    /// <summary>액션이 없으면 만들고 키를 붙인다. 이미 있으면 건드리지 않는다.</summary>
    public static void Register()
    {
        Bind(MoveUp, Key.W, Key.Up);
        Bind(MoveDown, Key.S, Key.Down);
        Bind(MoveLeft, Key.A, Key.Left);
        Bind(MoveRight, Key.D, Key.Right);

        Bind(Attack, Key.J);
        var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left };
        if (!InputMap.ActionHasEvent(Attack, click)) InputMap.ActionAddEvent(Attack, click);
        Bind(Guard, Key.K);
        Bind(Bash, Key.L);
        Bind(Warcry, Key.U);
        Bind(Skill4, Key.I);
        Bind(Skill5, Key.O);
        Bind(Interact, Key.F);
        Bind(Dash, Key.Space);
        Bind(Run, Key.Shift);
        Bind(LockOn, Key.Tab);
        Bind(Customize, Key.C);
        Bind(CharacterSheet, Key.P);
        Bind(SkillBook, Key.B);

        Bind(DebugOverlay, Key.F1);
        Bind(DebugHitbox, Key.F2);
        Bind(DebugTuning, Key.F3);
        Bind(DebugAnim, Key.F4);
        Bind(QuickSave, Key.F5);
        Bind(QuickLoad, Key.F9);
        Bind(DebugTestArena, Key.F8);
        Bind(DebugTestArenaExit, Key.F7);
    }

    private static void Bind(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        foreach (Key key in keys)
        {
            var evt = new InputEventKey { PhysicalKeycode = key };
            if (!InputMap.ActionHasEvent(action, evt))
                InputMap.ActionAddEvent(action, evt);
        }
    }
}
