using Godot;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

/// <summary>
/// 메인 씬. Autoload 가 전부 올라온 것을 확인하고 월드로 넘긴다.
/// 월드 씬이 아직 없으면(M1) 부팅 상태만 화면에 찍는다.
/// </summary>
public partial class Boot : Node2D
{
    private const string WorldScenePath = "res://scenes/World.tscn";

    public override void _Ready()
    {
        if(System.Array.IndexOf(OS.GetCmdlineUserArgs(),"--reproduce-raw-contour")>=0)
        { GetTree().Quit(SpriteContourChecks.ReproduceRaw()?0:1);return; }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--combat-check") >= 0)
        {
            AddChild(new CombatChecks());
            return;
        }
        var db = GameDatabase.Instance;
        if (db == null)
        {
            GD.PushError("[Boot] GameDb autoload 을 찾을 수 없다.");
            return;
        }

        GD.Print($"[Boot] ok — save={SaveSystem.Instance?.AbsoluteSavePath()}");

        if (SelfCheck.Requested())
        {
            RunSelfCheck(db);
            return;
        }

        if (ResourceLoader.Exists(WorldScenePath))
        {
            CallDeferred(nameof(GoToWorld));
            return;
        }

        ShowBootSummary(db);
    }

    /// <summary>--selfcheck. 결과를 찍고 바로 종료한다 (실패 있으면 코드 1).</summary>
    private void RunSelfCheck(GameDatabase db)
    {
        var fail = SelfCheck.Run(db);
        foreach (string line in fail)
            GD.PrintErr($"[selfcheck] {line}");

        GD.Print(fail.Count == 0
            ? $"[selfcheck] ok — jobs={db.Jobs.Count} skills={db.Skills.Count} npcs={db.Npcs.Count}"
            : $"[selfcheck] 실패 {fail.Count}건");

        GetTree().Quit(fail.Count == 0 ? 0 : 1);
    }

    private void GoToWorld()
    {
        GetTree().ChangeSceneToFile(WorldScenePath);
    }

    private void ShowBootSummary(GameDatabase db)
    {
        var label = new Label
        {
            Position = new Vector2(12, 12),
            Text = $"PixelMmo — M1 boot ok\njobs={db.Jobs.Count}  skills={db.Skills.Count}",
        };
        AddChild(label);
    }
}
