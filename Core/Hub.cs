namespace DynamicIsland;

/// <summary>Общие сервисы приложения.</summary>
public static class Hub
{
    public static readonly Settings Settings = Settings.Load();
    public static readonly MediaService Media = new();
    public static readonly ClipboardHistory Clip = new();
    public static readonly WeatherService Weather = new();
    public static readonly CountdownService Countdown = new();
    public static List<string> Shelf => Settings.Shelf;
    public static readonly ReminderService Reminders = new();
    public static readonly LyricsService Lyrics = new();
    public static readonly UpdateService Updates = new();
    public static readonly AudioMeter Meter = new();
    public static readonly NotificationService Notifications = new();

    static AudioService? _audio;
    public static AudioService Audio => _audio ??= new AudioService();

    public static MainWindow? Main;
    public static Action<string, bool> Hud = (_, _) => { };
    public static void Toast(string text, bool ok = true) => Hud(text, ok);

    public static string Fmt(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var s = (int)Math.Ceiling(t.TotalSeconds - 0.001);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}
