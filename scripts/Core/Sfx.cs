using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 효과음. res://audio/sfx 의 파일을 이름 뒤 번호를 뗀 '묶음'으로 모은다
/// (footstep_grass_000..004 → "footstep_grass"). 부를 때마다 묶음에서 하나를 고르고 음높이를 조금 흔든다 —
/// 같은 소리가 똑같이 반복되면 기계음처럼 들린다.
///
/// 버스: SFX(세계 소리, 슬로우모션 때 저역통과가 걸린다), Hero(주인공 소리 — 슬로우 중에도 또렷), UI.
/// 소리 출처: Kenney.nl (CC0) — audio/KENNEY_LICENSE.txt
/// </summary>
public partial class Sfx : Node
{
    private const string Folder = "res://audio/sfx";
    private const int Voices = 24;

    private static Sfx _instance;
    private readonly Dictionary<string, List<AudioStream>> _groups = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private int _next;
    private static readonly RandomNumberGenerator Rng = new();
    /// <summary>--no=sfx : 성능 A/B 용</summary>
    public static readonly bool Muted = Dev.DevCapture.Disabled().Contains("sfx");

    /// <summary>슬로우모션 때 켜는 먹먹함 (SFX 버스의 저역통과).</summary>
    public static AudioEffectLowPassFilter Muffle { get; private set; }

    public override void _EnterTree()
    {
        _instance = this;
        Name = "Sfx";
        SetupBuses();
        var tail = new Regex(@"_?\d+$");
        foreach (string file in ResourceLoader.ListDirectory(Folder))
        {
            if (!file.EndsWith(".ogg") && !file.EndsWith(".wav"))
                continue;
            string key = tail.Replace(file[..^4], "");
            if (!_groups.TryGetValue(key, out var list))
                _groups[key] = list = new List<AudioStream>();
            list.Add(GD.Load<AudioStream>($"{Folder}/{file}"));
        }
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(p);
            _players.Add(p);
        }
    }

    /// <param name="group">파일 묶음 이름 (번호 뗀 것)</param>
    /// <param name="db">음량 (dB)</param>
    /// <param name="pitch">기준 음높이 배율</param>
    /// <param name="jitter">음높이 흔들기 (±비율)</param>
    public static void Play(string group, float db = 0f, float pitch = 1f, float jitter = 0.06f, string bus = "SFX")
    {
        var s = _instance;
        if (Muted)
            return;
        if (s == null || !s._groups.TryGetValue(group, out var list))
        {
            GD.PushWarning($"[Sfx] 없는 소리: {group}");
            return;
        }
        // 가장 오래전에 쓴 목소리를 뺏는다
        var p = s._players[s._next];
        s._next = (s._next + 1) % s._players.Count;
        p.Stream = list[Rng.RandiRange(0, list.Count - 1)];
        p.VolumeDb = db;
        p.PitchScale = pitch * (1f + Rng.RandfRange(-jitter, jitter));
        p.Bus = bus;
        p.Play();
    }

    private static void SetupBuses()
    {
        if (AudioServer.GetBusIndex("SFX") >= 0)
            return;
        foreach (string name in new[] { "SFX", "Hero", "UI" })
        {
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, name);
            AudioServer.SetBusSend(i, "Master");
        }
        int sfx = AudioServer.GetBusIndex("SFX");
        Muffle = new AudioEffectLowPassFilter { CutoffHz = 900f };
        AudioServer.AddBusEffect(sfx, Muffle);
        AudioServer.SetBusEffectEnabled(sfx, 0, false);
    }

    public static void SetMuffled(bool on)
    {
        int sfx = AudioServer.GetBusIndex("SFX");
        if (sfx >= 0)
            AudioServer.SetBusEffectEnabled(sfx, 0, on);
    }
}
