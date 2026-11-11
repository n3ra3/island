using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>
/// Синхронизированный текст песни с открытого сервиса lrclib.net (бесплатно, без ключа).
/// Туда уходит только название трека, исполнитель и длительность.
/// </summary>
public sealed class LyricsService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    readonly Dictionary<string, List<LyricLine>?> _cache = new();

    static LyricsService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0 (desktop widget)");
    }

    public async Task<List<LyricLine>?> GetAsync(string artist, string title, TimeSpan duration)
    {
        artist = CleanArtist(artist);
        var key = $"{artist}|{title}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        List<LyricLine>? result = null;
        try
        {
            var plain = CleanTitle(title);
            result = await Search($"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}", duration)
                  ?? (plain != title ? await Search($"track_name={Uri.EscapeDataString(plain)}&artist_name={Uri.EscapeDataString(artist)}", duration) : null)
                  ?? await Search($"q={Uri.EscapeDataString(artist + " " + plain)}", duration);
        }
        catch { }
        _cache[key] = result;
        return result;
    }

    async Task<List<LyricLine>?> Search(string query, TimeSpan duration)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://lrclib.net/api/search?" + query));
        JsonElement? best = null;
        double bestDiff = double.MaxValue;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!e.TryGetProperty("syncedLyrics", out var sl) || sl.ValueKind != JsonValueKind.String) continue;
            double d = e.TryGetProperty("duration", out var du) && du.ValueKind == JsonValueKind.Number ? du.GetDouble() : 0;
            double diff = duration > TimeSpan.Zero && d > 0 ? Math.Abs(d - duration.TotalSeconds) : 0;
            if (diff < bestDiff) { bestDiff = diff; best = e.Clone(); }
        }
        if (best is not JsonElement b) return null;

        var lines = Parse(b.GetProperty("syncedLyrics").GetString()!);
        // Ускоренная или замедленная версия (slowed / sped up) — растягиваем тайминги под длину трека
        double lrcDur = b.TryGetProperty("duration", out var dd) && dd.ValueKind == JsonValueKind.Number ? dd.GetDouble() : 0;
        if (duration > TimeSpan.Zero && lrcDur > 0)
        {
            double k = duration.TotalSeconds / lrcDur;
            if (Math.Abs(k - 1) > 0.04 && k is > 0.6 and < 1.6)
                lines = lines.Select(l => l with { Time = TimeSpan.FromSeconds(l.Time.TotalSeconds * k) }).ToList();
        }
        return lines.Count > 0 ? lines : null;
    }

    static List<LyricLine> Parse(string lrc)
    {
        var list = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = Regex.Match(raw.Trim(), @"^\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$");
            if (!m.Success) continue;
            var t = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) +
                    TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            list.Add(new LyricLine(t, m.Groups[3].Value.Trim()));
        }
        return list;
    }

    static string CleanArtist(string a) =>
        Regex.Replace(a, @"\s*-\s*Topic$|VEVO$", "", RegexOptions.IgnoreCase).Split(',')[0].Trim();

    static string CleanTitle(string t) =>
        Regex.Replace(t, @"\s*[\(\[][^\)\]]*[\)\]]", "").Replace("Official Video", "", StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>Индекс строки, которая звучит сейчас (или -1 до первой).</summary>
    public static int IndexAt(List<LyricLine> lines, TimeSpan pos)
    {
        int lo = 0, hi = lines.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= pos) { ans = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return ans;
    }
}
