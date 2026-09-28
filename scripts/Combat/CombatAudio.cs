using Godot;

namespace PixelMmo.Combat;

/// <summary>Bounded positional voice pool; simultaneous area hits cannot multiply volume indefinitely.</summary>
public partial class CombatAudio : Node2D
{
    private readonly AudioStreamPlayer2D[] _voices = new AudioStreamPlayer2D[8];
    private readonly System.Collections.Generic.Dictionary<string, AudioStream> _clips = new();
    private readonly System.Collections.Generic.Dictionary<string, ulong> _last = new();
    private int _next;

    public override void _Ready()
    {
        foreach (string key in new[] { "swing", "hit", "heavy", "hurt", "block", "parry", "kill" })
            _clips[key] = GD.Load<AudioStream>($"res://art/audio/{key}.wav");
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AudioStreamPlayer2D { MaxDistance = 600f, VolumeDb = -9f };
            AddChild(_voices[i]);
        }
    }

    public void Play(string key, Vector2 at)
    {
        ulong now = Time.GetTicksMsec();
        if (_last.TryGetValue(key, out ulong last) && now - last < 45) return;
        if (!_clips.TryGetValue(key, out var stream) || stream == null) return;
        _last[key] = now;
        var voice = _voices[_next++ % _voices.Length];
        voice.Stop();
        voice.GlobalPosition = at;
        voice.Stream = stream;
        voice.PitchScale = (float)GD.RandRange(0.96, 1.04);
        voice.Play();
    }
}
