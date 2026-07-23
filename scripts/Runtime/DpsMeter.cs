using System.Collections.Generic;

namespace PixelMmo.Runtime;

/// <summary>최근 WindowSeconds 동안 넣은 피해의 초당 평균. (§I "최근 60초 DPS")</summary>
public sealed class DpsMeter
{
    public static readonly double WindowSeconds = 60.0;

    private readonly Queue<(double At, float Amount)> _samples = new();
    private float _total;

    public void Record(float amount, double nowSeconds)
    {
        _samples.Enqueue((nowSeconds, amount));
        _total += amount;
        Trim(nowSeconds);
    }

    public float Value(double nowSeconds)
    {
        Trim(nowSeconds);
        return (float)(_total / WindowSeconds);
    }

    public void Clear()
    {
        _samples.Clear();
        _total = 0f;
    }

    private void Trim(double nowSeconds)
    {
        while (_samples.Count > 0 && nowSeconds - _samples.Peek().At > WindowSeconds)
            _total -= _samples.Dequeue().Amount;
    }
}
