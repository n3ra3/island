using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicIsland;

/// <summary>«Сейчас играет» через системный медиа-API Windows.</summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _s;
    readonly Dispatcher _d = Dispatcher.CurrentDispatcher;

    public bool HasSession { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Source { get; private set; } = "";
    public ImageSource? Art { get; private set; }
    public bool Playing { get; private set; }
    public TimeSpan Duration { get; private set; }
    TimeSpan _pos;
    DateTimeOffset _posStamp;

    public event Action? Changed;

    public TimeSpan Position
    {
        get
        {
            var p = Playing ? _pos + (DateTimeOffset.Now - _posStamp) : _pos;
            if (p < TimeSpan.Zero) p = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p;
        }
    }

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => _d.InvokeAsync(Attach);
            Attach();
        }
        catch { }
    }

    void Attach()
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnPlayback;
            _s.TimelinePropertiesChanged -= OnTimeline;
        }
        _s = _mgr?.GetCurrentSession();
        HasSession = _s != null;
        if (_s != null)
        {
            _s.MediaPropertiesChanged += OnProps;
            _s.PlaybackInfoChanged += OnPlayback;
            _s.TimelinePropertiesChanged += OnTimeline;
            Source = Friendly(_s.SourceAppUserModelId);
            ReadPlayback();
            ReadTimeline();
            _ = LoadPropsAsync();
        }
        else
        {
            Title = Artist = Source = "";
            Art = null; Playing = false; Duration = TimeSpan.Zero;
        }
        Changed?.Invoke();
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) =>
        _d.InvokeAsync(async () => await LoadPropsAsync());
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) =>
        _d.InvokeAsync(() => { ReadPlayback(); Changed?.Invoke(); });
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) =>
        _d.InvokeAsync(() => { ReadTimeline(); Changed?.Invoke(); });

    void ReadPlayback()
    {
        try
        {
            var was = Playing;
            Playing = _s?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (was != Playing) { _pos = Position; _posStamp = DateTimeOffset.Now; }
        }
        catch { }
    }

    void ReadTimeline()
    {
        try
        {
            if (_s == null) return;
            var t = _s.GetTimelineProperties();
            Duration = t.EndTime - t.StartTime;
            _pos = t.Position;
            _posStamp = t.LastUpdatedTime;
            if (_posStamp > DateTimeOffset.Now || _posStamp.Year < 2000) _posStamp = DateTimeOffset.Now;
        }
        catch { }
    }

    async Task LoadPropsAsync()
    {
        if (_s == null) return;
        try
        {
            var p = await _s.TryGetMediaPropertiesAsync();
            Title = string.IsNullOrWhiteSpace(p.Title) ? "Без названия" : p.Title;
            Artist = p.Artist ?? "";
            Art = null;
            if (p.Thumbnail != null)
            {
                using var ras = await p.Thumbnail.OpenReadAsync();
                using var src = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await src.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160; // маленькая копия — экономим память
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                Art = bmp;
            }
        }
        catch { }
        Changed?.Invoke();
    }

    static string Friendly(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var s = id.ToLowerInvariant();
        if (s.Contains("spotify")) return "Spotify";
        if (s.Contains("chrome")) return "Google Chrome";
        if (s.Contains("edge")) return "Microsoft Edge";
        if (s.Contains("yandex")) return "Яндекс";
        if (s.Contains("firefox") || s.Contains("308046b0af4a39cb")) return "Firefox";
        if (s.Contains("opera")) return "Opera";
        if (s.Contains("telegram")) return "Telegram";
        if (s.Contains("zunemusic") || s.Contains("media")) return "Медиаплеер";
        var name = id.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    public async Task Toggle() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
    public async Task Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
    public async Task Prev() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }

    public async Task Seek(TimeSpan to)
    {
        try
        {
            if (_s == null) return;
            _pos = to; _posStamp = DateTimeOffset.Now;
            await _s.TryChangePlaybackPositionAsync(to.Ticks);
        }
        catch { }
    }
}
