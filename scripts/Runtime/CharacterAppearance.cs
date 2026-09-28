using Godot;
using PixelMmo.Balance;

namespace PixelMmo.Runtime;

/// <summary>The same cosmetic choices drive preview, field art and saves.</summary>
public sealed class CharacterAppearance
{
    public const int StyleCount = 60, EyeCount = 60, BodyCount = 6;
    public bool Female, Confirmed;
    public bool UsePixelLab=true;
    public int HairStyle, EyeShape, Body;
    public Color Skin, Hair, Shirt, Pants;
    public static readonly string[] BodyNames = { "균형", "아담", "늘씬", "건장", "다부짐", "포근함" };
    public static readonly Color[] HairColors = Palette("201c1c,382723,54382b,714a32,93643f,b58650,d4ac69,edcf92,f2e4bd,d3d3ca,959aa0,59606b,432c29,773b31,a44d35,c97643,e2a060,62323e,9a4d62,cf8092,533a63,795687,ab8fb9,2e3e61,48658c,7d9cbd,284b45,47766a,829a6b,b5bc8d");
    public static readonly Color[] SkinColors = Palette("f6dfce,efd0b5,e6bb98,d9a582,c9926d,b68059,a86d49,925a3d,7b4934,643c2e,503127,3c2721");
    public static CharacterAppearance Default() => new() { Skin=SkinColors[2],Hair=HairColors[2],Shirt=PlayerTuning.ShirtColors[0],Pants=PlayerTuning.PantsColors[0] };
    private static Color[] Palette(string values) => System.Array.ConvertAll(values.Split(','), value=>new Color(value));
    public CharacterAppearance Copy() => (CharacterAppearance)MemberwiseClone();
    public void Randomize(RandomNumberGenerator rng)
    {
        HairStyle=rng.RandiRange(0,StyleCount-1); EyeShape=rng.RandiRange(0,EyeCount-1); Body=rng.RandiRange(0,BodyCount-1);
        Skin=SkinColors[rng.RandiRange(0,SkinColors.Length-1)]; Hair=HairColors[rng.RandiRange(0,HairColors.Length-1)];
    }
    public SaveAppearance ToSave() => new() { Skin=Skin.ToHtml(false),Hair=Hair.ToHtml(false),Shirt=Shirt.ToHtml(false),Pants=Pants.ToHtml(false),
        Female=Female,HairStyle=HairStyle,EyeShape=EyeShape,Body=Body,Confirmed=Confirmed,UsePixelLab=UsePixelLab };
    public static CharacterAppearance FromSave(SaveAppearance data)
    {
        if(data==null) return Default();
        var value=Default();
        value.Skin=Parse(data.Skin,value.Skin); value.Hair=Parse(data.Hair,value.Hair);
        value.Shirt=Parse(data.Shirt,value.Shirt); value.Pants=Parse(data.Pants,value.Pants);
        value.Female=data.Female; value.Confirmed=data.Confirmed;value.UsePixelLab=data.UsePixelLab;
        value.HairStyle=Mathf.Clamp(data.HairStyle,0,StyleCount-1); value.EyeShape=Mathf.Clamp(data.EyeShape,0,EyeCount-1); value.Body=Mathf.Clamp(data.Body,0,BodyCount-1);
        return value;
    }
    private static Color Parse(string hex, Color fallback) => !string.IsNullOrEmpty(hex) && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
    public static string HairName(int index, bool female)
    {
        string[] names=female
            ? new[]{"픽시", "단발", "웨이브 단발", "긴 생머리", "물결 머리", "포니테일", "트윈테일", "땋은 머리", "올림머리", "반묶음", "곱슬머리", "옆땋기"}
            : new[]{"크롭", "댄디", "쉼표 머리", "장발", "울프컷", "묶음 머리", "투블럭", "브레이드", "상투", "리프컷", "곱슬머리", "옆넘김"};
        return names[index/5]+" · "+new[]{"내린 앞머리","가르마","사선 앞머리","짧은 앞머리","갈라진 앞머리"}[index%5];
    }
    public static string EyeName(int index) => new[]{"둥근", "아몬드", "또렷한", "차분한", "날카로운", "부드러운", "고양이", "처진", "깊은", "웃는"}[index/6]+" 눈 "+(index%6+1);
}
