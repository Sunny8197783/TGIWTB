using System;
using System.Collections.Generic;

namespace PixelMmo.Core;

/// <summary>
/// 행적: 주인공이 한 일을 사건 이름으로 센다. 세상이 이름(직업)을 붙이는 근거이자 이야기 단계의 조건이다.
/// 무엇을 몇 번 해야 무엇이 열리는지는 data/** 만 안다 (규칙 4) — 여기는 세기만 한다.
/// 사건 이름: kill · kill:&lt;몬스터&gt; · kill_night · kill_hushed · parry · perfect_dodge · reflect ·
///           stele · statue · death · engrave:&lt;이름&gt; · boss:&lt;몬스터&gt; · flawless(맞지 않고 연속 처치 최고 기록)
/// </summary>
public sealed class Deeds
{
    private readonly Dictionary<string, int> _n;

    public Deeds(Dictionary<string, int> saved = null) => _n = saved != null ? new Dictionary<string, int>(saved) : new Dictionary<string, int>();

    /// <summary>사건이 세어질 때마다 (이름, 새 값)</summary>
    public event Action<string, int> Changed;

    public int Get(string key) => key != null && _n.TryGetValue(key, out int v) ? v : 0;

    public int Add(string key, int by = 1)
    {
        int v = Get(key) + by;
        _n[key] = v;
        Changed?.Invoke(key, v);
        return v;
    }

    /// <summary>최고 기록처럼 '더 크면 바꾸는' 값</summary>
    public void Max(string key, int value)
    {
        if (value <= Get(key))
            return;
        _n[key] = value;
        Changed?.Invoke(key, value);
    }

    public Dictionary<string, int> Snapshot() => new(_n);
}
