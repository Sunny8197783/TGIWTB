using Godot;
using PixelMmo.Runtime;

namespace PixelMmo.Combat;

/// <summary>
/// 플레이어 스프라이트 렌더러. art/player 의 8방향 idle + 8방향×8프레임 walk 를 쓴다.
///
/// 도형 피규어를 대체하지만 전투 로직은 전혀 건드리지 않는다 —
/// 방향과 상태를 읽어 알맞은 텍스처를 고르는 일만 한다. (IAnimationDriver 와 같은 관찰자 방식)
/// 공격/방어/대시 프레임은 에셋에 없으므로 그 상태에서는 해당 방향의 idle 을 쓰고,
/// 기존 모션 오프셋과 무기 궤적 VFX 가 동작을 표현한다.
/// </summary>
public partial class PlayerSprite : Sprite2D
{
    private const string Root = "res://art/player";

    /// <summary>걷기 8프레임을 한 바퀴 도는 속도(초/프레임). 8프레임 = 0.64s 주기.</summary>
    private static readonly float WalkFrameTime = 0.08f;

    /// <summary>달릴 때는 같은 프레임을 더 빠르게 넘긴다.</summary>
    private static readonly float RunFrameSpeedScale = 1.6f;

    /// <summary>에셋 캔버스가 104px 인데 캐릭터 실물은 약 28x51px. 게임 스케일에 맞춘 배율.</summary>
    private static readonly float SpriteScale = 0.55f;

    /// <summary>
    /// 발끝을 그림자에 맞추는 세로 보정.
    /// 캔버스 104px 중심(52)에서 발끝(y≈76)까지 24px → 스케일 0.55 적용 시 13.2px.
    /// 그림자는 원점 아래 12.8px(= 8 * FigureScale 1.6)이라 거의 0 이면 맞는다.
    /// </summary>
    private static readonly float FootOffsetY = -0.4f;

    /// <summary>metadata.json 의 방향 이름. 인덱스는 남(0)에서 시계 반대 방향으로 45도씩.</summary>
    private static readonly string[] DirNames =
    {
        "south", "south-east", "east", "north-east",
        "north", "north-west", "west", "south-west",
    };

    private readonly Texture2D[] _idle = new Texture2D[8];
    private readonly Texture2D[][] _walk = new Texture2D[8][];

    private float _animTime;
    private bool _loaded;

    public override void _Ready()
    {
        Name = "PlayerSprite";
        Centered = true;
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 1;                       // 무기 VFX(50+) 보다 아래
        Scale = new Vector2(SpriteScale, SpriteScale);
        Load();
    }

    private void Load()
    {
        for (int i = 0; i < DirNames.Length; i++)
        {
            _idle[i] = GD.Load<Texture2D>($"{Root}/idle/{DirNames[i]}.png");
            _walk[i] = new Texture2D[8];
            for (int f = 0; f < 8; f++)
                _walk[i][f] = GD.Load<Texture2D>($"{Root}/walk/{DirNames[i]}/frame_{f:D3}.png");
        }

        _loaded = _idle[0] != null;
        if (!_loaded)
            GD.PushWarning("[PlayerSprite] art/player 를 불러오지 못했다 — 도형 피규어로 대체된다.");
    }

    public bool IsLoaded => _loaded;

    /// <summary>
    /// 매 프레임 갱신. facing 은 바라보는 방향, moving/running 은 걷기 프레임 진행에 쓴다.
    /// </summary>
    public void UpdateFrame(Vector2 facing, bool moving, bool running, float delta)
    {
        if (!_loaded)
            return;

        int dir = DirIndex(facing);

        if (moving)
        {
            _animTime += delta * (running ? RunFrameSpeedScale : 1f);
            int frame = Mathf.PosMod((int)(_animTime / WalkFrameTime), 8);
            Texture = _walk[dir][frame];
        }
        else
        {
            _animTime = 0f;
            Texture = _idle[dir];
        }

        Position = new Vector2(0f, FootOffsetY);
    }

    /// <summary>방향 벡터 → 8방향 인덱스. south 가 0, 시계 반대로 45도씩.</summary>
    private static int DirIndex(Vector2 facing)
    {
        if (facing == Vector2.Zero)
            return 0;

        // 화면 좌표계는 +Y 가 아래(=south). south 기준 각도를 45도 단위로 반올림.
        float angle = Mathf.Atan2(facing.X, facing.Y);           // south=0, east=+90도
        int step = Mathf.PosMod(Mathf.RoundToInt(angle / (Mathf.Pi / 4f)), 8);
        return step;
    }
}
