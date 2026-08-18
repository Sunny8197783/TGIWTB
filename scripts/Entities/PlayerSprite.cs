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

    /// <summary>기본 프레임 간격(초). 걷기 8프레임 = 0.64s 주기.</summary>
    private static readonly float WalkFrameTime = 0.08f;

    /// <summary>클립별 프레임 간격. 없으면 WalkFrameTime. 숨쉬기는 느려야 자연스럽다.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, float> FrameTime = new()
    {
        ["breathe"] = 0.22f,   // 4프레임 x 0.22 ≈ 0.9s 한 호흡
        ["guard"] = 0.16f,     // 4프레임 x 0.16 ≈ 0.64s — 자세를 유지한 채 숨만 쉰다
    };

    private float FrameTimeOf(string clip)
        => FrameTime.TryGetValue(clip, out float t) ? t : WalkFrameTime;

    /// <summary>
    /// 순환 클립의 '루프 시작 프레임'. 앞쪽 구간은 처음 한 번만 지나가고 그 뒤로는
    /// 여기서부터 반복한다.
    ///
    /// PixelLab v3 는 항상 서 있는 회전 이미지에서 출발하므로, 순환 동작이라도 앞
    /// 한두 프레임은 '자세를 잡는' 구간이 된다. 그대로 돌리면 방어는 한 바퀴마다
    /// 가드를 풀었다 다시 잡고, 질주는 한 바퀴마다 멈칫한다. 그 구간을 진입 동작으로
    /// 쓰고 나머지만 순환시키면 둘 다 사라진다.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> LoopStart = new()
    {
        ["run"] = 2,     // 0~1 은 출발 자세, 2~7 여섯 프레임이 실제 질주 한 사이클
        ["guard"] = 2,   // 0~1 은 가드를 올리는 동작, 2~3 이 자세를 유지한 채 쉬는 숨
    };

    private int LoopStartOf(string clip)
        => LoopStart.TryGetValue(clip, out int i) ? i : 0;

    /// <summary>
    /// 달릴 때는 같은 프레임을 더 빠르게 넘긴다.
    ///
    /// 질주 클립이 9프레임 전체 순환에서 8프레임 중 뒤 6프레임 순환으로 바뀌면서
    /// 같은 배율이면 한 사이클이 0.45s → 0.30s 로 확 짧아진다. 그 정도면 다리가
    /// 버둥거리는 것처럼 보이고 오히려 힘이 빠진다. 한 사이클 0.36s (6 x 0.06s) 가
    /// 되도록 잡았다 — 이전보다 빠르지만 보폭 하나하나는 읽힌다.
    /// 0.08(기본 간격) / 0.06 = 1.33
    /// </summary>
    private static readonly float RunFrameSpeedScale = 1.33f;

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

    /// <summary>
    /// 클립 이름 → [방향][프레임]. art/player/{클립}/{방향}/frame_NNN.png 규칙으로 읽는다.
    /// 폴더가 없으면 그 클립은 등록되지 않고, 재생 요청 시 idle 로 폴백한다.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<string, Texture2D[][]> _clips = new();

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

    /// <summary>있으면 읽고 없으면 건너뛰는 클립 목록. 에셋을 추가하면 여기만 늘리면 된다.</summary>
    private static readonly string[] ClipNames =
        { "breathe", "walk", "run", "dash", "punch", "heavy", "guard", "block", "shout" };

    private void Load()
    {
        for (int i = 0; i < DirNames.Length; i++)
            _idle[i] = GD.Load<Texture2D>($"{Root}/idle/{DirNames[i]}.png");

        foreach (string clip in ClipNames)
            LoadClip(clip);

        _loaded = _idle[0] != null;
        if (!_loaded)
            GD.PushWarning("[PlayerSprite] art/player 를 불러오지 못했다 — 도형 피규어로 대체된다.");
        else
            GD.Print($"[PlayerSprite] clips={string.Join(",", _clips.Keys)}");
    }

    /// <summary>클립 하나를 8방향 x N프레임으로 읽는다. 파일이 없으면 등록하지 않는다.</summary>
    private void LoadClip(string clip)
    {
        var byDir = new Texture2D[8][];

        for (int i = 0; i < DirNames.Length; i++)
        {
            var frames = new System.Collections.Generic.List<Texture2D>();
            for (int f = 0; f < MaxFrames; f++)
            {
                string path = $"{Root}/{clip}/{DirNames[i]}/frame_{f:D3}.png";
                if (!ResourceLoader.Exists(path))
                    break;
                var tex = GD.Load<Texture2D>(path);
                if (tex == null)
                    break;
                frames.Add(tex);
            }

            if (frames.Count == 0)
                return;                     // 이 클립은 에셋이 없다.
            byDir[i] = frames.ToArray();
        }

        _clips[clip] = byDir;
    }

    /// <summary>한 클립에서 읽어 볼 최대 프레임 수. 실제 개수는 파일 존재로 정해진다.</summary>
    private const int MaxFrames = 24;

    public bool IsLoaded => _loaded;

    public bool HasClip(string clip) => _clips.ContainsKey(clip);

    /// <summary>
    /// 한 번 재생하고 마지막 프레임에서 멈추는 클립.
    ///
    /// 지금은 비어 있다. 방어(guard)는 '자세를 잡는 동작'이 아니라 자세를 유지한 채
    /// 숨만 쉬는 4프레임 순환으로 다시 만들었기 때문에, 반복해도 들썩거리지 않는다.
    /// 피격 반동은 별도의 block 클립이 진행도로 한 번만 재생한다.
    /// 자세를 잡고 굳어야 하는 클립이 새로 생기면 여기에 이름만 넣으면 된다.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> HoldClips = new();

    /// <summary>
    /// 매 프레임 갱신. clip 은 재생할 애니메이션 이름("walk"/"run"/"punch"…),
    /// progress 는 1회성 동작(공격 등)의 진행도 0~1. null 이면 루프(또는 홀드).
    /// 클립이 없으면 idle 로 폴백하므로 에셋이 없어도 안전하다.
    /// </summary>
    public void UpdateFrame(Vector2 facing, string clip, float? progress, float delta)
    {
        if (!_loaded)
            return;

        int dir = DirIndex(facing);

        // 클립이 바뀌면 위상을 먼저 리셋한다 — 새 동작이 항상 첫 프레임부터 시작하도록.
        if (clip != _lastClip)
        {
            _animTime = 0f;
            _lastClip = clip;
        }

        if (clip != null && _clips.TryGetValue(clip, out var byDir))
        {
            Texture2D[] frames = byDir[dir];

            if (progress.HasValue)
            {
                // 1회성 동작 — 상태 진행도에 프레임을 직접 맞춘다. 되감기지 않는다.
                int f = Mathf.Clamp((int)(progress.Value * frames.Length), 0, frames.Length - 1);
                Texture = frames[f];
            }
            else if (HoldClips.Contains(clip))
            {
                // 홀드 — 자세를 잡고 마지막 프레임에서 정지한다. 클립이 바뀔 때 _animTime 이
                // 리셋되므로(아래 else 절과 상태 전환) 다시 들어오면 처음부터 잡는다.
                _animTime += delta;
                int f = Mathf.Min((int)(_animTime / FrameTimeOf(clip)), frames.Length - 1);
                Texture = frames[f];
            }
            else
            {
                // 루프 — 이동/대기 클립. 달리기는 프레임을 더 빨리 넘긴다.
                _animTime += delta * (clip == "run" ? RunFrameSpeedScale : 1f);

                int i = (int)(_animTime / FrameTimeOf(clip));
                int start = LoopStartOf(clip);
                int f = (start <= 0 || start >= frames.Length)
                    ? Mathf.PosMod(i, frames.Length)
                    : (i < frames.Length
                        ? i                                       // 첫 바퀴: 진입 구간부터 전부
                        : start + Mathf.PosMod(i - frames.Length, frames.Length - start));
                Texture = frames[f];
            }
        }
        else
        {
            Texture = _idle[dir];
        }

        Position = new Vector2(0f, FootOffsetY);
    }

    private string _lastClip;

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
