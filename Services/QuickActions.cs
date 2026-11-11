using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Devices.Radios;
using WinImaging = Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Быстрые действия и системные переключатели.</summary>
public static class QuickActions
{
    static DateTime _ocrUntil;

    static QuickActions()
    {
        Hub.Clip.ImageCopied += img =>
        {
            if (DateTime.Now > _ocrUntil) return;
            _ocrUntil = DateTime.MinValue;
            _ = RecognizeAsync(img);
        };
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { Hub.Toast("Не получилось открыть", false); }
    }

    // ---------- Скриншоты ----------

    public static void SnipArea() => Open("ms-screenclip:");

    public static void TextFromScreen()
    {
        _ocrUntil = DateTime.Now.AddMinutes(1);
        Open("ms-screenclip:");
    }

    static async Task RecognizeAsync(BitmapSource img)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Hub.Toast("Распознавание текста недоступно в этой Windows", false); return; }

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            var dec = await WinImaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using var sb = await dec.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied);
            var res = await engine.RecognizeAsync(sb);
            var text = string.Join(Environment.NewLine, res.Lines.Select(l => l.Text)).Trim();
            if (text.Length == 0) { Hub.Toast("Текст не найден", false); return; }
            Clipboard.SetText(text);
            Hub.Toast("Текст скопирован");
        }
        catch { Hub.Toast("Не получилось распознать текст", false); }
    }

    public static void Eyedropper() => new EyedropperWindow().Show();

    // ---------- Текст ----------

    const string EnL = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`";
    const string RuL = "йцукенгшщзхъфывапролджэячсмитьбю.ё";
    const string EnU = "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?~";
    const string RuU = "ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,Ё";

    public static void FixLayout()
    {
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { text = ""; }
        if (text.Length == 0) { Hub.Toast("Сначала скопируй текст", false); return; }

        int cyr = text.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        int lat = text.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        bool toRu = lat >= cyr;
        string from = toRu ? EnL + EnU : RuL + RuU;
        string to = toRu ? RuL + RuU : EnL + EnU;

        var chars = text.Select(c => { var i = from.IndexOf(c); return i >= 0 ? to[i] : c; }).ToArray();
        var fixedText = new string(chars);
        try { Clipboard.SetText(fixedText); } catch { }
        var preview = fixedText.Length > 28 ? fixedText[..28] + "…" : fixedText;
        Hub.Toast($"Скопировано: {preview}");
    }

    // ---------- Звук ----------

    public static void NextOutput()
    {
        if (Hub.Audio.OutputCount < 2) { Hub.Toast("Подключено только одно устройство вывода", false); return; }
        var name = Hub.Audio.NextOutput();
        if (name == null) Hub.Toast("Не получилось переключить звук", false);
        else { Hub.Meter.Restart(); Hub.Toast($"Звук: {name}"); }
    }

    public static void ToggleMic()
    {
        if (!Hub.Audio.HasMic) { Hub.Toast("Микрофон не найден", false); return; }
        var muted = !Hub.Audio.MicMuted;
        Hub.Audio.MicMuted = muted;
        Hub.Toast(muted ? "Микрофон выключен" : "Микрофон включён");
    }

    // ---------- Уведомления ----------

    const string NotifKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    public static bool DndOn
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(NotifKey);
                return k?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int v && v == 0;
            }
            catch { return false; }
        }
    }

    public static void ToggleDnd()
    {
        try
        {
            var on = !DndOn;
            using var k = Registry.CurrentUser.CreateSubKey(NotifKey);
            k.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", on ? 0 : 1, RegistryValueKind.DWord);
            Hub.Toast(on ? "Не беспокоить включено" : "Уведомления включены");
        }
        catch { Hub.Toast("Не получилось изменить уведомления", false); }
    }

    public static void OpenDownloads() => Open(DownloadsFolder());

    // ---------- Система ----------

    const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool DarkTheme
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(ThemeKey);
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return true; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(ThemeKey);
                k.SetValue("AppsUseLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
                k.SetValue("SystemUsesLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
                SendMessageTimeout((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
            }
            catch { }
        }
    }

    public static async Task<Radio?> GetRadio(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            var all = await Radio.GetRadiosAsync();
            return all.FirstOrDefault(r => r.Kind == kind);
        }
        catch { return null; }
    }

    public static void Lock() => LockWorkStation();
    public static void Sleep() => SetSuspendState(false, false, false);
    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
    public static void Shutdown() => Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
}
