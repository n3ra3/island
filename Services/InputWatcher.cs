using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Следит за раскладкой клавиатуры и Caps Lock, чтобы показать короткую всплывашку.</summary>
public static class InputWatcher
{
    public static event Action<string>? LanguageChanged;
    public static event Action<bool>? CapsChanged;

    static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    static ushort _lang;
    static IntPtr _fg, _hook;
    static LowLevelKeyboardProc? _proc; // держим ссылку, чтобы сборщик мусора не удалил
    static bool _caps;

    public static void Start()
    {
        _fg = GetForegroundWindow();
        _lang = LangOf(_fg);
        _caps = (GetKeyState(0x14) & 1) != 0;
        Timer.Tick += (_, _) => Poll();
        Timer.Start();
        // Перехват клавиатуры смотрит только на Caps Lock, остальные клавиши сразу пропускает дальше
        _proc = Hook;
        _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
    }

    public static void Stop()
    {
        Timer.Stop();
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    static ushort LangOf(IntPtr wnd)
    {
        var tid = GetWindowThreadProcessId(wnd, out _);
        return (ushort)((long)GetKeyboardLayout(tid) & 0xFFFF);
    }

    static void Poll()
    {
        var fg = GetForegroundWindow();
        var lang = LangOf(fg);
        if (lang == 0) return;
        // Переключился на другое окно — у него может быть своя раскладка, это не «смена языка»
        if (fg != _fg) { _fg = fg; _lang = lang; return; }
        if (lang == _lang) return;
        _lang = lang;
        LanguageChanged?.Invoke(Name(lang));
    }

    static string Name(ushort lang)
    {
        try { return CultureInfo.GetCultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return lang.ToString("X4"); }
    }

    static IntPtr Hook(int nCode, IntPtr w, IntPtr l)
    {
        if (nCode >= 0 && w == (IntPtr)0x0101 && Marshal.ReadInt32(l) == 0x14) // отпускание Caps Lock
        {
            _caps = !_caps;
            var on = _caps;
            Application.Current?.Dispatcher.BeginInvoke(() => CapsChanged?.Invoke(on));
        }
        return CallNextHookEx(_hook, nCode, w, l);
    }
}
