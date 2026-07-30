using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Combat;

/// <summary>
/// 허수아비 — 마을(안전 구역)의 훈련용 표적. 죽지도 움직이지도 않는다.
/// 몹을 잡으러 나가지 않고도 스킬 숙련을 올릴 수 있게 하는 연습대. (§F)
/// 같은 대상 반복 감쇠에서 면제되므로(IsTrainingDummy) 때린 만큼 꾸준히 오른다.
/// </summary>
public partial class TrainingDummy : MonsterBase
{
    public override MonsterStats Stats => MonsterTuning.TrainingDummy;

    public override bool IsTrainingDummy => true;
    protected override bool Immortal => true;
    protected override bool Immovable => true;

    protected override void UpdateAi(float delta)
    {
        // 아무것도 하지 않는다. 서서 맞아 주는 게 전부.
        Velocity = Vector2.Zero;
    }

    /// <summary>볏짚 인형 — 기둥 + 가로대 + 머리. 도형만. (규칙 5)</summary>
    protected override void DrawShape(Color color)
    {
        float r = Stats.Radius;
        var post = new Color(0.45f, 0.32f, 0.2f);   // 나무 기둥

        // 세로 기둥.
        DrawLine(new Vector2(0f, -r), new Vector2(0f, r + 4f), post, 3f);
        // 가로대(양팔).
        DrawLine(new Vector2(-r, -r * 0.2f), new Vector2(r, -r * 0.2f), post, 2.5f);
        // 볏짚 머리.
        DrawCircle(new Vector2(0f, -r), r * 0.6f, color);

        // 바닥 받침 — 안 밀린다는 걸 눈으로 알리는 짧은 밑동.
        DrawRect(new Rect2(-r * 0.5f, r + 3f, r, 2f), post);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        QueueRedraw();
    }
}
