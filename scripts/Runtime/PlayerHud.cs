using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelMmo.Combat;
using PixelMmo.Data;

namespace PixelMmo.Runtime;

public partial class PlayerHud : Control
{
    public static PlayerHud Instance { get; private set; }
    public bool MenuOpen => _window?.Visible == true;
    public string CurrentPage { get; private set; } = "";
    private IPlayerContext _player;
    private TileWorld _tiles;
    private PanelContainer _vitals, _hotbar, _window, _mapFrame, _menus;
    private VBoxContainer _page;
    private Label _identity, _zone, _title, _hpText, _mpText, _staminaText;
    private ProgressBar _hp, _mp, _stamina;
    private TextureRect _map;
    private ColorRect _mapDot;
    private Control _shade;
    private RichTextLabel _detail, _stats;
    private readonly List<(PanelContainer Box, Label Name, Label State, ProgressBar Cooldown, string Action)> _slots = new();
    private string _selectedSkill;
    private string _shownSkills;
    private float _refresh;
    private static readonly string[] Actions = { InputSetup.Attack, InputSetup.Bash, InputSetup.Warcry, InputSetup.Skill4, InputSetup.Skill5, InputSetup.Guard };
    private static readonly string[] Keys = { "J", "L", "U", "I", "O", "K" };
    public void Bind(IPlayerContext player, TileWorld tiles) { _player=player; _tiles=tiles; }
    public override void _EnterTree() => Instance=this;
    public override void _ExitTree() { if(Instance==this) Instance=null; if(_player!=null) _player.InputBlocked=false; }

    private PanelContainer Panel(Control parent, bool light=false)
    {
        var panel=new PanelContainer(); panel.AddThemeStyleboxOverride("panel",RpgUi.Frame(light)); parent.AddChild(panel); return panel;
    }
    private static Label Resource(VBoxContainer column, string name, Color color, out ProgressBar bar)
    {
        bar=RpgUi.Bar(color); column.AddChild(bar);
        var label=RpgUi.Text(name,13); label.HorizontalAlignment=HorizontalAlignment.Center;
        bar.AddChild(label); label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return label;
    }
    public override void _Ready()
    {
        Name="Hud"; MouseFilter=MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _vitals=Panel(this); var resources=new VBoxContainer(); _vitals.AddChild(resources);
        _identity=RpgUi.Text("모험가",18,RpgUi.Gold); resources.AddChild(_identity);
        _hpText=Resource(resources,"생명",new Color("a74238"),out _hp);
        _mpText=Resource(resources,"마나",new Color("456f94"),out _mp);
        _staminaText=Resource(resources,"스태미나",new Color("839353"),out _stamina);
        _stamina.TooltipText="회피에 사용하는 스태미나. 잠시 쉬면 회복됩니다.";
        _mapFrame=Panel(this); var mapColumn=new VBoxContainer(); _mapFrame.AddChild(mapColumn);
        _zone=RpgUi.Text("새잎의 숲",14,RpgUi.Gold); _zone.HorizontalAlignment=HorizontalAlignment.Center; mapColumn.AddChild(_zone);
        _map=new TextureRect { ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize, StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize=new Vector2(176,136), MouseFilter=MouseFilterEnum.Ignore, TextureFilter=TextureFilterEnum.Nearest };
        mapColumn.AddChild(_map);
        _mapDot=new ColorRect { Color=RpgUi.Paper, Size=new Vector2(5,5), MouseFilter=MouseFilterEnum.Ignore }; _map.AddChild(_mapDot);
        var compass=RpgUi.Text("◆  N  ·  탐험 지도  ◆",11,RpgUi.Muted); compass.HorizontalAlignment=HorizontalAlignment.Center; mapColumn.AddChild(compass);
        _hotbar=Panel(this); var row=new HBoxContainer(); row.AddThemeConstantOverride("separation",6); _hotbar.AddChild(row);
        for(int i=0;i<Actions.Length;i++)
        {
            var box=Panel(row,true); box.AddThemeStyleboxOverride("panel",RpgUi.Frame(true,7)); box.CustomMinimumSize=new Vector2(72,65);
            var column=new VBoxContainer(); box.AddChild(column);
            var key=RpgUi.Text(Keys[i],12,RpgUi.Gold); column.AddChild(key);
            var name=RpgUi.Text("—",12); name.HorizontalAlignment=HorizontalAlignment.Center; column.AddChild(name);
            var cd=RpgUi.Bar(new Color("c5a668"),3); column.AddChild(cd);
            var state=RpgUi.Text("",11,RpgUi.Muted); state.HorizontalAlignment=HorizontalAlignment.Center; column.AddChild(state);
            _slots.Add((box,name,state,cd,Actions[i]));
        }
        _menus=Panel(this); _menus.AddThemeStyleboxOverride("panel",RpgUi.Frame(false,6)); var menuRow=new HBoxContainer(); _menus.AddChild(menuRow);
        var character=RpgUi.Button("P  캐릭터"); character.Pressed+=()=>TogglePage("status"); menuRow.AddChild(character);
        var skills=RpgUi.Button("B  스킬"); skills.Pressed+=()=>TogglePage("skills"); menuRow.AddChild(skills);
        _shade=new ColorRect { Color=new Color(0,0,0,.38f),Visible=false,MouseFilter=MouseFilterEnum.Stop };
        AddChild(_shade); _shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _window=Panel(this); _window.Visible=false;
        var book=new VBoxContainer(); book.AddThemeConstantOverride("separation",12); _window.AddChild(book);
        var heading=new HBoxContainer(); book.AddChild(heading);
        _title=RpgUi.Text("모험가의 기록",24,RpgUi.Gold); _title.SizeFlagsHorizontal=SizeFlags.ExpandFill; heading.AddChild(_title);
        var close=RpgUi.Button("닫기  Esc"); close.Pressed+=CloseMenu; heading.AddChild(close);
        var tabs=new HBoxContainer(); book.AddChild(tabs);
        foreach(var tab in new[]{("status","인물 기록"),("skills","기술과 숙련")})
        { var button=RpgUi.Button(tab.Item2); button.SizeFlagsHorizontal=SizeFlags.ExpandFill; button.Pressed+=()=>ShowPage(tab.Item1); tabs.AddChild(button); }
        _page=new VBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; book.AddChild(_page);
        var footer=RpgUi.Text("P 캐릭터  ·  B 스킬  ·  Esc 닫기",12,RpgUi.Muted); book.AddChild(footer);
        GetViewport().SizeChanged+=Arrange; Arrange();
    }
    private void Arrange()
    {
        Vector2 screen=GetViewportRect().Size;
        _vitals.Position=new(18,18); _vitals.Size=new(Mathf.Min(292,screen.X*.43f),142);
        _mapFrame.Position=new(screen.X-228,18); _mapFrame.Size=new(210,196);
        _hotbar.Size=new(510,98); _hotbar.Position=new((screen.X-_hotbar.Size.X)*.5f,screen.Y-112);
        _menus.Size=new(215,52); _menus.Position=new(18,170);
        Vector2 size=new(Mathf.Min(820,screen.X-40),Mathf.Min(570,screen.Y-40));
        _window.Position=(screen-size)*.5f; _window.Size=size;
    }
    public void TogglePage(string page) { if(MenuOpen && CurrentPage==page) CloseMenu(); else ShowPage(page); }
    public void CloseMenu() { _window.Visible=false; _shade.Visible=false; _player.InputBlocked=false; GetViewport().GuiReleaseFocus(); }
    public void ShowPage(string page)
    {
        if(_player==null) return;
        CurrentPage=page; _window.Visible=true; _shade.Visible=true; _player.InputBlocked=true;
        foreach(var child in _page.GetChildren()) { _page.RemoveChild(child); child.QueueFree(); }
        _stats=null; _detail=null;
        _title.Text=page=="status" ? "◆  모험가의 기록" : "◆  기술과 숙련";
        if(page=="status")
        {
            var columns=new HBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; columns.AddThemeConstantOverride("separation",18); _page.AddChild(columns);
            var scroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill }; columns.AddChild(scroll);
            _stats=new RichTextLabel { BbcodeEnabled=true,FitContent=true,SizeFlagsHorizontal=SizeFlags.ExpandFill,ScrollActive=false,MouseFilter=MouseFilterEnum.Ignore };
            _stats.AddThemeFontSizeOverride("normal_font_size",18); _stats.AddThemeColorOverride("default_color",RpgUi.Paper); scroll.AddChild(_stats);
            var portrait=Panel(columns,true); portrait.CustomMinimumSize=new Vector2(224,0);
            var caption=new VBoxContainer(); caption.AddThemeConstantOverride("separation",14); portrait.AddChild(caption);
            var crest=RpgUi.Text("◆  여행자의 초상  ◆",16,RpgUi.Gold); crest.HorizontalAlignment=HorizontalAlignment.Center; caption.AddChild(crest);
            using var portraitImage=CharacterArt.Render(_player.Appearance);
            caption.AddChild(new TextureRect { Texture=_player.Appearance.Confirmed || _player.Appearance.UsePixelLab?ImageTexture.CreateFromImage(portraitImage):GD.Load<Texture2D>("res://art/player/sword_idle/south.png"),
                CustomMinimumSize=new Vector2(180,210),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=TextureFilterEnum.Nearest,MouseFilter=MouseFilterEnum.Ignore });
            caption.AddChild(RpgUi.Text($"보유 기술   {_player.Jobs.LearnedSkills.Count}\n거쳐온 직업   {_player.Jobs.History.Count}",14,RpgUi.Muted));
            var customize=RpgUi.Button("외형 변경  C");customize.Pressed+=()=>CharacterCreator.Open(GetTree().CurrentScene,_player);caption.AddChild(customize);
        }
        else
        {
            var columns=new HBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; columns.AddThemeConstantOverride("separation",20); _page.AddChild(columns);
            var scroll=new ScrollContainer { CustomMinimumSize=new Vector2(240,0),SizeFlagsVertical=SizeFlags.ExpandFill }; columns.AddChild(scroll);
            var list=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; scroll.AddChild(list);
            var owned=_player.Jobs.LearnedSkills.Select(id=>GameDatabase.Instance.GetSkill(id)).Where(s=>s!=null).ToList();
            _shownSkills=string.Join("|",_player.Jobs.LearnedSkills);
            if(!owned.Exists(s=>s.Id==_selectedSkill)) _selectedSkill=owned.FirstOrDefault()?.Id;
            var selection=new ButtonGroup();
            foreach(var skill in owned)
            {
                var button=RpgUi.Button(skill.Name); button.TooltipText="기술의 효과와 숙련도를 확인합니다.";
                button.ToggleMode=true; button.ButtonGroup=selection; button.ButtonPressed=skill.Id==_selectedSkill;
                button.Pressed+=()=>{_selectedSkill=skill.Id;RefreshDetails();}; list.AddChild(button);
            }
            _detail=new RichTextLabel { BbcodeEnabled=true,SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill };
            _detail.AddThemeFontSizeOverride("normal_font_size",17); _detail.AddThemeColorOverride("default_color",RpgUi.Paper); columns.AddChild(_detail);
        }
        RefreshDetails(); _window.GetChild(0).GetChild(0).GetChild<Button>(1).GrabFocus();
    }
    private void RefreshDetails()
    {
        var job=GameDatabase.Instance.GetJob(_player.Jobs.CurrentJobId);
        if(_stats!=null)
        {
            string rows="";
            foreach(var stat in new[]{("str","힘"),("dex","민첩"),("vit","체력"),("int","지능"),("wis","정신"),("luk","행운")})
                rows+=$"{stat.Item2}     [color=#e4bf7c]{_player.GetStat(stat.Item1)}[/color]     [font_size=14](기본 {_player.GetBaseStat(stat.Item1)})[/font_size]\n";
            _stats.Text=$"[font_size=26][color=#e4bf7c]{job?.Name ?? "모험가"}[/color][/font_size]   Lv. {_player.Level}\n직업 숙련 단계  {_player.Jobs.CurrentRank}\n\n생명     {_player.Hp:0} / {_player.MaxHp:0}\n마나     {_player.Mp:0} / {_player.MaxMp:0}\n스태미나     {_player.Stamina:0} / {_player.MaxStamina:0}\n\n[color=#c4a46a]────────  능력치  ────────[/color]\n{rows}\n[font_size=14]스태미나는 회피에 사용되며 잠시 쉬면 회복됩니다.\n능력치는 직업 성장과 계승 보정을 반영합니다.[/font_size]";
        }
        if(_detail==null) return;
        var skill=_selectedSkill==null ? null : GameDatabase.Instance.GetSkill(_selectedSkill);
        if(skill==null) { _detail.Text="아직 배운 기술이 없습니다."; return; }
        int slot=Array.FindIndex(Actions,a=>_player.ResolveSkillForAction(a)?.Id==skill.Id);
        string description=skill.Kind=="passive" ? "습득한 동안 지속되는 효과입니다." : skill.Heal!=null ? "생명을 회복하는 기술입니다."
            : skill.Guard!=null ? "공격을 막아 피해를 줄입니다." : skill.Projectile!=null ? "전방으로 투사체를 발사합니다."
            : skill.Stun!=null ? "주변 적의 행동을 저지합니다." : skill.Dash!=null ? "앞으로 파고들며 타격합니다."
            : skill.Buff!=null ? "일정 시간 자신의 전투 능력을 강화합니다." : "바라보는 방향의 적을 공격합니다.";
        float mastery=_player.Mastery.Get(skill.Id);
        _detail.Text=$"[font_size=25][color=#e4bf7c]{skill.Name}[/color][/font_size]\n{(skill.Kind=="passive" ? "지속 효과" : slot>=0 ? "단축키  "+Keys[slot] : "보유 기술")}\n\n{description}\n\n[color=#c4a46a]기술 정보[/color]\n마나 소모   {skill.ManaCost:0}\n재사용 대기   {skill.Cooldown:0.#}초\n남은 대기   {_player.CooldownRemaining(skill.Id):0.#}초\n\n[color=#c4a46a]숙련[/color]   {mastery:0.0}\n숙련 피해 보정   +{( _player.Mastery.DamageMultiplier(skill)-1f)*100f:0.#}%\n\n[font_size=14]유효한 사용으로 숙련이 쌓입니다.[/font_size]";
    }
    public override void _Input(InputEvent input)
    {
        if(CharacterCreator.IsOpen) return;
        if(input.IsEcho()) return;
        if(input.IsActionPressed(InputSetup.CharacterSheet)) { TogglePage("status"); GetViewport().SetInputAsHandled(); }
        else if(input.IsActionPressed(InputSetup.SkillBook)) { TogglePage("skills"); GetViewport().SetInputAsHandled(); }
        else if(MenuOpen && input is InputEventKey key && key.Pressed && key.PhysicalKeycode==Key.Escape)
        { CloseMenu(); GetViewport().SetInputAsHandled(); }
    }
    public override void _Process(double delta)
    {
        if(_player==null) return;
        _identity.Text=$"◆  {GameDatabase.Instance.GetJob(_player.Jobs.CurrentJobId)?.Name ?? "모험가"}    Lv. {_player.Level}";
        _hp.Value=_player.HpRatio*100; _mp.Value=_player.MpRatio*100; _stamina.Value=_player.MaxStamina>0 ? _player.Stamina/_player.MaxStamina*100 : 0;
        _hpText.Text=$"생명   {_player.Hp:0} / {_player.MaxHp:0}"; _mpText.Text=$"마나   {_player.Mp:0} / {_player.MaxMp:0}";
        _staminaText.Text=$"스태미나   {_player.Stamina:0} / {_player.MaxStamina:0}";
        foreach(var slot in _slots)
        {
            var skill=_player.ResolveSkillForAction(slot.Action);
            slot.Name.Text=skill?.Name ?? "—";
            float cd=skill==null ? 0 : slot.Action==InputSetup.Guard ? _player.GuardCooldownRemaining : _player.CooldownRemaining(skill.Id);
            bool ready=skill!=null && cd<=0 && _player.HasManaFor(skill);
            slot.State.Text=skill==null ? "미습득" : cd>0 ? $"{cd:0.0}초" : ready ? "준비" : "마나 부족";
            slot.Cooldown.Value=skill?.Cooldown>0 ? 100*(1-Mathf.Clamp(cd/skill.Cooldown,0,1)) : 100;
            slot.Box.TooltipText=skill==null ? "미습득 기술" : $"{skill.Name} · 마나 {skill.ManaCost:0} · 숙련 {_player.Mastery.Get(skill.Id):0.0}";
        }
        bool indoor=WorldLayout.IronjawDen.Contains(_player.WorldPosition);
        _map.Texture=indoor ? _tiles?.SanctuaryMinimap : _tiles?.Minimap;
        _zone.Text=WorldLayout.ZoneAt(_player.WorldPosition)?.DisplayName ?? "여행 중";
        _refresh-=(float)delta;
        if(MenuOpen && _refresh<=0)
        {
            _refresh=.15f;
            if(CurrentPage=="skills" && _shownSkills!=string.Join("|",_player.Jobs.LearnedSkills)) ShowPage("skills");
            else RefreshDetails();
        }
        UpdateMapMarker();
    }
    private void UpdateMapMarker()
    {
        if(_player==null || _map?.Texture==null) return;
        bool indoor=WorldLayout.IronjawDen.Contains(_player.WorldPosition);
        Vector2 source=_map.Texture.GetSize(); float scale=Mathf.Min(_map.Size.X/source.X,_map.Size.Y/source.Y);
        Vector2 at=(_map.Size-source*scale)*.5f;
        Vector2 tile=_player.WorldPosition/WorldLayout.TileSize;
        if(indoor)tile-=WorldLayout.IronjawDen.Tiles.Grow(2).Position;
        _mapDot.Visible=new Rect2(Vector2.Zero,source).HasPoint(tile);
        _mapDot.Position=at+tile*scale-_mapDot.Size*.5f;
    }
}
