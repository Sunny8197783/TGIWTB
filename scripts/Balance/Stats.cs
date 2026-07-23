namespace PixelMmo.Balance;

/// <summary>스탯 키 문자열. JSON 의 키와 1:1 로 맞춘다. 매직 스트링 금지.</summary>
public static class Stats
{
    public const string Str = "str";
    public const string Dex = "dex";
    public const string Vit = "vit";
    public const string Int = "int";
    public const string Wis = "wis";
    public const string Luk = "luk";

    public static readonly string[] All = { Str, Dex, Vit, Int, Wis, Luk };
}
