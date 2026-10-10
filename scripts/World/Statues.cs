using System.Collections.Generic;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Core;
using PixelMmo.Render;

namespace PixelMmo.World;

/// <summary>
/// 여신상: 지역마다 하나 (지도 meta.statues). 앞에 서서 F(패드 A)로 기도하면
///  - 처음이면 깨어난다 (빠른 이동 목록에 오른다)
///  - 부활 지점이 되고 체력이 찬다 (쓰러지면·다시 켜면 여기서)
///  - 깨운 여신상이 둘 이상이면 이동 목록이 열린다: ↑↓ 고르기, 공격·F 이동, 회피·Esc 닫기
/// BotW 의 사당·할로우 나이트의 의자 — 넓은 지도를 걸어서 되돌아가는 수고를 덜고, 지역마다 '돌아올 자리'를 만든다.
/// </summary>
public partial class Statues : Control
{
    private readonly record struct Statue(string Id, string Name, Vector3 Front, Vector3 Top);

    private const float Reach = 3.4f;          // 기도할 수 있는 거리 (m, 상 앞자리에서)
    private const float FrontOffset = 2.6f;    // 상 앞자리: 받침 남쪽 (m)
    private static readonly Color Gold = new(1f, 0.88f, 0.55f);
    private static readonly Color Back = new(0.06f, 0.05f, 0.09f, 0.86f);
    private static readonly Color Dim = new(0.72f, 0.68f, 0.62f);

    private readonly List<Statue> _all = new();
    private IPlayerContext _player;
    private int _near = -1;
    private bool _menu;
    private int _pick;
    private float _age;
    private readonly List<int> _list = new();   // 이동 목록 (깨운 것만, 지도 순서)

    public Statues(WorldData world)
    {
        Name = "Statues";
        if (!world.Meta.TryGetProperty("statues", out var arr))
            return;
        foreach (var s in arr.EnumerateArray())
        {
            float x = s.GetProperty("x").GetSingle(), z = s.GetProperty("z").GetSingle();
            float fz = z + FrontOffset;
            _all.Add(new Statue(s.GetProperty("id").GetString(), s.GetProperty("name").GetString(),
                new Vector3(x, world.HeightAt(x, fz), fz), new Vector3(x, world.HeightAt(x, z) + 6.2f, z + 1f)));
        }
    }

    public Statues() { }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Bind(IPlayerContext player) => _player = player;

    /// <summary>저장된 부활 여신상의 앞자리 (없으면 null) — 켤 때 여기서 시작한다</summary>
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
        var root = GameRoot.Instance;
        if (_near >= 0 && _player.IsAlive && !root.InputLocked && Input.IsActionJustPressed(Controls.Interact))
            Pray(_all[_near]);
        QueueRedraw();
    }

    private void Pray(Statue s)
    {
        bool first = _player.Pray(s.Id, s.Front);
        CombatFx.SparkleBurst(s.Top - Vector3.Up * 2.5f, first ? 60 : 24, first ? 3f : 1.8f, CombatFx.PaletteOf("moon"));
        CombatFx.Flash(new Color(1f, 0.95f, 0.8f), first ? 0.35f : 0.15f);
        Sfx.Play("impactBell_heavy", first ? -6f : -12f, 1.5f, 0f, "UI");
        Hud.Announce(first ? "여신상이 깨어났다" : "여신상에 기도했다", s.Name + " — 부활 지점", Gold);
        if (_player.Statues.Count > 1)
            OpenMenu(s);
    }

    private void OpenMenu(Statue here)
    {
        _list.Clear();
        for (int i = 0; i < _all.Count; i++)
            if (_player.Statues.Contains(_all[i].Id))
                _list.Add(i);
        _pick = Mathf.Max(0, _list.FindIndex(i => _all[i].Id == here.Id));
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
            var s = _all[_list[_pick]];
            CloseMenu();
            if (_near >= 0 && _all[_near].Id == s.Id)
                return; // 제자리
            _player.Pray(s.Id, s.Front);
            _player.TravelTo(s.Front);
            CombatFx.SparkleBurst(s.Front + Vector3.Up, 30, 2f, CombatFx.PaletteOf("moon"));
            CombatFx.Flash(Colors.White, 0.5f);
            Sfx.Play("confirmation", -6f, 1f, 0f, "UI");
            Hud.Announce(s.Name, "여신상", Gold);
        }
        else if (Input.IsActionJustPressed(Controls.Dodge) || Input.IsActionJustPressed("ui_cancel"))
        {
            CloseMenu();
            Sfx.Play("click", -8f, 1f, 0f, "UI");
        }
    }

    private void Step(int d)
    {
        _pick = (int)Mathf.PosMod(_pick + d, _list.Count);
        Sfx.Play("select", -10f, 1.1f, 0.05f, "UI");
    }

    public override void _Draw()
    {
        var view = GameRoot.Instance.View;
        float px = view.PixelScale;
        if (!_menu && _near >= 0 && !GameRoot.Instance.InputLocked)
        {
            // 주인공 머리 위에 'F 기도' (깨운 상은 금빛). 상 머리 위는 가까이 서면 화면 밖으로 잘렸다
            Vector2 p = (view.WorldToScreen(_player.WorldPosition + Vector3.Up * 2.4f) / px).Round();
            bool awake = _player.Statues.Contains(_all[_near].Id);
            float bob = Mathf.PosMod(_age, 1f) < 0.5f ? 0f : 1f;
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One * px);
            string label = "F  기도";
            float w = PixelText.Width(label) + 10f;
            var r = new Rect2(Mathf.Round(p.X - w * 0.5f), p.Y - 14 - bob, Mathf.Round(w), 17);
            DrawRect(r, Back);
            DrawRect(r, awake ? Gold : Dim, false, 1f);
            PixelText.DrawCentered(this, label, new Vector2(p.X, p.Y - 1 - bob), awake ? Gold : Colors.White);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        if (!_menu)
            return;
        float drop = Mathf.Round(8f * (1f - Mathf.Min(_age / 0.18f, 1f)));
        DrawSetTransform(new Vector2(0f, -drop * px), 0f, Vector2.One * px);
        float lowW = GetViewportRect().Size.X / px;
        int rows = _list.Count;
        var panel = new Rect2(Mathf.Round(lowW - 196), 70, 178, 44 + rows * 18 + 16);
        DrawRect(panel, Back);
        DrawRect(panel, Gold, false, 1f);
        DrawRect(new Rect2(panel.Position + new Vector2(2, 2), panel.Size - new Vector2(4, 4)), new Color(Gold, 0.35f), false, 1f);
        PixelText.DrawCentered(this, "여신상 — 어디로 갈까", new Vector2(panel.GetCenter().X, panel.Position.Y + 18), Gold);
        for (int k = 0; k < rows; k++)
        {
            var s = _all[_list[k]];
            float y = panel.Position.Y + 42 + k * 18;
            bool on = k == _pick;
            if (on)
                DrawRect(new Rect2(panel.Position.X + 4, y - 12, panel.Size.X - 8, 16), new Color(Gold, 0.18f));
            string mark = _near >= 0 && _all[_near].Id == s.Id ? "  (여기)" : "";
            PixelText.Draw(this, (on ? "▶ " : "   ") + s.Name + mark, new Vector2(panel.Position.X + 10, y), on ? Colors.White : Dim);
        }
        PixelText.Draw(this, "↑↓ 고르기  F 이동  Space 닫기", new Vector2(panel.Position.X + 10, panel.End.Y - 6), new Color(Dim, 0.9f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
