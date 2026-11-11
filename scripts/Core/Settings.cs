using System.Text.Json;
using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 설정 — user://settings.json (세이브와 따로: 새로 시작해도 남는다). 바꾸면 바로 적용하고 저장한다.
/// </summary>
public sealed class Settings
{
    public const string Path = "user://settings.json";

    /// <summary>전체 음량 0~10</summary>
    public int Volume { get; set; } = 8;
    /// <summary>화면 흔들림 0~10 (0 = 끔 — 멀미가 나는 사람을 위해)</summary>
    public int Shake { get; set; } = 10;
    public bool Fullscreen { get; set; }

    private static Settings _i;
    public static Settings Instance => _i ??= Load();

    /// <summary>화면 흔들림 배율 0~1 (Combat/CombatFx 가 곱한다)</summary>
    public static float ShakeScale => Instance.Shake / 10f;

    private static Settings Load()
    {
        if (!FileAccess.FileExists(Path))
            return new Settings();
        try
        {
            return JsonSerializer.Deserialize<Settings>(FileAccess.GetFileAsString(Path)) ?? new Settings();
        }
        catch (JsonException)
        {
            return new Settings();
        }
    }

    public void Apply()
    {
        Volume = Mathf.Clamp(Volume, 0, 10);
        Shake = Mathf.Clamp(Shake, 0, 10);
        AudioServer.SetBusMute(0, Volume == 0);
        AudioServer.SetBusVolumeDb(0, Volume == 0 ? -80f : Mathf.LinearToDb(Volume / 10f) * 1.2f);
        var want = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != want && !(want == DisplayServer.WindowMode.Windowed
                && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized))
            DisplayServer.WindowSetMode(want);
    }

    public void Save()
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this));
    }
}
