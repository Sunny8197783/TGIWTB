using System;

namespace PixelMmo.Combat;

/// <summary>
/// 손맛 수치 1개.
/// Default 는 §C 명세값으로 고정이고, Value 만 F3 튜닝 슬라이더가 실시간으로 바꾼다.
/// (손맛 튜닝을 코드 재컴파일로 하지 않기 위한 것 — §I)
/// float 로 암시적 변환되므로 전투 코드에서는 그냥 숫자처럼 쓴다.
/// </summary>
public sealed class TuningEntry
{
    public string Key { get; }
    public string Group { get; }
    public string Label { get; }
    public string Unit { get; }
    public float Default { get; }
    public float Min { get; }
    public float Max { get; }
    public float Step { get; }

    /// <summary>초 단위 시간 값이면 true — 슬라이더에 프레임 수를 같이 보여준다.</summary>
    public bool IsTime { get; }

    private float _value;

    public float Value
    {
        get => _value;
        set => _value = Math.Clamp(value, Min, Max);
    }

    /// <summary>60fps 기준 프레임 수. (CLAUDE.md 코드 규칙)</summary>
    public int Frames => (int)Math.Round(_value * 60f);

    public bool IsModified => Math.Abs(_value - Default) > 0.0001f;

    internal TuningEntry(string key, string group, string label, float defaultValue,
        float min, float max, float step, string unit, bool isTime)
    {
        Key = key;
        Group = group;
        Label = label;
        Default = defaultValue;
        Min = min;
        Max = max;
        Step = step;
        Unit = unit;
        IsTime = isTime;
        _value = defaultValue;
    }

    public void Reset() => _value = Default;

    public string Describe()
        => IsTime
            ? $"{Label} {_value:0.000}s ({Frames}f)"
            : $"{Label} {_value:0.##}{Unit}";

    public override string ToString() => _value.ToString("0.###");

    public static implicit operator float(TuningEntry entry) => entry._value;
}
