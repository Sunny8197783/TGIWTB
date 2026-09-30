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
    private string _announce, _announceSub;
    private Color _announceColor;
    private float _announceAge = 99f;
    private Font _font;

    private const float EnemyBarShow = 3f;   // 맞은 뒤 체력바를 띄워 두는 시간 (초)
    private const float NumberLife = 0.7f;
    private const float AnnounceLife = 3.5f;
    // 한글은 3x5 로 못 찍는다 — 윈도우 굴림의 12px 비트맵 글자를 안티앨리어싱 없이 픽셀 그대로 키운다
    // ponytail: 시스템 글꼴 의존. 다른 OS 로 가면 OFL 픽셀 한글 글꼴(갈무리 등)을 넣는다
    private static readonly string[] FontNames = { "Gulim", "Dotum", "Malgun Gothic" };
    private const int FontPx = 12, SubFontPx = 12;

    private static readonly Color Outline = new(0.08f, 0.06f, 0.1f);
    private static readonly Color Back = new(0.22f, 0.16f, 0.2f);
    private static readonly Color HpHi = new(1f, 0.42f, 0.36f);
    private static readonly Color HpLo = new(0.78f, 0.18f, 0.22f);
    private static readonly Color Chip = new(1f, 0.93f, 0.8f);

    // 3x5 숫자 (위에서 아래로 한 줄씩, 1 = 칠함)
    // 스킬 칸 키 글자 (같은 3x5)
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['Q'] = "111101101111001", ['E'] = "111100111100111", ['R'] = "110101110101101",
    };
    private const string SlotKeys = "QER";
    private const int SlotSize = 20, SlotGap = 6;

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

    /// <summary>화면 위쪽 가운데 한 줄 알림 (진화·히든 습득). 조건은 절대 쓰지 않는다 (규칙 4).</summary>
    public static void Announce(string line, string sub, Color color)
    {
        if (_i == null || string.IsNullOrEmpty(line))
            return;
        _i._announce = line;
        _i._announceSub = sub;
        _i._announceColor = color;
        _i._announceAge = 0f;
    }

    public override void _EnterTree() => _i = this;

    public override void _Ready()
    {
        Name = "Hud";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
        _font = new SystemFont
        {
            FontNames = FontNames,
            Antialiasing = TextServer.FontAntialiasing.None,
            SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
        };
    }

    public void Bind(IPlayerContext player) => _player = player;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _announceAge += dt;
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
        {
            Bar(new Vector2(8, 8) * px, 72, 5, _player.Hp / _player.MaxHp, _chip, px);
            SkillSlots(px);
        }
        if (_announceAge < AnnounceLife)
            DrawAnnounce(px);

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

    /// <summary>화면 아래 가운데 스킬 칸: 스킬 색, 대기 중이면 위에서부터 어둡게 덮고, 준비되면 흰 테두리.</summary>
    private void SkillSlots(float px)
    {
        var skills = _player.Skills;
        Vector2 screen = GetViewportRect().Size;
        int n = skills.Count;
        float total = (n * SlotSize + (n - 1) * SlotGap) * px;
        Vector2 origin = Snap(new Vector2((screen.X - total) * 0.5f, screen.Y - (SlotSize + 12) * px), px);
        for (int i = 0; i < n; i++)
        {
            string id = skills[i];
            Vector2 pos = origin + new Vector2(i * (SlotSize + SlotGap) * px, 0f);
            var size = new Vector2(SlotSize, SlotSize) * px;
            if (id == null || !Data.SkillDef.All.TryGetValue(id, out var def))
            {
                DrawRect(new Rect2(pos, size), Back);
                continue;
            }
            var pal = Combat.CombatFx.PaletteOf(def.Palette);
            float cd = _player.CooldownRemaining(id);
            bool ready = cd <= 0f;
            DrawRect(new Rect2(pos - Vector2.One * px, size + Vector2.One * 2 * px), ready ? Colors.White : Outline);
            DrawRect(new Rect2(pos, size), pal.Deep);
            // 안쪽 무늬: 가운데 밝은 마름모 (스킬 색)
            for (int y = 0; y < SlotSize; y++)
            {
                int half = SlotSize / 2 - 3 - System.Math.Abs(y - SlotSize / 2);
                if (half <= 0)
                    continue;
                DrawRect(new Rect2(pos + new Vector2(SlotSize / 2 - half, y) * px, new Vector2(half * 2, 1) * px), y < SlotSize / 2 ? pal.Bright : pal.Mid);
            }
            if (!ready)
            {
                int covered = Mathf.CeilToInt(SlotSize * Mathf.Clamp(cd / def.Cooldown, 0f, 1f));
                DrawRect(new Rect2(pos, new Vector2(SlotSize, covered) * px), new Color(0f, 0f, 0f, 0.65f));
            }
            // 숙련: 칸 바로 아래 한 줄이 진화까지 차오른다
            float mastery = _player.MasteryProgress(id);
            if (mastery >= 0f)
            {
                Vector2 bar = pos + new Vector2(0, SlotSize + 1) * px;
                DrawRect(new Rect2(bar, new Vector2(SlotSize, 1) * px), Outline);
                DrawRect(new Rect2(bar, new Vector2(Mathf.RoundToInt(SlotSize * mastery), 1) * px), pal.Bright);
            }
            Glyph(Letters[SlotKeys[i]], pos + new Vector2(SlotSize / 2 - 1, SlotSize + 4) * px, px, Colors.White);
        }
    }

    /// <summary>톡 떨어지며 나타났다가 서서히 사라지는 두 줄. 저해상도 좌표로 찍고 px 배로 키운다.</summary>
    private void DrawAnnounce(float px)
    {
        float k = _announceAge;
        float alpha = Mathf.Min(k / 0.15f, 1f) * Mathf.Clamp((AnnounceLife - k) / 0.6f, 0f, 1f);
        int drop = Mathf.RoundToInt(6f * (1f - Mathf.Min(k / 0.2f, 1f)));
        float lowW = GetViewportRect().Size.X / px;
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * px);
        // 뒤에 어두운 띠 — 수풀·꽃밭 위에서도 읽히게
        float bandW = Mathf.Max(_font.GetStringSize(_announce, HorizontalAlignment.Left, -1, FontPx).X,
                                string.IsNullOrEmpty(_announceSub) ? 0f : _font.GetStringSize(_announceSub, HorizontalAlignment.Left, -1, SubFontPx).X) + 32f;
        var band = new Rect2(Mathf.Round(lowW * 0.5f - bandW * 0.5f), 50 - drop, Mathf.Round(bandW), string.IsNullOrEmpty(_announceSub) ? 20 : 36);
        DrawRect(band, new Color(0.05f, 0.04f, 0.08f, 0.55f * alpha));
        DrawRect(new Rect2(band.Position, new Vector2(band.Size.X, 1)), new Color(_announceColor, 0.7f * alpha));
        DrawRect(new Rect2(band.Position + new Vector2(0, band.Size.Y - 1), new Vector2(band.Size.X, 1)), new Color(_announceColor, 0.7f * alpha));
        Text(_announce, new Vector2(lowW * 0.5f, 64 - drop), FontPx, _announceColor, alpha);
        if (!string.IsNullOrEmpty(_announceSub))
            Text(_announceSub, new Vector2(lowW * 0.5f, 80 - drop), SubFontPx, Colors.White, alpha * 0.85f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>가운데 정렬 글자 + 사방 1픽셀 테두리 (저해상도 좌표)</summary>
    private void Text(string s, Vector2 center, int size, Color c, float alpha)
    {
        float w = _font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        var p = new Vector2(Mathf.Round(center.X - w * 0.5f), center.Y);
        var edge = new Color(Outline, alpha);
        foreach (var d in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
            DrawString(_font, p + d, s, HorizontalAlignment.Left, -1, size, edge);
        DrawString(_font, p, s, HorizontalAlignment.Left, -1, size, new Color(c, alpha));
    }

    private void Glyph(string g, Vector2 topLeft, float px, Color c)
    {
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 3; x++)
                if (g[y * 3 + x] == '1')
                    DrawRect(new Rect2(topLeft + new Vector2(x, y) * px, Vector2.One * px), c);
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
