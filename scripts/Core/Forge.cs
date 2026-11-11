using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace PixelMmo.Core;

/// <summary>
/// 빚기 — 고른 항목과 자유 묘사로 PixelLab 에서 세상에 하나뿐인 주인공 원화와 동작을 만들어 받아 온다 (설계: docs/design/NAMELESS.md).
///  1) Create: 묘사 → 8방향 원화 (생성 2회, 2~3분) → user://forge/&lt;id&gt;/rot.png. 마음에 들 때까지 다시 빚는다.
///  2) Animate: 정한 원화로 동작 아홉 가지를 하나씩 만든다 (동작마다 8방향 = 생성 8회, 3~4분). 뒤에서 돌고, 다 되면 Done.
///     동작 묘사·칸 수는 data/player/forge.json — 기성 원화(hero)와 같은 박자라 전투 판정 칸이 그대로 맞는다.
/// 시트로 묶는 법은 tools/fetch_assets.py 의 pack 과 같다: 원화의 발밑 가운데를 칸의 닻에 두고, 동작의 첫 칸을 원화에 겹쳐 맞춘다.
/// 열쇠: user://pixellab_key.txt (저장소에 넣지 않는다). 인터넷과 PixelLab 생성 횟수가 든다 — 화면이 그 사실을 알린다.
/// </summary>
public sealed class Forge
{
    public const string Root = "user://forge";
    private const string Api = "https://api.pixellab.ai/v2";
    private const string ZipUrl = "https://api.pixellab.ai/mcp/characters/{0}/download";
    private const int Cell = 128;
    private static readonly (int X, int Y) Anchor = (64, 116);   // Render/CharacterSprite 와 같아야 한다
    private const int AlignReach = 12;
    private const float PollSeconds = 6f;
    public static readonly string[] Dirs = { "south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west" };

    public sealed class AnimSpec
    {
        public string Name { get; set; }
        public string Template { get; set; }
        public string Action { get; set; }
        public int Frames { get; set; } = 8;
        public bool KeepFirst { get; set; }
    }

    public sealed class Meta
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public List<string> Done { get; set; } = new();
        public bool Complete { get; set; }
    }

    private static Forge _i;
    public static Forge Instance => _i ??= new Forge();

    private readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private List<AnimSpec> _anims;

    /// <summary>지금 하는 일 한 줄 (화면에 띄운다)과 동작 만들기 진행 0~1</summary>
    public string Status { get; private set; } = "";
    public float Progress { get; private set; }
    public bool Busy { get; private set; }
    public string Error { get; private set; }
    /// <summary>남은 생성 횟수 (모르면 -1)</summary>
    public int Balance { get; private set; } = -1;
    /// <summary>동작이 모두 완성됐다 (빚은 모습 id)</summary>
    public event Action<string> Done;

    public static string KeyPath => "user://pixellab_key.txt";
    public bool HasKey => Godot.FileAccess.FileExists(KeyPath) && Godot.FileAccess.GetFileAsString(KeyPath).Trim().Length > 8;
    public static string Folder(string id) => $"{Root}/{id}";
    public static bool Ready(string id) => id != null && ReadMeta(id)?.Complete == true;
    public static bool HasPreview(string id) => id != null && Godot.FileAccess.FileExists(Folder(id) + "/rot.png");

    public IReadOnlyList<AnimSpec> Anims => _anims ??= LoadAnims();

    private static List<AnimSpec> LoadAnims()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://data/player/forge.json"),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return JsonSerializer.Deserialize<List<AnimSpec>>(doc.RootElement.GetProperty("anims").GetRawText(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    public static Meta ReadMeta(string id)
    {
        string path = Folder(id) + "/meta.json";
        if (!Godot.FileAccess.FileExists(path))
            return null;
        try { return JsonSerializer.Deserialize<Meta>(Godot.FileAccess.GetFileAsString(path)); }
        catch (JsonException) { return null; }
    }

    private static void WriteMeta(Meta m)
    {
        using var f = Godot.FileAccess.Open(Folder(m.Id) + "/meta.json", Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(m));
    }

    // ── 통신 ────────────────────────────────────────────

    private async Task<JsonElement> Call(HttpMethod method, string path, object body = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, Api + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Godot.FileAccess.GetFileAsString(KeyPath).Trim());
            req.Headers.UserAgent.ParseAdd("pixellab-forge");
            if (body != null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            string text = await res.Content.ReadAsStringAsync();
            // 429(동시 작업 한도)·5xx 는 잠깐 쉬고 다시
            if (((int)res.StatusCode == 429 || (int)res.StatusCode >= 500) && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(20 * (attempt + 1)));
                continue;
            }
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"PixelLab {(int)res.StatusCode}: {(text.Length > 200 ? text[..200] : text)}");
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }

    private async Task WaitJob(string jobId)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(PollSeconds));
            JsonElement j;
            try { j = await Call(HttpMethod.Get, $"/background-jobs/{jobId}"); }
            catch (InvalidOperationException e) when (e.Message.Contains("404")) { return; }   // 끝난 작업은 치워진다
            string status = j.GetProperty("status").GetString();
            if (status == "processing")
                continue;
            if (status != "completed")
                throw new InvalidOperationException("생성이 실패했다");
            return;
        }
    }

    public async Task RefreshBalance()
    {
        if (!HasKey)
            return;
        try
        {
            var b = await Call(HttpMethod.Get, "/balance");
            Balance = (int)b.GetProperty("subscription").GetProperty("generations").GetDouble();
        }
        catch (Exception) { Balance = -1; }
    }

    // ── 1) 원화 ─────────────────────────────────────────

    /// <summary>묘사로 8방향 원화를 빚는다. 돌려주는 값 = 빚은 모습 id (실패하면 null, Error 에 까닭)</summary>
    public async Task<string> Create(string description)
    {
        if (Busy)
            return null;
        Busy = true;
        Error = null;
        Progress = 0f;
        try
        {
            Status = "이름 없는 흙을 빚는 중…";
            var r = await Call(HttpMethod.Post, "/create-character-v3", new
            {
                description, name = "forged hero", view = "high top-down",
                image_size = new { width = 64, height = 64 }, template_id = "mannequin",
                outline = "selective outline", detail = "high detail", no_background = true,
            });
            string id = r.GetProperty("character_id").GetString();
            await WaitJob(r.GetProperty("background_job_id").GetString());
            Status = "빚은 모습을 받아 오는 중…";
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder(id)));
            var zip = await Download(id);
            var rot = ReadRotations(zip);
            SaveSheet(id, "rot", PackRot(rot), 1);
            WriteMeta(new Meta { Id = id, Description = description });
            Status = "";
            _ = RefreshBalance();
            return id;
        }
        catch (Exception e)
        {
            Error = Friendly(e);
            GD.PrintErr("[Forge] " + e);
            Status = "";
            return null;
        }
        finally { Busy = false; }
    }

    // ── 2) 동작 ─────────────────────────────────────────

    /// <summary>정한 원화의 동작을 하나씩 만든다 (끊겼던 것은 이어서). 다 되면 Done</summary>
    public async Task Animate(string id)
    {
        if (Busy)
            return;
        var meta = ReadMeta(id);
        if (meta == null || meta.Complete)
            return;
        Busy = true;
        Error = null;
        try
        {
            var specs = Anims;
            for (int i = 0; i < specs.Count; i++)
            {
                var a = specs[i];
                Progress = i / (float)specs.Count;
                if (meta.Done.Contains(a.Name))
                    continue;
                Status = $"동작을 새기는 중  {i + 1}/{specs.Count}";
                object body = a.Template != null
                    ? new { character_id = id, animation_name = "forge_" + a.Name, directions = Dirs, template_animation_id = a.Template, mode = "template" }
                    : new { character_id = id, animation_name = "forge_" + a.Name, directions = Dirs, action_description = a.Action, mode = "v3",
                            frame_count = a.Frames, keep_first_frame = a.KeepFirst };
                string folder = a.Template != null ? a.Template.Replace("-", "_") : "forge_" + a.Name;
                // 지난번에 내 놓고 받기 전에 게임이 꺼졌을 수 있다 — 이미 다 그려져 있으면 다시 내지 않는다 (생성 횟수를 두 번 쓰지 않게)
                var zip = await Download(id);
                var frames = ReadFrames(zip, folder);
                if (frames.Count < Dirs.Length)
                {
                    var r = await Call(HttpMethod.Post, "/characters/animations", body);
                    if (r.TryGetProperty("background_job_ids", out var jobs))
                        foreach (var j in jobs.EnumerateArray())
                            await WaitJob(j.GetString());
                    zip = await Download(id);
                    frames = ReadFrames(zip, folder);
                }
                var rot = ReadRotations(zip);
                if (frames.Count == 0)
                    throw new InvalidOperationException($"동작 '{a.Name}' 을 받지 못했다");
                // 겹쳐 맞추기는 무겁다 — 화면이 멈칫하지 않게 다른 실에서 (바이트 배열만 만진다)
                var sheet = await Task.Run(() =>
                {
                    byte[] p = Pack(rot, frames, out int c);
                    return (px: p, count: c);
                });
                SaveSheet(id, a.Name, sheet.px, sheet.count);
                meta.Done.Add(a.Name);
                WriteMeta(meta);
            }
            meta.Complete = true;
            WriteMeta(meta);
            Progress = 1f;
            Status = "";
            _ = RefreshBalance();
            Done?.Invoke(id);
        }
        catch (Exception e)
        {
            Error = Friendly(e);
            GD.PrintErr("[Forge] " + e);
            Status = "";
        }
        finally { Busy = false; }
    }

    private static string Friendly(Exception e) => e switch
    {
        HttpRequestException => "PixelLab 에 닿지 못했다 — 인터넷을 확인한다",
        TaskCanceledException => "PixelLab 이 대답하지 않는다 — 잠시 뒤에 다시",
        _ => e.Message,
    };

    // ── 받기·묶기 ───────────────────────────────────────

    private readonly record struct Pic(int W, int H, byte[] Px);   // RGBA8

    private async Task<ZipArchive> Download(string id)
    {
        // 동작을 만드는 중이면 zip 을 안 준다 — 몇 번 다시 청한다
        for (int attempt = 0; ; attempt++)
        {
            using var res = await _http.GetAsync(string.Format(ZipUrl, id));
            if (res.IsSuccessStatusCode)
                return new ZipArchive(new MemoryStream(await res.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
            if (attempt >= 8)
                throw new InvalidOperationException("빚은 그림을 내려받지 못했다");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }

    private static Pic Decode(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var img = new Image();
        if (img.LoadPngFromBuffer(ms.ToArray()) != Godot.Error.Ok)
            return default;
        img.Convert(Image.Format.Rgba8);
        return new Pic(img.GetWidth(), img.GetHeight(), img.GetData());
    }

    private static Dictionary<string, Pic> ReadRotations(ZipArchive zip)
    {
        var map = new Dictionary<string, Pic>();
        foreach (string d in Dirs)
        {
            var e = zip.Entries.FirstOrDefault(x => x.FullName.EndsWith($"/rotations/{d}.png") || x.FullName == $"rotations/{d}.png");
            if (e != null)
                map[d] = Decode(e);
        }
        return map;
    }

    private static Dictionary<string, List<Pic>> ReadFrames(ZipArchive zip, string anim)
    {
        var map = new Dictionary<string, List<Pic>>();
        foreach (string d in Dirs)
        {
            string mark = $"/animations/{anim}/{d}/";
            var entries = zip.Entries.Where(x => ("/" + x.FullName).Contains(mark) && x.FullName.EndsWith(".png"))
                .OrderBy(x => x.FullName, StringComparer.Ordinal).ToList();
            if (entries.Count > 0)
                map[d] = entries.Select(Decode).ToList();
        }
        return map;
    }

    private static int BottomRow(Pic p)
    {
        for (int y = p.H - 1; y >= 0; y--)
            for (int x = 0; x < p.W; x++)
                if (p.Px[(y * p.W + x) * 4 + 3] > 127)
                    return y;
        return p.H - 1;
    }

    private static byte[] PackRot(Dictionary<string, Pic> rot)
    {
        var one = rot.ToDictionary(kv => kv.Key, kv => new List<Pic> { kv.Value });
        return Pack(rot, one, out _, align: false);
    }

    /// <summary>가로 = 칸, 세로 = 8방향. 원화의 발밑 가운데를 닻에 두고, 동작은 방향마다 첫 칸을 원화에 겹쳐 가장 잘 맞는 자리에 놓는다</summary>
    private static byte[] Pack(Dictionary<string, Pic> rot, Dictionary<string, List<Pic>> frames, out int count, bool align = true)
    {
        count = frames.Values.Select(f => f.Count).DefaultIfEmpty(1).Max();
        int W = Cell * count, H = Cell * Dirs.Length;
        var px = new byte[W * H * 4];
        for (int row = 0; row < Dirs.Length; row++)
        {
            if (!rot.TryGetValue(Dirs[row], out var reference) || reference.Px == null)
                continue;
            int rx = Anchor.X - reference.W / 2;
            int ry = Anchor.Y - BottomRow(reference);
            // 아직 없는 방향은 서 있는 원화로 채운다
            var list = frames.TryGetValue(Dirs[row], out var f) && f.Count == count && f[0].Px != null
                ? f : Enumerable.Repeat(reference, count).ToList();
            (int dx, int dy) off = (0, 0);
            if (align && !ReferenceEquals(list[0].Px, reference.Px))
                off = Align(reference, list[0], ((reference.W - list[0].W) / 2, BottomRow(reference) - BottomRow(list[0])));
            for (int col = 0; col < count; col++)
            {
                var img = list[col];
                if (img.Px == null)
                    continue;
                int ox = col * Cell + rx + off.dx, oy = row * Cell + ry + off.dy;
                for (int y = 0; y < img.H; y++)
                {
                    int ty = oy + y;
                    if (ty < row * Cell || ty >= (row + 1) * Cell)
                        continue;
                    for (int x = 0; x < img.W; x++)
                    {
                        int tx = ox + x;
                        int s = (y * img.W + x) * 4;
                        if (img.Px[s + 3] <= 127 || tx < col * Cell || tx >= (col + 1) * Cell)
                            continue;
                        int t = (ty * W + tx) * 4;
                        px[t] = img.Px[s];
                        px[t + 1] = img.Px[s + 1];
                        px[t + 2] = img.Px[s + 2];
                        px[t + 3] = 255;
                    }
                }
            }
        }
        return px;
    }

    /// <summary>img 를 원화 위에 겹쳤을 때 색이 가장 많이 맞는 (dx, dy) — PixelLab 은 동작마다 캔버스 안 자리가 달라 그림으로 맞춘다</summary>
    private static (int, int) Align(Pic reference, Pic img, (int x, int y) guess)
    {
        int best = -1;
        (int, int) bestOff = guess;
        for (int dy = guess.y - AlignReach; dy <= guess.y + AlignReach; dy++)
        {
            for (int dx = guess.x - AlignReach; dx <= guess.x + AlignReach; dx++)
            {
                int score = 0;
                for (int y = 0; y < img.H; y++)
                {
                    int ry = y + dy;
                    if (ry < 0 || ry >= reference.H)
                        continue;
                    for (int x = 0; x < img.W; x++)
                    {
                        int rx = x + dx;
                        if (rx < 0 || rx >= reference.W)
                            continue;
                        int s = (y * img.W + x) * 4, r = (ry * reference.W + rx) * 4;
                        if (img.Px[s + 3] <= 127 || reference.Px[r + 3] <= 127)
                            continue;
                        if (Math.Abs(img.Px[s] - reference.Px[r]) + Math.Abs(img.Px[s + 1] - reference.Px[r + 1])
                            + Math.Abs(img.Px[s + 2] - reference.Px[r + 2]) < 60)
                            score++;
                    }
                }
                if (score > best)
                {
                    best = score;
                    bestOff = (dx, dy);
                }
            }
        }
        return bestOff;
    }

    private static void SaveSheet(string id, string anim, byte[] px, int count)
    {
        if (px == null)
            return;
        var img = Image.CreateFromData(Cell * Math.Max(1, count), Cell * Dirs.Length, false, Image.Format.Rgba8, px);
        img.SavePng($"{Folder(id)}/{anim}.png");
    }
}
