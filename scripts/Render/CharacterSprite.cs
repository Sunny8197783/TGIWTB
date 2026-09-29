using System.Collections.Generic;
using Godot;
using PixelMmo.Core;

namespace PixelMmo.Render;

/// <summary>
/// 8방향 캐릭터 스프라이트. 시트는 tools/fetch_assets.py 가 만든다:
/// 가로 = 프레임, 세로 = 8방향(Dirs 순서), 칸 128x128, 발밑 가운데가 칸의 Anchor.
/// 몸(물리)은 매끄럽게 움직이고, 그림은 텍셀 격자에 스냅해 세운다.
/// 시간은 주인(Advance)이 넣는다 — 슬로우모션 때 주인공은 정상 속도, 적만 느려야 해서.
/// </summary>
public partial class CharacterSprite : Node3D
{
    public static readonly string[] Dirs = { "south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west" };
    private const int Cell = 128;
    private static readonly Vector2 Anchor = new(64, 116); // fetch_assets.py ANCHOR 와 같아야 한다
    private const float DepthPull = 0.6f;

    private readonly Dictionary<string, (Texture2D tex, int frames)> _sheets = new();
    private MeshInstance3D _quad;
    private ShaderMaterial _mat;
    private MeshInstance3D _blob;
    private MeshInstance3D _caster;

    private string _anim;
    private int _frames = 1;
    private float _fps;
    private bool _loop;
    private float _time;

    /// <summary>해 그림자를 드리우는 몸통 크기 (m). AddChild 전에 정한다.</summary>
    public float ShadowHeight { get; init; } = 1.5f;
    public float ShadowRadius { get; init; } = 0.28f;

    /// <summary>0 = 남(화면 아래), 시계 반대로 45°씩. Dirs 참고.</summary>
    public int Dir { get; set; }
    public string Anim => _anim;
    public int Frame { get; private set; }
    public int FrameCount => _frames;
    /// <summary>한 번 재생 애니메이션이 끝났는가.</summary>
    public bool Finished => !_loop && _time * _fps >= _frames;
    /// <summary>한 번 재생 애니메이션의 진행도 0..1.</summary>
    public float Progress => Mathf.Clamp(_time * _fps / _frames, 0f, 1f);

    /// <param name="folder">예: res://art/characters/hero — 그 안의 *.png 가 애니메이션 하나씩.</param>
    public CharacterSprite(string folder)
    {
        Name = "Sprite";
        TopLevel = true;
        foreach (string file in ResourceLoader.ListDirectory(folder))
        {
            if (!file.EndsWith(".png"))
                continue;
            var tex = GD.Load<Texture2D>($"{folder}/{file}");
            _sheets[file[..^4]] = (tex, Mathf.Max(1, tex.GetWidth() / Cell));
        }
    }

    public CharacterSprite() { }

    public bool Has(string anim) => _sheets.ContainsKey(anim);

    public override void _Ready()
    {
        float size = Cell / Px.PerMeter;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sprite.gdshader") };
        _mat.SetShaderParameter("depth_pull", DepthPull);
        _quad = new MeshInstance3D
        {
            Name = "Quad",
            Mesh = new QuadMesh
            {
                Size = new Vector2(size, size * Px.UprightStretch),
                // 칸의 발밑(Anchor)이 노드 원점에 오도록
                CenterOffset = new Vector3(
                    (Cell * 0.5f - Anchor.X) / Px.PerMeter,
                    (Anchor.Y - Cell * 0.5f) / Px.PerMeter * Px.UprightStretch, 0f),
            },
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_quad);

        _blob = MakeBlob();
        AddChild(_blob);

        // 해가 만드는 긴 그림자는 보이지 않는 캡슐이 대신 드리운다 (스프라이트 판은 그림자를 안 만든다).
        // 판과 같은 자리에 두면 제 그림자를 제 몸에 드리워 캐릭터가 시커멓게 된다 → 판 뒤(북쪽)로 뺀다.
        // 해·달은 늘 남쪽 하늘을 지나므로 뒤로 뺀 캡슐의 그림자는 판에 닿지 않는다.
        _caster = new MeshInstance3D
        {
            Name = "ShadowCaster",
            Mesh = new CapsuleMesh { Radius = ShadowRadius, Height = ShadowHeight },
            Position = new Vector3(0f, ShadowHeight * 0.5f, -(ShadowRadius + 0.1f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
        };
        AddChild(_caster);
        if (_sheets.ContainsKey("idle"))
            Play("idle", 5f, true);
        else if (_sheets.ContainsKey("rot"))
            Play("rot", 1f, true);
    }

    /// <summary>애니메이션을 바꾼다. 같은 것이면 이어서 재생 (restart 로 처음부터).</summary>
    public void Play(string anim, float fps, bool loop, bool restart = false)
    {
        if (!_sheets.TryGetValue(anim, out var sheet))
            sheet = _sheets.GetValueOrDefault("rot");
        if (sheet.tex == null)
            return;
        _fps = fps;
        _loop = loop;
        if (anim == _anim && !restart)
            return;
        _keyed = false;
        _holdFrame = -1;
        _anim = anim;
        _time = 0f;
        _frames = sheet.frames;
        _mat.SetShaderParameter("albedo_tex", sheet.tex);
        _mat.SetShaderParameter("frame_count", new Vector2(_frames, Dirs.Length));
        Apply();
    }

    /// <summary>한 번 재생 애니메이션을 정해진 시간에 끝나게 튼다 (공격 동작 길이 = 판정 타이밍).</summary>
    /// <param name="keepIfSame">이미 이 동작이면 처음으로 되감지 않는다 (막기 자세 유지).</param>
    public void PlayOnce(string anim, float duration, bool keepIfSame = false)
    {
        int frames = _sheets.TryGetValue(anim, out var s) ? s.frames : 1;
        Play(anim, frames / Mathf.Max(duration, 0.01f), false, restart: !(keepIfSame && anim == _anim));
    }

    /// <summary>
    /// 한 번 재생을 두 구간으로 휘어 튼다: startFrame→keyFrame 을 keyTime 초에, 나머지를 duration 까지.
    /// 공격의 '칼이 닿는 칸'을 판정 순간에 정확히 맞출 때 쓴다.
    /// </summary>
    public void PlayKeyed(string anim, float duration, int startFrame, int keyFrame, float keyTime)
    {
        PlayOnce(anim, duration);
        _keyed = true;
        _keyStart = startFrame;
        _keyFrame = Mathf.Min(keyFrame, _frames - 1);
        _keyTime = keyTime;
        _keyDuration = duration;
        Apply();
    }

    private bool _keyed;
    private int _keyStart, _keyFrame;
    private float _keyTime, _keyDuration;

    /// <summary>한 칸에 멈춰 세운다 (회전 베기처럼 방향만 바꿔 가며 보여 줄 때).</summary>
    public void Hold(string anim, int frame)
    {
        Play(anim, 0f, false, restart: true);
        _keyed = false;
        _time = 0f;
        _holdFrame = Mathf.Clamp(frame, 0, _frames - 1);
        Apply();
    }

    private int _holdFrame = -1;

    public void Advance(float dt)
    {
        _time += dt;
        Apply();
    }

    /// <summary>몸 위치를 받아 텍셀 격자에 세운다. 그림자 원판은 땅 높이에.</summary>
    public void PlaceAt(Vector3 feet, float groundY)
    {
        var view = GameRoot.Instance.View;
        GlobalPosition = view.SnapToTexel(feet);
        _blob.GlobalPosition = new Vector3(GlobalPosition.X, groundY + 0.03f, GlobalPosition.Z);
    }

    public void SetFlash(float amount, Color? color = null)
    {
        _mat.SetShaderParameter("flash", amount);
        _mat.SetShaderParameter("flash_color", color ?? Colors.White);
    }

    /// <summary>슬로우 중에도 제 색 (주인공).</summary>
    public void ExemptFromSlow() => _mat.SetShaderParameter("slow_exempt", true);

    /// <summary>지금 그림 그대로 잔상을 하나 남긴다.</summary>
    public void SpawnGhost(Color color, float life)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ghost.gdshader") };
        mat.SetShaderParameter("albedo_tex", _mat.GetShaderParameter("albedo_tex"));
        mat.SetShaderParameter("frame_count", new Vector2(_frames, Dirs.Length));
        mat.SetShaderParameter("frame", (float)(Dir * _frames + Frame));
        mat.SetShaderParameter("color", color);
        var ghost = new MeshInstance3D
        {
            Mesh = _quad.Mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
        };
        GetParent().AddChild(ghost);
        ghost.GlobalPosition = GlobalPosition;
        var tw = ghost.CreateTween();
        tw.TweenMethod(Callable.From<float>(f => mat.SetShaderParameter("fade", f)), 0.8f, 0f, life);
        tw.TweenCallback(Callable.From(ghost.QueueFree));
    }

    private void Apply()
    {
        int f = (int)(_time * _fps);
        if (_keyed)
        {
            f = _time < _keyTime
                ? _keyStart + (int)((_keyFrame - _keyStart) * _time / _keyTime)
                : _keyFrame + (int)((_frames - _keyFrame) * (_time - _keyTime) / Mathf.Max(_keyDuration - _keyTime, 0.01f));
        }
        if (_holdFrame >= 0)
            f = _holdFrame;
        Frame = _loop ? f % _frames : Mathf.Min(f, _frames - 1);
        _mat.SetShaderParameter("frame", (float)(Dir * _frames + Frame));
    }

    /// <summary>발밑 타원 그림자. 화면에서 20x8 픽셀이 되도록 땅 위 판을 세로로 늘린다(내려다보면 세로가 줄어서).</summary>
    private static MeshInstance3D MakeBlob()
    {
        const int W = 20, H = 8;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float dx = (x + 0.5f - W * 0.5f) / (W * 0.5f);
                float dy = (y + 0.5f - H * 0.5f) / (H * 0.5f);
                float r = dx * dx + dy * dy;
                if (r <= 1f)
                    img.SetPixel(x, y, new Color(0.08f, 0.1f, 0.18f, r < 0.45f ? 0.42f : 0.28f));
            }
        }
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            RenderPriority = 1,
        };
        float sinPitch = Mathf.Sin(Mathf.DegToRad(Px.PitchDeg));
        return new MeshInstance3D
        {
            Name = "Blob",
            TopLevel = true,
            Mesh = new PlaneMesh { Size = new Vector2(W / Px.PerMeter, H / Px.PerMeter / sinPitch) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
