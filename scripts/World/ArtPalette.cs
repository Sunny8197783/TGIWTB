using System.Collections.Generic;
using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// 마을 아트의 색을 한 번에 눌러 준다.
///
/// PixelLab 이 뽑아 주는 그림은 낱장으로 보면 예쁘지만, 수백 장을 한 화면에
/// 깔면 잔디는 형광 초록, 흙은 주황, 포석은 남색이라 서로 싸운다. 명세가
/// 요구한 '채도 낮은 자연색'은 프롬프트로는 잡히지 않는다 —
/// 색 형용사를 넣으면 오히려 색이 튄다(흙 타일셋을 여섯 번 말아먹고 배웠다).
///
/// 그래서 생성이 아니라 **불러오는 길목에서** 한 번 눌러 통일한다.
/// 원본 PNG 는 건드리지 않으므로 배율만 바꾸면 언제든 되돌릴 수 있고,
/// 새 에셋을 받아도 자동으로 같은 색이 된다.
/// </summary>
public static class ArtPalette
{
    /// <summary>바닥 타일. 화면의 대부분을 차지하므로 가장 많이 눌러야 한다.</summary>
    public const float GroundSaturation = 0.52f;
    public const float GroundValue = 0.84f;

    /// <summary>건물·소품. 바닥보다는 살아 있어야 눈이 간다.</summary>
    public const float PropSaturation = 0.82f;
    public const float PropValue = 0.96f;

    // ── 흰 회벽 누르기 ────────────────────────────────────────────
    //
    // 낱장으로는 멀쩡한 그림도 수백 장 사이에 놓이면 혼자 튄다 — 흰 회벽 2층집이
    // 전경에서 희끄무레한 점으로 보였다.
    //
    // 무엇으로 재느냐를 두 번 틀렸다. 평균 밝기로 재면 이 집이 안 걸린다(벽만
    // 새하얗고 지붕·그늘이 어두워 평균이 가운데로 끌려온다). 상위 분위(p85)로
    // 재도 안 갈린다(멀쩡한 집들도 창틀·차양에 흰 픽셀이 있어 다 0.93 근처다).
    // 튀는 이유는 '밝은 픽셀이 있다'가 아니라 **넓은 면이 흰 회벽**이라는 것이라,
    // 그 면적 비율을 직접 재야 갈린다:
    //
    //   roof_m08 0.599  ←  여관 0.249  ←  본진 0.12~0.23
    //
    // 이름을 적어 두지 않고 숫자로 거르는 이유: 앞으로 흰 에셋이 들어와도 목록을
    // 고치지 않고 자동으로 잡히고, 무엇이 눌렸는지 로그에 남는다.

    /// <summary>흰 회벽으로 볼 픽셀의 기준.</summary>
    private const float WhitewashValue = 0.70f;
    private const float WhitewashSaturation = 0.34f;


    /// <summary>
    /// 이 비율을 넘으면 회벽 집으로 본다.
    ///
    /// 처음에는 0.30 으로 잡아 가장 튀는 한 장만 눌렀는데, 정작 전경에서 흰 점으로
    /// 보이던 것들은 그게 아니라 **회벽 계열 전체**(roof_m01/m02, kit_m12, kit_l07,
    /// 여관 — 전부 0.20~0.25)였다. 프롬프트에 whitewashed plaster 를 넣어 뽑은
    /// 집들이라 한 무리로 몰려 있다. 0.19 면 그 무리가 다 들어오고,
    /// 분수(0.188)·허수아비(0.167) 같은 흰 것 아닌 것들은 안 걸린다.
    /// </summary>
    private const float WhitewashLimit = 0.19f;

    /// <summary>
    /// 흰 면을 누르는 배율. 그림 전체가 아니라 **흰 픽셀만** 누른다 —
    /// 전체를 누르면 어두운 지붕과 그늘까지 같이 뭉개져 그림이 납작해진다.
    /// 채도를 조금 올려 회색이 아니라 회벽으로 읽히게 한다.
    /// </summary>
    private const float WhitewashDim = 0.74f;
    private const float WhitewashWarmth = 0.05f;

    /// <summary>
    /// 성벽 시트의 돌 밝기 배율. 바닥용 등급을 그대로 쓰면 '채도 낮은 픽셀은 밝게'
    /// (포석용) 규칙에 걸려 벽이 하얗게 떠서, 마을을 두른 흰 띠가 되고 문루보다
    /// 밝아진다. 성벽은 무겁고 어두워야 마을을 지키는 것으로 보인다.
    ///
    /// 잔디 규칙은 그대로 둔다 — 성벽 시트에도 잔디가 절반쯤 들어 있어서,
    /// 여기만 다르게 누르면 성벽 둘레에 색이 다른 잔디 띠가 생긴다.
    /// </summary>
    private const float WallStoneGain = 0.62f;
    private const float WallStoneSaturation = 0.55f;

    /// <summary>
    /// 형광 초록을 올리브 쪽으로 끌어온다. 채도만 낮추면 회색빛 초록이 되어
    /// 여전히 '게임 잔디'처럼 보인다 — 색상 자체를 노란 쪽으로 옮겨야 풀이 된다.
    /// (HSV 색상환에서 0.33 이 순수 초록, 0.17 이 누런 풀색)
    /// </summary>
    private const float GrassHueTarget = 0.19f;
    private const float GrassHuePull = 0.55f;

    /// <summary>이 범위의 색상만 잔디로 본다.</summary>
    private const float GrassHueLow = 0.22f;
    private const float GrassHueHigh = 0.42f;

    /// <summary>
    /// 포석을 따뜻한 회색으로. 생성된 포석은 채도가 낮은 **푸른** 회색이라
    /// 채도를 더 낮추면 그냥 검은 얼룩이 된다 — 화면 한가운데 광장이 구멍처럼 보인다.
    /// 채도가 낮은 픽셀(=돌)만 골라 색을 흙빛으로 돌리고 밝기를 올린다.
    /// </summary>
    private const float StoneSaturationMax = 0.34f;
    private const float StoneHueTarget = 0.08f;
    private const float StoneHuePull = 0.7f;
    private const float StoneValueGain = 1.32f;

    /// <summary>같은 그림을 수백 번 쓰므로 경로마다 한 번만 처리한다.</summary>
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Ground(string path)
        => Load(path, GroundSaturation, GroundValue, pullGrass: true,
                stoneGain: StoneValueGain, stoneSaturation: 0.70f);

    /// <summary>
    /// 소품·건물. 바닥 시트와 달리 **장마다 밝기를 맞춘다** — 바닥은 두세 장이
    /// 화면 전체를 덮으니 서로 튈 일이 없지만, 소품은 팔십여 장이 섞여 깔린다.
    /// </summary>
    public static Texture2D Prop(string path)
        => Load(path, PropSaturation, PropValue, pullGrass: false,
                stoneGain: 0f, stoneSaturation: 0f, dimWhitewash: true);

    public static Texture2D Wall(string path)
        => Load(path, GroundSaturation, GroundValue, pullGrass: true,
                stoneGain: WallStoneGain, stoneSaturation: WallStoneSaturation);

    private static Texture2D Load(string path, float saturation, float value,
        bool pullGrass, float stoneGain, float stoneSaturation, bool dimWhitewash = false)
    {
        if (Cache.TryGetValue(path, out Texture2D cached))
            return cached;

        var source = GD.Load<Texture2D>(path);
        if (source == null)
            return null;

        Image image = source.GetImage();
        if (image == null)
        {
            Cache[path] = source;
            return source;
        }

        // 압축된 텍스처는 픽셀을 못 건드린다. 그때는 원본을 그대로 쓴다 —
        // 색이 안 맞는 편이 아무 것도 안 보이는 것보다 낫다.
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            Cache[path] = source;
            return source;
        }

        Grade(image, saturation, value, pullGrass, stoneGain, stoneSaturation);

        if (dimWhitewash)
            DimWhitewash(image, path);

        Texture2D graded = ImageTexture.CreateFromImage(image);
        Cache[path] = graded;
        return graded;
    }

    /// <summary>
    /// 그림의 절반 가까이가 흰 회벽이면 그 흰 픽셀만 회벽 톤으로 눌러 내린다.
    /// </summary>
    private static void DimWhitewash(Image image, string path)
    {
        float fraction = WhitewashFraction(image);
        if (fraction <= WhitewashLimit)
            return;

        int w = image.GetWidth(), h = image.GetHeight();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A <= 0f || !IsWhitewash(c))
                    continue;

                image.SetPixel(x, y, Color.FromHsv(
                    c.H,
                    Mathf.Min(c.S + WhitewashWarmth, 1f),
                    c.V * WhitewashDim,
                    c.A));
            }
        }

        GD.Print($"[Palette] {path.GetFile()} 흰 면이 {fraction:P0} 라 회벽 톤으로 눌렀다");
    }

    private static bool IsWhitewash(Color c)
        => c.V > WhitewashValue && c.S < WhitewashSaturation;


    /// <summary>
    /// 불투명 픽셀 밝기의 상위 분위값.
    ///
    /// 평균으로 재면 흰 회벽 집이 안 걸린다 — 벽만 새하얗고 지붕·그늘이 어두워서
    /// 평균이 가운데로 끌려오기 때문이다. 눈에 띄는 건 평균이 아니라 **넓은 밝은
    /// 면**이므로 상위 분위로 재야 한다.
    /// </summary>
    private static float WhitewashFraction(Image image)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        int white = 0, total = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A <= 0.5f)
                    continue;
                total++;
                if (IsWhitewash(c))
                    white++;
            }
        }

        return total == 0 ? 0f : (float)white / total;
    }

    /// <summary>
    /// 픽셀마다 HSV 로 눌러 준다. Image.AdjustBcs 는 색상(H)을 못 건드려서
    /// 형광 초록이 그대로 남는다 — 그래서 직접 돈다. 시트가 커야 128x128 이라
    /// 로딩에서 티가 나지 않는다.
    /// </summary>
    private static void Grade(Image image, float saturation, float value,
        bool pullGrass, float stoneGain, float stoneSaturation)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A <= 0f)
                    continue;

                float hue = c.H, sat = c.S, val = c.V;
                float outSat = sat * saturation;
                float outVal = val * value;

                if (pullGrass)
                {
                    if (sat > 0.25f && hue > GrassHueLow && hue < GrassHueHigh)
                    {
                        hue = Mathf.Lerp(hue, GrassHueTarget, GrassHuePull);
                    }
                    else if (sat <= StoneSaturationMax && stoneGain > 0f)
                    {
                        hue = Mathf.Lerp(hue, StoneHueTarget, StoneHuePull);
                        outSat = sat * stoneSaturation;
                        outVal = val * value * stoneGain;
                    }
                }

                image.SetPixel(x, y, Color.FromHsv(
                    hue,
                    Mathf.Clamp(outSat, 0f, 1f),
                    Mathf.Clamp(outVal, 0f, 1f),
                    c.A));
            }
        }
    }
}
