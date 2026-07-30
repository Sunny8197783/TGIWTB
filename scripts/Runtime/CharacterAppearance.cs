using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Runtime;

/// <summary>
/// 캐릭터 겉모습 — 색 슬롯 4개. 게임 스탯이 아니라 순수 꾸미기 데이터라
/// IPlayerContext(스탯)와 분리해서 둔다. 파츠는 도형이므로 색만 갈아끼우면 된다. (규칙 5)
/// P2 이후 장비·머리모양이 붙으면 여기에 슬롯을 늘린다.
/// </summary>
public sealed class CharacterAppearance
{
    public Color Skin;
    public Color Hair;
    public Color Shirt;   // 상의
    public Color Pants;   // 하의

    public static CharacterAppearance Default() => new()
    {
        Skin = PlayerTuning.SkinTones[0],
        Hair = PlayerTuning.HairColors[0],
        Shirt = PlayerTuning.ShirtColors[0],
        Pants = PlayerTuning.PantsColors[0],
    };

    /// <summary>프리셋 팔레트에서 하나씩 골라 무작위 조합. (임시 커스터마이즈 데모용)</summary>
    public void Randomize(RandomNumberGenerator rng)
    {
        Skin = Pick(rng, PlayerTuning.SkinTones);
        Hair = Pick(rng, PlayerTuning.HairColors);
        Shirt = Pick(rng, PlayerTuning.ShirtColors);
        Pants = Pick(rng, PlayerTuning.PantsColors);
    }

    private static Color Pick(RandomNumberGenerator rng, Color[] palette)
        => palette[rng.RandiRange(0, palette.Length - 1)];

    // --- 세이브 (색은 hex 문자열로) ---------------------------------------

    public SaveAppearance ToSave() => new()
    {
        Skin = Skin.ToHtml(false),
        Hair = Hair.ToHtml(false),
        Shirt = Shirt.ToHtml(false),
        Pants = Pants.ToHtml(false),
    };

    public static CharacterAppearance FromSave(SaveAppearance data)
    {
        if (data == null)
            return Default();

        return new CharacterAppearance
        {
            Skin = Parse(data.Skin, PlayerTuning.SkinTones[0]),
            Hair = Parse(data.Hair, PlayerTuning.HairColors[0]),
            Shirt = Parse(data.Shirt, PlayerTuning.ShirtColors[0]),
            Pants = Parse(data.Pants, PlayerTuning.PantsColors[0]),
        };
    }

    private static Color Parse(string hex, Color fallback)
        => string.IsNullOrEmpty(hex) ? fallback : Color.FromHtml(hex);
}
