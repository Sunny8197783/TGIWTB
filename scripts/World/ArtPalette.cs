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

    public static Texture2D Prop(string path)
        => Load(path, PropSaturation, PropValue, pullGrass: false,
                stoneGain: 0f, stoneSaturation: 0f);

    public static Texture2D Wall(string path)
        => Load(path, GroundSaturation, GroundValue, pullGrass: true,
                stoneGain: WallStoneGain, stoneSaturation: WallStoneSaturation);

    private static Texture2D Load(string path, float saturation, float value,
        bool pullGrass, float stoneGain, float stoneSaturation)
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

        Texture2D graded = ImageTexture.CreateFromImage(image);
        Cache[path] = graded;
        return graded;
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
