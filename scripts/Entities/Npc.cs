using Godot;
using PixelMmo.Data;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// 마을 사람 하나. 움직이지 않고, 맞지도 않고, 말만 한다.
///
/// 물리체가 아니라 그냥 Node2D 다 — 충돌체를 붙이면 좁은 골목에서 길을 막고,
/// 막힌 NPC 를 피해 돌아가게 만드는 것은 P1 의 검증 질문과 아무 상관이 없다.
/// 상호작용은 <see cref="Nearest"/> 의 거리 검사 하나로 끝난다.
/// </summary>
public partial class Npc : Node2D
{
    public const string Group = "npc";

    /// <summary>말을 걸 수 있는 거리(px). 타일 2칸 남짓.</summary>
    private static readonly float TalkRange = 56f;

    /// <summary>몸통 반지름(px). 플레이어와 비슷한 덩치로 보이게.</summary>
    private static readonly float BodyRadius = 7f;

    /// <summary>
    /// 이름표 글자 크기(화면 픽셀).
    ///
    /// 월드 단위로 7 을 주면 글립을 7px 로 구운 다음 카메라 줌 3배로 늘리므로
    /// 뭉개져서 회색 얼룩이 된다. 화면 크기로 굽고 그린 다음 1/줌 으로 줄여야
    /// 또렷하다.
    /// </summary>
    private const int NameFontSize = 20;

    public NpcDefinition Def { get; private set; }

    private Color _color = Colors.White;
    private int _line;

    /// <summary>플레이어와 같은 스프라이트를 쓴다. 없으면 아래 도형으로 떨어진다.</summary>
    private float _breath;

    /// <summary>역할별 기본색. json 에 color 가 있으면 그쪽이 이긴다.</summary>
    private static Color RoleColor(string role) => role switch
    {
        "merchant" => new Color(0.82f, 0.66f, 0.28f),
        "quest" => new Color(0.86f, 0.82f, 0.52f),
        "guard" => new Color(0.42f, 0.50f, 0.70f),
        "trainer" => new Color(0.72f, 0.36f, 0.34f),
        _ => new Color(0.62f, 0.58f, 0.52f),
    };

    public void Bind(NpcDefinition def)
    {
        Def = def;
        _color = PlayerCharacter.ParseColor(def.Color, RoleColor(def.Role));
    }

    public override void _Ready()
    {
        Name = Def?.Id ?? "Npc";
        AddToGroup(Group);
        ZIndex = 0;      // 건물·플레이어와 같은 층. 앞뒤는 Y 정렬이 가른다.

        // 이름은 Label 이 아니라 _Draw 에서 직접 찍는다. Label 은 Control 이라
        // 카메라 줌을 그대로 받지 않아 글자가 제 크기로 안 나왔고, NPC 열여섯이면
        // 쓰지도 않는 Control 노드가 열여섯 개 붙는다.
    }

    public override void _Process(double delta)
    {
        // 서서 숨만 쉰다. 남쪽(화면 아래)을 보게 두면 지나가는 사람과 눈이 맞는다.
        _breath += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 발밑 그림자는 스프라이트가 있어도 그린다 — 땅에 붙어 보이게 하는 건 이쪽이다.
        DrawCircle(new Vector2(0f, BodyRadius * 0.6f), BodyRadius * 0.9f, new Color(0f, 0f, 0f, 0.25f));

        string role = Def?.Role is "merchant" or "guard" or "trainer" ? Def.Role : "quest";
        bool loaded = ActorArt.Draw(this, "npc_" + role, Vector2.Down, 28f, 7f,
            Colors.White, new Vector2(1f, 1f + Mathf.Sin(_breath * 2f) * .015f));
        if (!loaded)
        {
            var dark = new Color(_color.R * 0.6f, _color.G * 0.6f, _color.B * 0.6f);
            DrawRect(new Rect2(-BodyRadius * 0.8f, -BodyRadius, BodyRadius * 1.6f, BodyRadius * 2f), _color);
            DrawCircle(new Vector2(0f, -BodyRadius - 3f), BodyRadius * 0.62f, dark);
        }

        DrawName();
    }

    /// <summary>머리 위 이름표. 검은 테를 두 겹 겹쳐 어떤 바닥 위에서도 읽히게 한다.</summary>
    private void DrawName()
    {
        string text = Def?.Name;
        if (string.IsNullOrEmpty(text))
            return;

        var font = ThemeDB.FallbackFont;
        if (font == null)
            return;

        float zoom = GameCamera.ZoomLevel;
        float width = font.GetStringSize(text, fontSize: NameFontSize).X;

        // 글자만 화면 배율로 그린다. 노드 자체는 월드에 있으므로 위치는 그대로 따라간다.
        DrawSetTransform(new Vector2(0f, -BodyRadius - 14f), 0f, Vector2.One / zoom);

        var at = new Vector2(-width * 0.5f, 0f);

        // DrawString 에 외곽선 옵션이 없어서 여덟 방향으로 한 번씩 더 찍는다.
        for (int dy = -2; dy <= 2; dy += 2)
        {
            for (int dx = -2; dx <= 2; dx += 2)
            {
                if (dx == 0 && dy == 0)
                    continue;
                DrawString(font, at + new Vector2(dx, dy), text, HorizontalAlignment.Left,
                    -1f, NameFontSize, new Color(0f, 0f, 0f, 0.85f));
            }
        }

        DrawString(font, at, text, HorizontalAlignment.Left, -1f, NameFontSize,
            new Color(0.95f, 0.93f, 0.85f));

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// 다음 대사 한 줄. 마지막까지 갔으면 null 을 돌려주고 처음으로 되감는다 —
    /// 되감아 두지 않으면 한 번 말을 건 NPC 가 두 번째부터 벙어리가 된다.
    /// </summary>
    public string NextLine()
    {
        if (Def == null || Def.Lines.Count == 0)
            return null;

        if (_line >= Def.Lines.Count)
        {
            _line = 0;
            return null;
        }

        return Def.Lines[_line++];
    }

    /// <summary>대사를 처음부터 다시 듣게 한다.</summary>
    public void Rewind() => _line = 0;

    /// <summary>말 걸 수 있는 거리 안의 가장 가까운 NPC. 없으면 null.</summary>
    public static Npc Nearest(Node fromTree, Vector2 worldPosition)
    {
        Npc best = null;
        float bestDistance = TalkRange * TalkRange;

        foreach (Node node in fromTree.GetTree().GetNodesInGroup(Group))
        {
            if (node is not Npc npc)
                continue;

            float d = npc.GlobalPosition.DistanceSquaredTo(worldPosition);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = npc;
            }
        }

        return best;
    }
}
