using System.Collections.Generic;

namespace PixelMmo.Data;

/// <summary>
/// data/npcs/*.json 1:1 매핑. 대사도 배치 좌표도 전부 데이터다 —
/// NPC 를 하나 늘리는 데 C# 을 고칠 일이 없어야 한다. (CLAUDE.md 규칙 1)
/// </summary>
public sealed class NpcDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>"merchant" | "quest" | "villager" | "guard" | "trainer". 겉모습 색만 정한다.</summary>
    public string Role { get; set; } = "villager";

    /// <summary>WorldLayout 의 구역 id. 좌표가 그 구역 밖이면 배치하지 않는다.</summary>
    public string Zone { get; set; } = "town";

    /// <summary>
    /// 어느 시설 앞에 서는가. TownGenerator 가 실제로 앉힌 자리를 찾아 그 정면에 세운다.
    /// ("smithy", "chapel", "town_hall", "training_hall", "gate_east", "plaza" …)
    ///
    /// 마을이 절차적으로 생성되므로 좌표를 손으로 적으면 대장장이가 대장간에서
    /// 멀리 떨어진 데 서 있게 된다. 비워 두면 아래 TileX/TileY 를 쓴다.
    /// </summary>
    public string Anchor { get; set; } = "";

    /// <summary>앵커가 없거나 못 찾았을 때의 배치 타일 좌표.</summary>
    public int TileX { get; set; }
    public int TileY { get; set; }

    /// <summary>"c8a05a" 같은 16진 색. 비우면 역할 기본색.</summary>
    public string Color { get; set; } = "";

    /// <summary>말을 걸 때마다 한 줄씩. 끝까지 가면 대화가 닫힌다.</summary>
    public List<string> Lines { get; set; } = new();

    /// <summary>
    /// 대사를 끝까지 들으면 이 직업으로 전직시킨다. 비면 전직 기능이 없는 NPC.
    /// 전직 조건 판정은 JobState.CanStart 가 한다.
    /// </summary>
    public string TeachesJob { get; set; } = "";

    /// <summary>조건이 안 될 때 대신 하는 한 줄. 조건은 설명하지 않는다. (규칙 4)</summary>
    public string RefuseLine { get; set; } = "";

    /// <summary>전직이 성사됐을 때 한 줄.</summary>
    public string AcceptLine { get; set; } = "";

    /// <summary>
    /// 대사를 끝까지 들으면 체력과 마나를 가득 채운다.
    /// 초보 마을에 회복 지점이 없으면 마나를 쓰는 직업이 마을에서 할 일이 없다.
    /// </summary>
    public bool Restores { get; set; }
}
