using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 화면 위 정보: 주인공 체력(왼쪽 위), 맞은 적 머리 위 체력, 튀어 오르는 피해 숫자.
/// 전부 저해상도 픽셀 단위로 그려 월드와 같은 픽셀 크기로 보인다. 글자도 3x5 픽셀 숫자를 직접 찍는다.
/// </summary>
public partial class Hud : Control
{
    private static Hud _i;
    private IPlayerContext _player;
    private float _chip = 1f;      // 깎인 만큼 늦게 따라오는 흰 잔상
    private readonly List<(Vector3 at, int value, bool heavy, float age)> _numbers = new();

    private const float EnemyBarShow = 3f;   // 맞은 뒤 체력바를 띄워 두는 시간 (초)
    private const float NumberLife = 0.7f;

    private static readonly Color Outline = new(0.08f, 0.06f, 0.1f);
    private static readonly Color Back = new(0.22f, 0.16f, 0.2f);
    private static readonly Color HpHi = new(1f, 0.42f, 0.36f);
    private static readonly Color HpLo = new(0.78f, 0.18f, 0.22f);
    private static readonly Color Chip = new(1f, 0.93f, 0.8f);

    // 3x5 숫자 (위에서 아래로 한 줄씩, 1 = 칠함)
    private static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
    };

    public static void Damage(Vector3 at, float value, bool heavy)
    {
        if (_i != null)
            _i._numbers.Add((at, Mathf.RoundToInt(value), heavy, 0f));
    }

    public override void _EnterTree() => _i = this;

    public override void _Ready()
    {
        Name = "Hud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Bind(IPlayerContext player) => _player = player;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_player != null)
            _chip = Mathf.MoveToward(_chip, _player.Hp / _player.MaxHp, dt * (_chip > _player.Hp / _player.MaxHp ? 0.6f : 5f));
        for (int i = _numbers.Count - 1; i >= 0; i--)
        {
            var n = _numbers[i];
            n.age += dt;
            if (n.age > NumberLife)
                _numbers.RemoveAt(i);
            else
                _numbers[i] = n;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;

        if (_player != null)
            Bar(new Vector2(8, 8) * px, 72, 5, _player.Hp / _player.MaxHp, _chip, px);

        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var e in Combat.Enemy.All)
        {
            if (!e.Alive || now - e.LastHitAt > EnemyBarShow)
                continue;
            Vector2 head = view.WorldToScreen(e.GlobalPosition + Vector3.Up * (e.Radius * 3.2f + 0.35f));
            Bar(Snap(head - new Vector2(9, 0) * px, px), 18, 2, e.HpRatio, e.HpRatio, px);
        }

        foreach (var (at, value, heavy, age) in _numbers)
        {
            float k = age / NumberLife;
            // 톡 튀어 올랐다가 살짝 가라앉으며 사라진다
            float rise = 14f * Mathf.Sin(Mathf.Min(k * 2.2f, 1f) * Mathf.Pi * 0.5f) - (k > 0.6f ? (k - 0.6f) * 8f : 0f);
            int scale = heavy ? 3 : 2;
            Color c = heavy ? new Color(1f, 0.85f, 0.3f) : Colors.White;
            if (k > 0.75f && Mathf.PosMod(age, 0.08f) < 0.04f)
                continue; // 사라지기 전 깜빡
            Vector2 p = view.WorldToScreen(at) - new Vector2(0, rise) * px;
            Number(Snap(p, px), value, scale, c, px);
        }
    }

    private void Bar(Vector2 pos, int w, int h, float ratio, float chip, float px)
    {
        DrawRect(new Rect2(pos - Vector2.One * px, new Vector2(w + 2, h + 2) * px), Outline);
        DrawRect(new Rect2(pos, new Vector2(w, h) * px), Back);
        int chipW = Mathf.RoundToInt(w * Mathf.Clamp(chip, 0f, 1f));
        int fillW = Mathf.RoundToInt(w * Mathf.Clamp(ratio, 0f, 1f));
        if (chipW > fillW)
            DrawRect(new Rect2(pos + new Vector2(fillW, 0) * px, new Vector2(chipW - fillW, h) * px), Chip);
        if (fillW > 0)
        {
            DrawRect(new Rect2(pos, new Vector2(fillW, h) * px), HpLo);
            DrawRect(new Rect2(pos, new Vector2(fillW, Mathf.Max(1, h / 2)) * px), HpHi); // 위쪽 밝은 줄
        }
    }

    private void Number(Vector2 center, int value, int scale, Color c, float px)
    {
        string s = value.ToString();
        float cell = px * scale;
        float width = (s.Length * 4 - 1) * cell;
        Vector2 origin = center - new Vector2(width * 0.5f, 5 * cell * 0.5f);
        // 테두리 먼저 (한 픽셀 두께로 사방), 그다음 글자
        for (int pass = 0; pass < 2; pass++)
        {
            for (int d = 0; d < s.Length; d++)
            {
                string g = Digits[s[d] - '0'];
                for (int y = 0; y < 5; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        if (g[y * 3 + x] != '1')
                            continue;
                        Vector2 p = origin + new Vector2(d * 4 + x, y) * cell;
                        if (pass == 0)
                            DrawRect(new Rect2(p - Vector2.One * px, new Vector2(cell + 2 * px, cell + 2 * px)), Outline);
                        else
                            DrawRect(new Rect2(p, new Vector2(cell, cell)), c);
                    }
                }
            }
        }
    }

    private static Vector2 Snap(Vector2 p, float px) => (p / px).Round() * px;
}
