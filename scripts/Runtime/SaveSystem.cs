using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// Autoload "SaveSystem". user://save_01.json 하나만 쓴다.
/// 로드 실패는 절대 크래시로 이어지지 않는다 — 경고 로그 + 새 게임 폴백. (§H)
/// </summary>
public partial class SaveSystem : Node
{
    public const int CurrentVersion = 1;
    public const string SavePath = "user://save_01.json";
    // The real serializer/file replacement is exercised without touching the user's slot.
    private static string ActivePath => HuntCheck.Requested ? "user://hunt-check.json" : SavePath;

    /// <summary>자동 저장 주기(초). (§H)</summary>
    public static readonly float AutoSaveIntervalSeconds = 60.0f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static SaveSystem Instance { get; private set; }

    /// <summary>저장/로드 결과를 디버그 오버레이로 흘려보낸다. (§I)</summary>
    public event Action<string> Logged;

    private double _autoSaveTimer;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public bool HasSave() => Godot.FileAccess.FileExists(ActivePath);

    /// <summary>마을 진입·60초 주기·F5 가 모두 이 경로로 들어온다.</summary>
    public bool Save(IPlayerContext player, string reason = "manual")
    {
        if (player == null || (DevCapture.IsRequested() && !(HuntCheck.Requested && reason=="hunt-check")) || ReferenceWorld3D.CheckRequested || (CharacterCreator.IsOpen && reason!="appearance"))
            return false;

        try
        {
            SaveData data = player.CaptureSave();
            data.Version = CurrentVersion;
            data.WorldRevision = WorldLayout.Revision;

            string json = JsonSerializer.Serialize(data, JsonOptions);
            string path=AbsoluteSavePath();
            if(System.IO.File.Exists(path) && !System.IO.File.Exists(path+".before-forest"))
                System.IO.File.Copy(path,path+".before-forest");
            System.IO.File.WriteAllText(path+".tmp",json);
            System.IO.File.Move(path+".tmp",path,true);
            _autoSaveTimer = 0.0;
            Report($"[Save] saved ({reason})");
            return true;
        }
        catch (Exception e)
        {
            Report($"[Save] 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>실패하면 null 을 돌려준다. 호출부는 새 게임으로 폴백한다.</summary>
    public SaveData Load()
    {
        if (!HasSave())
        {
            Report("[Save] 세이브 없음 — 새 게임");
            return null;
        }

        try
        {
            string json = Godot.FileAccess.GetFileAsString(ActivePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Report("[Save] 파일이 비어 있다 — 새 게임");
                return null;
            }

            var data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
            if (data == null)
            {
                Report("[Save] 파싱 결과가 비었다 — 새 게임");
                return null;
            }

            if (data.Version != CurrentVersion)
            {
                Report($"[Save] 버전 불일치 (파일 {data.Version} / 코드 {CurrentVersion}) — 새 게임");
                return null;
            }

            Report("[Save] loaded");
            return data;
        }
        catch (Exception e)
        {
            Report($"[Save] 로드 실패, 새 게임으로 폴백: {e.Message}");
            return null;
        }
    }

    /// <summary>로드 후 플레이어에 적용. 실패해도 크래시하지 않는다.</summary>
    public bool LoadInto(IPlayerContext player)
    {
        SaveData data = Load();
        if (data == null || player == null)
            return false;

        try
        {
            if (data.WorldRevision != WorldLayout.Revision)
            {
                data.Position.X = WorldLayout.SpawnPoint.X;
                data.Position.Y = WorldLayout.SpawnPoint.Y;
                data.WorldRevision = WorldLayout.Revision;
            }
            player.RestoreSave(data);
            return true;
        }
        catch (Exception e)
        {
            Report($"[Save] 적용 실패, 현재 상태 유지: {e.Message}");
            return false;
        }
    }

    /// <summary>월드가 매 프레임 호출한다. 60초마다 자동 저장. (§H)</summary>
    public void Tick(double delta, IPlayerContext player)
    {
        if (player == null || !player.IsAlive)
            return;

        _autoSaveTimer += delta;
        if (_autoSaveTimer >= AutoSaveIntervalSeconds)
        {
            _autoSaveTimer = 0.0;
            Save(player, "auto/60s");
        }
    }

    public void ResetAutoSaveTimer() => _autoSaveTimer = 0.0;

    public string AbsoluteSavePath() => ProjectSettings.GlobalizePath(ActivePath);

    private void Report(string message)
    {
        GD.Print(message);
        Logged?.Invoke(message);
    }
}
