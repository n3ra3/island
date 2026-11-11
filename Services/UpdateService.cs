using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace DynamicIsland;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string Notes);

/// <summary>
/// Проверяет новые версии на GitHub Releases и ставит их тихо.
/// Выпуск новой версии: installer\release.ps1 — он кладёт установщик в Releases.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Репозиторий с версиями: «владелец/название». Меняется в одном месте.</summary>
    public const string Repo = "n3ra3/island";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly DispatcherTimer _daily = new() { Interval = TimeSpan.FromHours(6) };

    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);

    public UpdateInfo? Available { get; private set; }
    public bool Busy { get; private set; }
    public DateTime LastCheck { get; private set; }
    public event Action<UpdateInfo>? Found;
    public event Action? Changed;

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland-Updater");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static bool Configured => !Repo.StartsWith("OWNER/");

    public void Start()
    {
        if (!Configured) return;
        _daily.Tick += async (_, _) => await CheckAsync(silent: true);
        _daily.Start();
        // Первая проверка — через минуту после запуска, чтобы не мешать старту
        var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(silent: true); };
        first.Start();
    }

    /// <summary>Спрашивает GitHub о последней версии. silent — не показывать «у тебя последняя версия».</summary>
    public async Task<UpdateInfo?> CheckAsync(bool silent)
    {
        if (!Configured || Busy) return null;
        if (silent && !Hub.Settings.AutoUpdate) return null;
        Busy = true; Changed?.Invoke();
        UpdateInfo? found = null;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (Version.TryParse(tag.TrimStart('v', 'V'), out var v) && v > Current)
            {
                string? url = null;
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        url = a.GetProperty("browser_download_url").GetString();
                }
                if (url != null)
                    found = new UpdateInfo(v, tag, url, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // В репозитории ещё нет ни одной версии — значит, обновлять нечего
        }
        catch
        {
            if (!silent) Hub.Toast("Не получилось проверить обновления. Проверь интернет", false);
        }
        LastCheck = DateTime.Now;
        Busy = false;
        Available = found;
        Changed?.Invoke();
        if (found != null) Found?.Invoke(found);
        else if (!silent) Hub.Toast($"У тебя последняя версия ({Current})");
        return found;
    }

    /// <summary>Скачивает установщик новой версии и запускает его тихо. Островок закроется и откроется уже новым.</summary>
    public async Task InstallAsync(UpdateInfo u)
    {
        if (Busy) return;
        Busy = true; Changed?.Invoke();
        try
        {
            Hub.Toast($"Скачиваю версию {u.Version}…");
            var dir = Path.Combine(Path.GetTempPath(), "DynamicIsland-update");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"DynamicIsland-Setup-{u.Version}.exe");
            using (var resp = await Http.GetAsync(u.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(file);
                await resp.Content.CopyToAsync(fs);
            }
            // /UPDATE=1 — установщик сам запустит островок после обновления
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
            ((App)System.Windows.Application.Current).Quit();
        }
        catch
        {
            Busy = false; Changed?.Invoke();
            Hub.Toast("Не получилось скачать обновление", false);
        }
    }
}
