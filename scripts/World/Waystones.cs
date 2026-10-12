using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 전송석 (M10): 하루미 마을 광장 1 + 지역 입구 6 + 왕국 6 (지도 meta.waystones, tools/mapgen).
/// 앞에 서서 F(패드 A) → 부활 지점이 되고 체력이 차고, 이동 목록이 열린다.
/// 레벨이 요구 레벨에 닿으면 가 본 적 없어도 간다 — 모자라면 회색 "Lv N" (넓은 들판을 다시 걷지 않게, 강해질수록 멀리).
/// 목록: ↑↓ 고르기, 공격·F 이동, 회피·Esc 닫기.
/// </summary>
public partial class Waystones : Control
{
    private readonly record struct Stone(string Id, string Name, int Level, Vector3 Front, Vector3 Top);

    private const float Reach = 3.2f;          // 쓸 수 있는 거리 (m, 돌 앞자리에서)
    private const float FrontOffset = 1.8f;    // 돌 앞자리: 받침 남쪽 (m)
    private const int RowH = 14;               // 목록 한 줄 (화면 픽셀)
    private static readonly Color Sky = new(0.62f, 0.86f, 1f);
    private static readonly Color Back = new(0.05f, 0.06f, 0.1f, 0.88f);
    private static readonly Color Dim = new(0.62f, 0.64f, 0.7f);
    private static readonly Color Locked = new(0.42f, 0.42f, 0.46f);

    private readonly List<Stone> _all = new();
    private IPlayerContext _player;
    private int _near = -1;
    private bool _menu;
    private int _pick;
    private float _age;

    public Waystones(WorldData world)
    {
        Name = "Waystones";
        if (!world.Meta.TryGetProperty("waystones", out var arr))
            return;
        foreach (var s in arr.EnumerateArray())
        {
            float x = s.GetProperty("x").GetSingle(), z = s.GetProperty("z").GetSingle();
            float fz = z + FrontOffset;
            _all.Add(new Stone(s.GetProperty("id").GetString(), s.GetProperty("name").GetString(), s.GetProperty("level").GetInt32(),
                new Vector3(x, world.HeightAt(x, fz), fz), new Vector3(x, world.HeightAt(x, z) + 3.4f, z + 0.5f)));
        }
        // 목록은 레벨 순 (같으면 지도 순) — 위에서부터 갈 수 있는 곳
        _all.Sort((a, b) => a.Level.CompareTo(b.Level));
    }

    public Waystones() { }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Bind(IPlayerContext player) => _player = player;

    /// <summary>저장된 부활 전송석의 앞자리 (없으면 null) — 켤 때 여기서 시작한다</summary>
    public Vector3? RespawnFeet(IPlayerContext player)
    {
        foreach (var s in _all)
            if (s.Id == player.RespawnStatue)
                return s.Front;
        return null;
    }

    public override void _Process(double delta)
    {
        if (_player == null)
            return;
        _age += (float)delta;
        if (_menu)
        {
            MenuInput();
            QueueRedraw();
            return;
        }
        _near = -1;
        var at = _player.WorldPosition;
        for (int i = 0; i < _all.Count; i++)
        {
            var f = _all[i].Front;
            if (new Vector2(f.X - at.X, f.Z - at.Z).Length() < Reach)
                _near = i;
        }
        if (_near >= 0 && _player.IsAlive && !GameRoot.Instance.InputLocked && Input.IsActionJustPressed(Controls.Interact))
            Use(_all[_near]);
        QueueRedraw();
    }

    private void Use(Stone s)
    {
        _player.RestAt(s.Id, s.Front);
        CombatFx.SparkleBurst(s.Top - Vector3.Up, 24, 1.8f, CombatFx.PaletteOf("moon"));
        Sfx.Play("impactBell_heavy", -12f, 1.6f, 0f, "UI");
        Hud.Announce(s.Name, "전송석 — 부활 지점", Sky);
        _pick = _near;
        _menu = true;
        _age = 0f;
        GameRoot.Instance.MenuOpen = true;
    }

    private void CloseMenu()
    {
        _menu = false;
        GameRoot.Instance.MenuOpen = false;
    }

    private void MenuInput()
    {
        if (Input.IsActionJustPressed(Controls.Up))
            Step(-1);
        if (Input.IsActionJustPressed(Controls.Down))
            Step(1);
        if (Input.IsActionJustPressed(Controls.Attack) || Input.IsActionJustPressed(Controls.Interact) || Input.IsActionJustPressed("ui_accept"))
        {
            var s = _all[_pick];
            if (_player.Level < s.Level)
            {
                Sfx.Play("error", -8f, 1f, 0f, "UI");
                Hud.Announce(s.Name, $"Lv {s.Level} 이 되어야 갈 수 있다", Locked);
                return;
            }
            CloseMenu();
            if (_pick == _near)
                return; // 제자리
            _player.RestAt(s.Id, s.Front);
            _player.TravelTo(s.Front);
            CombatFx.SparkleBurst(s.Front + Vector3.Up, 30, 2f, CombatFx.PaletteOf("moon"));
            CombatFx.Flash(Colors.White, 0.5f);
            Sfx.Play("confirmation", -6f, 1f, 0f, "UI");
            Hud.Announce(s.Name, "전송석", Sky);
        }
        else if (Input.IsActionJustPressed(Controls.Dodge) || Input.IsActionJustPressed("ui_cancel"))
        {
            CloseMenu();
            Sfx.Play("click", -8f, 1f, 0f, "UI");
        }
    }

    private void Step(int d)
    {
        _pick = (int)Mathf.PosMod(_pick + d, _all.Count);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    public override void _Draw()
    {
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;
        if (!_menu && _near >= 0 && !GameRoot.Instance.InputLocked)
        {
            // 주인공 머리 위에 'F 전송석'
            Vector2 p = (view.WorldToScreen(_player.WorldPosition + Vector3.Up * 2.4f) / px).Round();
            float bob = Mathf.PosMod(_age, 1f) < 0.5f ? 0f : 1f;
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One * px);
            string label = "F  전송석";
            float w = PixelText.Width(label) + 10f;
            var r = new Rect2(Mathf.Round(p.X - w * 0.5f), p.Y - 14 - bob, Mathf.Round(w), 17);
            DrawRect(r, Back);
            DrawRect(r, Sky, false, 1f);
            PixelText.DrawCentered(this, label, new Vector2(p.X, p.Y - 1 - bob), Sky);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        if (!_menu)
            return;
        float drop = Mathf.Round(8f * (1f - Mathf.Min(_age / 0.18f, 1f)));
        DrawSetTransform(new Vector2(0f, -drop * px), 0f, Vector2.One * px);
        float lowW = GetViewportRect().Size.X / px;
        int rows = _all.Count;
        var panel = new Rect2(Mathf.Round(lowW - 206), 30, 190, 36 + rows * RowH + 16);
        DrawRect(panel, Back);
        DrawRect(panel, Sky, false, 1f);
        DrawRect(new Rect2(panel.Position + new Vector2(2, 2), panel.Size - new Vector2(4, 4)), new Color(Sky, 0.35f), false, 1f);
        PixelText.DrawCentered(this, "전송석 — 어디로 갈까", new Vector2(panel.GetCenter().X, panel.Position.Y + 16), Sky);
        for (int k = 0; k < rows; k++)
        {
            var s = _all[k];
            float y = panel.Position.Y + 36 + k * RowH;
            bool on = k == _pick, open = _player.Level >= s.Level;
            if (on)
                DrawRect(new Rect2(panel.Position.X + 4, y - 11, panel.Size.X - 8, RowH), new Color(Sky, 0.18f));
            string mark = k == _near ? " (여기)" : "";
            var col = !open ? Locked : on ? Colors.White : Dim;
            PixelText.Draw(this, (on ? "▶ " : "   ") + s.Name + mark, new Vector2(panel.Position.X + 10, y), col);
            string lv = "Lv " + s.Level;
            PixelText.Draw(this, lv, new Vector2(panel.End.X - 10 - PixelText.Width(lv), y), open ? new Color(Sky, 0.8f) : new Color(1f, 0.45f, 0.4f));
        }
        PixelText.Draw(this, "↑↓ 고르기  F 이동  Space 닫기", new Vector2(panel.Position.X + 10, panel.End.Y - 5), new Color(Dim, 0.9f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
