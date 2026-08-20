using Godot;

namespace PixelMmo.Runtime;

/// <summary>
/// PixelLab 이 만든 Wang 타일셋 한 벌 (메타데이터 JSON + 스프라이트시트 PNG).
///
/// 한 벌은 '두 지형 사이의 전환'이고, 타일 16장이 네 코너가 각각 lower/upper 인
/// 16가지 조합을 전부 덮는다. 코너 조합을 Wang 인덱스로 바꾸면 타일이 하나로 정해진다.
///
///   인덱스 = NW*8 + NE*4 + SW*2 + SE   (upper = 1, lower = 0)
///
/// Godot 의 terrain 시스템(SetCellsTerrainConnect)에 태워 봤지만 칸을 칠하지 않고
/// 지워서, 타일을 직접 고르는 쪽으로 갔다. 어차피 필요한 정보가 메타데이터에 다
/// 들어 있고, 무엇을 그릴지 우리가 정하는 편이 디버깅도 쉽다.
///
/// 주의: 시트 위 위치는 반드시 각 타일의 bounding_box 로 읽는다.
/// name("wang_13")과 original_position 은 생성 당시 격자라 이 시트와 다르다.
/// </summary>
public sealed class WangTileset
{
    /// <summary>이 타일셋이 등록된 아틀라스 소스 번호.</summary>
    public int SourceId { get; private init; }

    /// <summary>Wang 인덱스(0~15) → 아틀라스 좌표.</summary>
    private readonly Vector2I[] _byWang = new Vector2I[16];

    private WangTileset() { }

    /// <summary>인덱스에 해당하는 타일이 있는가. 16장이 다 있으면 항상 true.</summary>
    public bool Has(int wang) => _byWang[wang].X >= 0;

    public Vector2I Atlas(int wang) => _byWang[wang];

    /// <summary>
    /// 타일셋을 TileSet 에 새 아틀라스 소스로 붙이고 조회표를 만든다.
    /// 에셋이 없거나 깨졌으면 null — 호출부는 도형 플레이스홀더로 폴백한다.
    /// </summary>
    public static WangTileset Load(TileSet tileSet, int sourceId,
        string metadataPath, string imagePath)
    {
        // 색은 여기서 한 번 눌러 들여온다 — 시트마다 따로 손보면 바닥이 알록달록해진다.
        Texture2D texture = ArtPalette.Ground(imagePath);
        if (texture == null)
        {
            GD.PushWarning($"[WangTileset] 시트를 못 읽었다: {imagePath}");
            return null;
        }

        using var file = FileAccess.Open(metadataPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"[WangTileset] 메타데이터를 못 읽었다: {metadataPath}");
            return null;
        }

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            GD.PushWarning($"[WangTileset] 메타데이터 JSON 오류: {metadataPath}");
            return null;
        }

        var data = json.Data.AsGodotDictionary()["tileset_data"].AsGodotDictionary();
        var sizeDict = data["tile_size"].AsGodotDictionary();
        int tileW = (int)sizeDict["width"];
        int tileH = (int)sizeDict["height"];

        var source = new TileSetAtlasSource
        {
            Texture = texture,
            TextureRegionSize = new Vector2I(tileW, tileH),
        };
        tileSet.AddSource(source, sourceId);

        var set = new WangTileset { SourceId = sourceId };
        for (int i = 0; i < set._byWang.Length; i++)
            set._byWang[i] = new Vector2I(-1, -1);

        foreach (var entry in data["tiles"].AsGodotArray())
        {
            var tile = entry.AsGodotDictionary();
            var box = tile["bounding_box"].AsGodotDictionary();
            var atlas = new Vector2I((int)box["x"] / tileW, (int)box["y"] / tileH);

            if (!source.HasTile(atlas))
                source.CreateTile(atlas);

            var corners = tile["corners"].AsGodotDictionary();
            int wang = Bit(corners, "NW") * 8 + Bit(corners, "NE") * 4
                     + Bit(corners, "SW") * 2 + Bit(corners, "SE");
            set._byWang[wang] = atlas;
        }

        int missing = 0;
        foreach (Vector2I a in set._byWang)
        {
            if (a.X < 0)
                missing++;
        }

        GD.Print($"[WangTileset] {metadataPath.GetFile()} 소스{sourceId} "
            + (missing == 0 ? "16조합 전부" : $"빠진 조합 {missing}개"));
        return missing == 0 ? set : null;
    }

    private static int Bit(Godot.Collections.Dictionary corners, string key)
        => corners[key].AsString() == "upper" ? 1 : 0;
}
