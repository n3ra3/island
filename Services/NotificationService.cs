using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland;

public sealed record NotifItem(string App, string Title, string Body, ImageSource? Icon, Action? OnClick = null);

/// <summary>Слушает системные уведомления Windows и сообщает о новых.</summary>
public sealed class NotificationService
{
    UserNotificationListener? _l;
    readonly HashSet<uint> _seen = new();
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) }; // если всплывашки Windows скрыты, сигнала нет — тогда спасает опрос
    readonly DispatcherTimer _rehook = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _soon = new() { Interval = TimeSpan.FromMilliseconds(100) };
    // После сигнала проверяем серией: Windows вносит уведомление в список с небольшой задержкой
    static readonly int[] Retries = { 100, 150, 250, 300, 400 };
    int _retry;
    bool _checking, _again;

    // Мгновенный сигнал: системный процесс, который рисует уведомления Windows, показал окно —
    // значит, пришло уведомление. Слушаем только его, поэтому это почти ничего не стоит.
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    static readonly string[] ToastHosts = { "ShellExperienceHost", "ShellHost" };
    WinEventProc? _proc;
    readonly Dictionary<int, IntPtr> _hooks = new();

    public event Action<NotifItem>? Arrived;
    public string Status { get; private set; } = "не запущено";

    public async Task InitAsync()
    {
        try
        {
            _l = UserNotificationListener.Current;
            var st = await _l.RequestAccessAsync();
            Status = st.ToString();
            if (st != UserNotificationListenerAccessStatus.Allowed) { _l = null; Log(); return; }

            foreach (var n in await _l.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id);

            // Официальное событие у обычных программ обычно не работает — пробуем, но не рассчитываем на него
            try
            {
                _l.NotificationChanged += (_, _) => System.Windows.Application.Current.Dispatcher.InvokeAsync(Check);
                Status += ", событие";
            }
            catch { }

            _proc = OnWinEvent;
            Hook();
            if (File.Exists(Path.Combine(Settings.Dir, "debug.on")))
            {
                _spy = Spy;
                var hs = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _spy, 0, 0, 0x2);
                Status += $", spy={hs}, tid={Environment.CurrentManagedThreadId}, err={Marshal.GetLastWin32Error()}";
            }
            _rehook.Tick += (_, _) => Hook(); // процесс уведомлений мог перезапуститься
            _rehook.Start();
            _soon.Tick += (_, _) =>
            {
                Check();
                if (++_retry >= Retries.Length) { _soon.Stop(); return; }
                _soon.Interval = TimeSpan.FromMilliseconds(Retries[_retry]);
            };
            _poll.Tick += (_, _) => Check();
            _poll.Start();
            Status += $", слежу за окнами уведомлений ({string.Join(",", _hooks.Select(kv => kv.Key + ":" + kv.Value))})";
        }
        catch (Exception ex) { Status = "ошибка: " + ex.Message; _l = null; }
        Log();
    }

    void Hook()
    {
        var alive = new HashSet<int>();
        foreach (var name in ToastHosts)
            foreach (var p in Process.GetProcessesByName(name))
            {
                alive.Add(p.Id);
                if (!_hooks.ContainsKey(p.Id))
                    _hooks[p.Id] = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _proc!, (uint)p.Id, 0, 0x2); // EVENT_OBJECT_SHOW
                p.Dispose();
            }
        foreach (var dead in _hooks.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            UnhookWinEvent(_hooks[dead]);
            _hooks.Remove(dead);
        }
    }

    void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (obj != 0) return; // только окна целиком
        Check();               // сразу…
        _retry = 0;            // …и серией повторов в первые ~1,2 с
        _soon.Stop();
        _soon.Interval = TimeSpan.FromMilliseconds(Retries[0]);
        _soon.Start();
    }

    WinEventProc? _spy;
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
    void Spy(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (obj != 0 || hwnd == IntPtr.Zero) return;
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            var sb = new System.Text.StringBuilder(128); GetClassName(hwnd, sb, 128);
            using var pr = Process.GetProcessById((int)pid);
            File.AppendAllText(Path.Combine(Settings.Dir, "spy.log"), $"{DateTime.Now:HH:mm:ss.fff} {pr.ProcessName} {sb}" + Environment.NewLine);
        }
        catch { }
    }

    void Log()
    {
        try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(Path.Combine(Settings.Dir, "notifications.log"), $"{DateTime.Now:u} {Status}"); }
        catch { }
    }

    async void Check()
    {
        if (_l == null) return;
        if (_checking) { _again = true; return; }
        _checking = true;
        try
        {
            var list = await _l.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            foreach (var n in list)
            {
                ids.Add(n.Id);
                if (!_seen.Add(n.Id)) continue;
                if ((DateTimeOffset.Now - n.CreationTime).TotalSeconds > 30) continue; // старьё не показываем
                var item = await Read(n);
                if (item != null)
                {
                    if (File.Exists(Path.Combine(Settings.Dir, "debug.on")))
                        try { File.AppendAllText(Path.Combine(Settings.Dir, "notif-latency.log"), $"{DateTime.Now:HH:mm:ss.fff} показано: {item.Title}" + Environment.NewLine); } catch { }
                    Arrived?.Invoke(item);
                }
            }
            _seen.IntersectWith(ids);
        }
        catch { }
        _checking = false;
        if (_again) { _again = false; Check(); }
    }

    static async Task<NotifItem?> Read(UserNotification n)
    {
        try
        {
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
            if (texts.Count == 0) return null;
            string app = "";
            try { app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
            return new NotifItem(app, texts[0], string.Join(" ", texts.Skip(1)), await Logo(n));
        }
        catch { return null; }
    }

    static async Task<ImageSource?> Logo(UserNotification n)
    {
        try
        {
            var r = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var s = await r.OpenReadAsync();
            using var src = s.AsStreamForRead();
            var ms = new MemoryStream();
            await src.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
