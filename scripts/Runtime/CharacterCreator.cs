using System;
using Godot;
using PixelMmo.Combat;

namespace PixelMmo.Runtime;

public partial class CharacterCreator : CanvasLayer
{
    public static CharacterCreator Instance { get; private set; }
    public static bool IsOpen => Instance != null;
    public CharacterAppearance Draft { get; private set; }
    private IPlayerContext _player;
    private bool _wasPaused;
    private Control _root;
    private GridContainer _grid;
    private Label _heading, _pageLabel, _choice, _message;
    private TextureRect _preview;
    private Button _male, _female, _previous, _next;
    private int _category, _page, _direction, _frame;
    private string _pose="idle";
    private bool _face;
    private double _elapsed;
    private ImageTexture _previewTexture;
    private OptionButton _artMode;
    private readonly RandomNumberGenerator _rng=new();
    private static readonly string[] Categories={"헤어스타일","눈 모양","체형","색상"};
    public static void Open(Node parent,IPlayerContext player)
    {
        if(IsOpen) return;
        PlayerHud.Instance?.CloseMenu();
        parent.AddChild(new CharacterCreator { _player=player });
    }
    public override void _EnterTree() { Instance=this; Layer=100; ProcessMode=ProcessModeEnum.Always; }
    public override void _Ready()
    {
        Draft=_player.Appearance.Copy();_category=Draft.UsePixelLab?2:0; _wasPaused=GetTree().Paused; GetTree().Paused=true; _player.InputBlocked=true;
        _root=new AtelierBackdrop(); AddChild(_root); _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin=new MarginContainer(); _root.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach(string side in new[]{"left","right","top","bottom"}) margin.AddThemeConstantOverride("margin_"+side,28);
        var layout=new VBoxContainer(); layout.AddThemeConstantOverride("separation",18); margin.AddChild(layout);
        var top=new HBoxContainer(); layout.AddChild(top);
        var title=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; top.AddChild(title);
        title.AddChild(RpgUi.Text("S A E I P   ·   A T E L I E R",12,RpgUi.Gold));
        title.AddChild(RpgUi.Text("숲에 새길 나의 모습",30));
        top.AddChild(RpgUi.Text("CHARACTER CREATION\n새잎의 숲  /  모험가의 기록",13,RpgUi.Muted));
        var body=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation",20); layout.AddChild(body);
        var nav=new VBoxContainer { CustomMinimumSize=new Vector2(155,0) }; nav.AddThemeConstantOverride("separation",12); body.AddChild(nav);
        nav.AddChild(RpgUi.Text("01  ·  인물",13,RpgUi.Gold));
        _male=ChoiceButton("남성",()=>SetFemale(false)); nav.AddChild(_male);
        _female=ChoiceButton("여성",()=>SetFemale(true)); nav.AddChild(_female);
        nav.AddChild(new Control { CustomMinimumSize=new Vector2(0,18) }); nav.AddChild(RpgUi.Text("02  ·  세부 외형",13,RpgUi.Gold));
        var tabs=new ButtonGroup();
        for(int i=0;i<Categories.Length;i++) { int category=i; var button=ChoiceButton(Categories[i],()=>ShowCategory(category)); button.ButtonGroup=tabs;button.ButtonPressed=i==_category;nav.AddChild(button); }
        var fill=new Control { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; nav.AddChild(fill);
        var random=RpgUi.Button("새로운 조합 ↻"); random.Pressed+=()=>{ Draft.Randomize(_rng); Refresh(); }; nav.AddChild(random);
        var stage=Panel(body,false,12); stage.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; stage.SizeFlagsStretchRatio=.8f;
        var stageColumn=new VBoxContainer(); stage.AddChild(stageColumn);
        var stageTitle=RpgUi.Text("◆  모험가의 초상  ◆",15,RpgUi.Gold); stageTitle.HorizontalAlignment=HorizontalAlignment.Center; stageColumn.AddChild(stageTitle);
        _artMode=new OptionButton();_artMode.AddItem("PixelLab 검객");_artMode.AddItem("기존 파츠 조합");
        _artMode.AccessibilityName="캐릭터 아트 방식";_artMode.ItemSelected+=index=>SetArtMode(index==0);stageColumn.AddChild(_artMode);
        _preview=new TextureRect { ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize, StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical=Control.SizeFlags.ExpandFill, TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore };
        stageColumn.AddChild(_preview);
        _choice=RpgUi.Text("",13); _choice.HorizontalAlignment=HorizontalAlignment.Center; stageColumn.AddChild(_choice);
        var angles=new HBoxContainer { Alignment=BoxContainer.AlignmentMode.Center }; stageColumn.AddChild(angles);
        AddButton(angles,"↶  회전",()=>Rotate(-1)); AddButton(angles,"얼굴 / 전신",()=>{_face=!_face;UpdatePreview();}); AddButton(angles,"회전  ↷",()=>Rotate(1));
        var poses=new HBoxContainer { Alignment=BoxContainer.AlignmentMode.Center }; stageColumn.AddChild(poses);
        foreach(var pose in new[]{("idle","서기"),("run","달리기"),("sword","베기"),("sword_heavy","강타")}) AddButton(poses,pose.Item2,()=>{_pose=pose.Item1;_frame=0;UpdatePreview();});
        var right=Panel(body,true,18); right.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; right.SizeFlagsStretchRatio=1.18f;
        var options=new VBoxContainer(); options.AddThemeConstantOverride("separation",10); right.AddChild(options);
        _heading=RpgUi.Text("",23,new Color("30281f")); options.AddChild(_heading);
        options.AddChild(RpgUi.Text("선택한 외형이 모험 속 모습에 그대로 반영됩니다.",12,new Color("72624e")));
        var scroll=new ScrollContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled }; options.AddChild(scroll);
        _grid=new GridContainer { Columns=5,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; _grid.AddThemeConstantOverride("h_separation",8);_grid.AddThemeConstantOverride("v_separation",8);scroll.AddChild(_grid);
        var paging=new HBoxContainer { Alignment=BoxContainer.AlignmentMode.Center }; options.AddChild(paging);
        _previous=AddButton(paging,"←",()=>{_page--;RebuildChoices();}); _pageLabel=RpgUi.Text("",13,new Color("62513c")); paging.AddChild(_pageLabel);
        _next=AddButton(paging,"→",()=>{_page++;RebuildChoices();});
        var footer=new HBoxContainer(); footer.AddThemeConstantOverride("separation",12); layout.AddChild(footer);
        _message=RpgUi.Text("외형은 능력치에 영향을 주지 않습니다.  ·  게임 중 C키로 다시 꾸밀 수 있습니다.",13,RpgUi.Muted);
        _message.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;footer.AddChild(_message);
        AddButton(footer,"취소  Esc",Cancel);
        var confirm=AddButton(footer,Draft.Confirmed?"외형 저장":"이 모습으로 모험 시작",()=>Apply()); confirm.CustomMinimumSize=new Vector2(230,50);
        var style=RpgUi.Frame(true,12);style.BgColor=new Color("526a56");style.BorderColor=new Color("cfb47a");confirm.AddThemeStyleboxOverride("normal",style);
        if(DevCapture.IsRequested() && Array.IndexOf(OS.GetCmdlineUserArgs(),"--creator-female")>=0) Draft.Female=true;
        Refresh();
        if(DevCapture.IsRequested() && Array.IndexOf(OS.GetCmdlineUserArgs(),"--creator-eyes")>=0) ShowCategory(1);
        confirm.GrabFocus();
    }
    private static PanelContainer Panel(Control parent,bool paper,int margin)
    {
        var panel=new PanelContainer(); var style=RpgUi.Frame(false,margin);
        style.BgColor=paper?new Color("e7dbc1"):new Color("242d29");style.BorderColor=new Color("8f7b57");style.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel",style);parent.AddChild(panel);return panel;
    }
    private static Button AddButton(Control parent,string text,Action action) { var button=RpgUi.Button(text);button.Pressed+=action;parent.AddChild(button);return button; }
    private static Button ChoiceButton(string text,Action action) { var b=RpgUi.Button(text);b.ToggleMode=true;b.Pressed+=action;return b; }
    public void SetFemale(bool female) { Draft.Female=female; Refresh(); }
    public void SetArtMode(bool pixelLab) { Draft.UsePixelLab=pixelLab;_page=0;_frame=0;Refresh(); }
    public void ShowCategory(int category) { _category=Mathf.Clamp(category,0,3);_page=0;if(category==1){_face=true;_direction=0;} Refresh(); }
    public void Rotate(int by) { _direction=Mathf.PosMod(_direction+by,8);UpdatePreview(); }
    private void Refresh() { _artMode.Selected=Draft.UsePixelLab?0:1;_male.ButtonPressed=!Draft.Female;_female.ButtonPressed=Draft.Female;RebuildChoices();UpdatePreview(); }
    private void RebuildChoices()
    {
        foreach(var child in _grid.GetChildren()) { _grid.RemoveChild(child);child.QueueFree(); }
        if(Draft.UsePixelLab && _category<2) {
            _heading.Text=Categories[_category]+"   /   기본 원화";
            _grid.Columns=1;
            var note=RpgUi.Text("정교한 검객 원화가 적용됩니다.\n\n체형과 머리색·피부색을 바꿀 수 있습니다.\n헤어·눈 파츠는 ‘기존 파츠 조합’에서 편집합니다.",15,new Color("62513c"));
            _grid.AddChild(note);_pageLabel.Text="";_previous.Disabled=true;_next.Disabled=true;return;
        }
        _grid.Columns=_category==2?3:5;
        int count=_category==0?CharacterAppearance.StyleCount:_category==1?CharacterAppearance.EyeCount:_category==2?CharacterAppearance.BodyCount:42;
        _heading.Text=Categories[_category]+"   /   "+count.ToString("00");
        int pages=(count+14)/15;_page=Mathf.Clamp(_page,0,pages-1);_pageLabel.Text=$"  {_page+1} / {pages}  ";_previous.Disabled=_page==0;_next.Disabled=_page==pages-1;
        var group=new ButtonGroup();
        for(int i=_page*15;i<Mathf.Min(count,(_page+1)*15);i++)
        {
            int index=i; var look=Draft.Copy();
            string name;
            if(_category==0) {look.HairStyle=i;name=CharacterAppearance.HairName(i,Draft.Female);}
            else if(_category==1) {look.EyeShape=i;name=CharacterAppearance.EyeName(i);}
            else if(_category==2) {look.Body=i;name=CharacterAppearance.BodyNames[i];}
            else {name=i<30?$"머리색 {i+1:00}":$"피부색 {i-29:00}";}
            var button=ChoiceButton("",()=>Select(index));button.ButtonGroup=group;
            button.CustomMinimumSize=new Vector2(82,_category==2?148:104);button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            button.TooltipText=name;button.AccessibilityName=name;
            int selected=_category==0?Draft.HairStyle:_category==1?Draft.EyeShape:_category==2?Draft.Body:-1;button.ButtonPressed=i==selected;
            var normal=RpgUi.Frame(false,4);normal.BgColor=new Color("d8c9ab");normal.BorderColor=new Color("b4a181");normal.SetBorderWidthAll(1);button.AddThemeStyleboxOverride("normal",normal);
            var chosen=RpgUi.Frame(true,4);chosen.BgColor=new Color("b6bfaa");chosen.BorderColor=new Color("526a56");button.AddThemeStyleboxOverride("pressed",chosen);
            _grid.AddChild(button);
            var box=new VBoxContainer { MouseFilter=Control.MouseFilterEnum.Ignore };button.AddChild(box);box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);box.OffsetLeft=4;box.OffsetRight=-4;box.OffsetTop=4;box.OffsetBottom=-4;
            if(_category==3) {
                var color=i<30?CharacterAppearance.HairColors[i]:CharacterAppearance.SkinColors[i-30];
                var swatch=new ColorRect { Color=color,CustomMinimumSize=new Vector2(0,70),MouseFilter=Control.MouseFilterEnum.Ignore };box.AddChild(swatch);
                button.ButtonPressed=i<30?Draft.Hair==color:Draft.Skin==color;
            } else {
                using var image=CharacterArt.Render(look,0,"idle",0,false);var texture=ImageTexture.CreateFromImage(image);
                Texture2D thumb=_category==2?texture:new AtlasTexture { Atlas=texture,Region=new Rect2(35,16,58,52) };
                box.AddChild(new TextureRect { Texture=thumb,CustomMinimumSize=new Vector2(0,_category==2?112:72),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore });
            }
            var label=RpgUi.Text(_category==3?name:_category==2?name:$"{i+1:00}",12,new Color("30281f"));label.HorizontalAlignment=HorizontalAlignment.Center;box.AddChild(label);
        }
    }
    public void Select(int index)
    {
        if(Draft.UsePixelLab && _category<2) return;
        if(_category==0) Draft.HairStyle=Mathf.Clamp(index,0,59);
        else if(_category==1) Draft.EyeShape=Mathf.Clamp(index,0,59);
        else if(_category==2) Draft.Body=Mathf.Clamp(index,0,5);
        else if(index<30 && index>=0) Draft.Hair=CharacterAppearance.HairColors[index];
        else if(index<42 && index>=30) Draft.Skin=CharacterAppearance.SkinColors[index-30];
        Refresh();
    }
    private void UpdatePreview()
    {
        using var image=CharacterArt.Render(Draft,_direction,_pose,_frame);
        if(_previewTexture==null) _previewTexture=ImageTexture.CreateFromImage(image);else _previewTexture.Update(image);
        _preview.Texture=_face?new AtlasTexture { Atlas=_previewTexture,Region=Draft.UsePixelLab?new Rect2(40,18,48,58):new Rect2(33,12,62,60) }:_previewTexture;
        _choice.Text=$"{(Draft.Female?"여성":"남성")} · {CharacterAppearance.BodyNames[Draft.Body]}\n{(Draft.UsePixelLab?"새잎의 숲 · 검객":CharacterAppearance.HairName(Draft.HairStyle,Draft.Female))}";
    }
    public override void _Process(double delta)
    {
        if(_pose=="idle") return;
        _elapsed+=delta;if(_elapsed<(_pose=="run"?.065:.10)) return;_elapsed=0;_frame=(_frame+1)%(Draft.UsePixelLab?PixelHeroArt.Count(_pose):_pose.StartsWith("sword")?9:8);UpdatePreview();
    }
    public bool Apply()
    {
        var before=_player.Appearance.Copy();Draft.Confirmed=true;_player.ApplyAppearance(Draft);
        bool testing=DevCapture.IsRequested() || ReferenceWorld3D.CheckRequested;
        if(!testing && SaveSystem.Instance?.Save(_player,"appearance")!=true) { _player.ApplyAppearance(before);_message.Text="저장에 실패했습니다. 다시 시도하거나 취소해 주세요.";return false; }
        Close();return true;
    }
    public void Cancel() => Close();
    private void Close() { GetTree().Paused=_wasPaused;_player.InputBlocked=false;Instance=null;QueueFree();GetViewport().GuiReleaseFocus(); }
    public override void _ExitTree() { if(Instance==this) { Instance=null;GetTree().Paused=_wasPaused; } }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventKey key && key.Pressed && !key.Echo) {
            if(key.PhysicalKeycode==Key.Escape) Cancel();
            else if(key.PhysicalKeycode==Key.Q) Rotate(-1);else if(key.PhysicalKeycode==Key.E) Rotate(1);
        }
    }
}

public partial class AtelierBackdrop : Control
{
    public override void _Ready() { MouseFilter=MouseFilterEnum.Stop;Resized+=QueueRedraw; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("141e1b"));
        for(int i=0;i<30;i++) { float t=i/30f;DrawCircle(new Vector2(Size.X*.36f,Size.Y*.45f),560-i*15,new Color(.23f,.29f,.22f,.012f+t*.004f)); }
        Color line=new(.68f,.57f,.36f,.22f);
        DrawRect(new Rect2(12,12,Size.X-24,Size.Y-24),line,false,1);
        foreach(var pos in new[]{new Vector2(16,16),new Vector2(Size.X-16,16),new Vector2(16,Size.Y-16),Size-new Vector2(16,16)}) DrawCircle(pos,3,RpgUi.Gold);
    }
}
