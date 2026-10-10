using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace PixelMmo.Data;

/// <summary>
/// 주인공 모습 (M6) — data/player/appearance.json.
/// 체형·머리·옷은 원화를 따로 뽑은 '바탕'을 고르고, 그 위에 옷 색만 바꾼다.
/// 한 원화 안에서 머리 색은 가죽 옷과 같은 색을 써서 떼어 바꿀 수 없다 (tools/palette.py 로 확인).
/// </summary>
public sealed class AppearanceDef
{
    public List<BaseDef> Bases { get; set; } = new();
    public List<AccentDef> Accents { get; set; } = new();

    public sealed class BaseDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>m·f — 전직하면 그 직업 원화 중 같은 성별을 입는다 (JobDef.Art)</summary>
        public string Gender { get; set; } = "m";
        /// <summary>시트 폴더 (idle·run·attack1… 이름이 모두 같다)</summary>
        public string Art { get; set; }
        /// <summary>옷 색으로 바꿀 픽셀: 색상 범위(°)와 최소 채도. tools/palette.py --mark 로 확인한 값</summary>
        public float[] AccentHue { get; set; }
        public float AccentSat { get; set; } = 0.25f;
        /// <summary>최소 명도 — 같은 색상의 검은 머리·외곽선을 빼야 할 때</summary>
        public float AccentVal { get; set; }
    }

    public sealed class AccentDef
    {
        public string Name { get; set; }
        /// <summary>새 색상(°). 없으면 원래 색상 그대로 (채도·명도만 바꾼다)</summary>
        public float? Hue { get; set; }
        public float Sat { get; set; } = 1f;
        public float Val { get; set; } = 1f;
        /// <summary>채도 하한 — 원래 옷이 칙칙한 원화(여검사 남색 하카마)에서도 새 색이 또렷하게</summary>
        public float MinSat { get; set; }
        /// <summary>0번은 '원래 색' — 아무것도 바꾸지 않는다</summary>
        public bool IsIdentity => Hue == null && Sat == 1f && Val == 1f && MinSat == 0f;
    }

    public BaseDef Find(string id) => Bases.FirstOrDefault(b => b.Id == id) ?? Bases[0];

    private static AppearanceDef _i;
    public static AppearanceDef Instance => _i ??= JsonSerializer.Deserialize<AppearanceDef>(
        FileAccess.GetFileAsString("res://data/player/appearance.json"),
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
}
