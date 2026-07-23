using System;
using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// 디버그 오버레이 하단 로그 패널이 읽는 링 버퍼. (§I)
/// 콘솔에도 같이 찍어서 오버레이를 안 켜도 추적할 수 있게 한다.
/// </summary>
public static class DebugLog
{
    /// <summary>패널에 보이는 줄 수는 12줄이지만, 스크롤 여유를 두고 담아 둔다.</summary>
    public static readonly int Capacity = 64;

    public static readonly int VisibleLines = 12;

    private static readonly List<string> Lines = new();

    public static event Action Changed;

    public static void Add(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        Lines.Add(line);
        if (Lines.Count > Capacity)
            Lines.RemoveRange(0, Lines.Count - Capacity);

        GD.Print($"[Log] {line}");
        Changed?.Invoke();
    }

    /// <summary>최근 count 줄을 오래된 것부터.</summary>
    public static IEnumerable<string> Recent(int count)
    {
        int start = Mathf.Max(0, Lines.Count - count);
        for (int i = start; i < Lines.Count; i++)
            yield return Lines[i];
    }

    public static void Clear()
    {
        Lines.Clear();
        Changed?.Invoke();
    }
}
