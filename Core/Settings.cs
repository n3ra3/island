using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland;

public sealed class NoteDoc
{
    public string Xaml { get; set; } = "";
    public string Preview { get; set; } = "";
}

public sealed class Settings
{
    public bool Eyes { get; set; } = true;
    public bool ReserveTop { get; set; } // старая настройка, больше не используется
    public bool OverlapWindows { get; set; } = true; // старая настройка, больше не используется
    public bool MiniOverMaximized { get; set; } = true; // над развёрнутыми окнами — тонкая полоска
    public bool AutoUpdate { get; set; } = true;     // проверять новые версии на GitHub
    public bool HoverPeek { get; set; } = true;      // раскрывать мини-плеер при наведении
    public bool Autostart { get; set; }
    public string City { get; set; } = "Шымкент";
    public double Lat { get; set; } = 42.3;
    public double Lon { get; set; } = 69.6;
    public string Notes { get; set; } = "";          // старая одиночная заметка (переносится в NotesList)
    public List<NoteDoc> NotesList { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public List<string> Shelf { get; set; } = new();

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void ApplyAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (Autostart && Environment.ProcessPath is { } p) k.SetValue("DynamicIsland", $"\"{p}\"");
            else k.DeleteValue("DynamicIsland", false);
        }
        catch { }
    }
}
