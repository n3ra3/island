using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static DynamicIsland.Native;

namespace DynamicIsland;

public enum IslandState { Compact, Peek, Open, Notify, Osd, Mini }

public partial class MainWindow : Window
{
    const double BandHeight = 36;   // полоска сверху под островок, когда он не перекрывает окна
    const int HotkeyId = 1;

    IntPtr _hwnd;
    uint _appBarMsg;
    bool _appBar, _pinned, _hidden, _waveOn, _cleaned, _hudVisible;
    IslandState _state = IslandState.Compact;
    string _tab = "home";
    POINT _lastCursor;
    readonly ScaleTransform[] _bars = new ScaleTransform[4];
    readonly double[] _barLevel = new double[4];
    readonly DispatcherTimer _meterTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(42) };
    readonly DispatcherTimer _notifTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly Random _rnd = new();
    readonly HashSet<UIElement> _shown = new();

    // Анимация погоды в свёрнутом островке: 5 секунд движения примерно раз в 40 секунд
    readonly DispatcherTimer _wxTimer = new() { Interval = TimeSpan.FromSeconds(40) };
    readonly DispatcherTimer _wxStop = new() { Interval = TimeSpan.FromSeconds(5) };
    void WxBurst()
    {
        CompactWx.Resume();
        _wxStop.Stop();
        _wxStop.Start();
    }
    readonly DispatcherTimer _eyeTimer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    readonly DispatcherTimer _blinkTimer = new();
    readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(30) };

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(MainWindow), new PropertyMetadata(18.0, (d, _) => ((MainWindow)d).ApplyRadius()));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public IslandState State => _state;

    public MainWindow()
    {
        InitializeComponent();
        Hub.Main = this;
        Hub.Hud = ShowHud;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += (_, _) => { if (_state == IslandState.Open && !_pinned) SetState(IslandState.Compact); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetState(IslandState.Compact); };

        Island.SizeChanged += (_, _) => ApplyRadius();
        Hud.SizeChanged += (_, _) => { if (_hudVisible) UpdateRegion(); };
        Island.MouseEnter += (_, _) =>
        {
            _leaveTimer.Stop();
            // Мини-плеер — только если есть что показать
            if (_state is IslandState.Compact or IslandState.Osd or IslandState.Mini && Hub.Media.HasSession && Hub.Settings.HoverPeek) { _osdTimer.Stop(); SetState(IslandState.Peek); }
            else if (_state == IslandState.Mini) SetState(IslandState.Compact, force: true); // полоска раскрывается в обычный островок
            else if (_state == IslandState.Notify) _notifTimer.Stop(); // читаешь — не прячем
        };
        // Увёл мышь — сворачиваемся сразу. Но Windows шлёт ложный «уход» мыши, когда островок меняет форму,
        // поэтому сверяемся с настоящим положением курсора — иначе островок «скакал» туда-сюда.
        Island.MouseLeave += (_, _) =>
        {
            if (_state is IslandState.Peek || _state == IslandState.Compact && _mini)
            {
                if (CursorOverIsland()) _leaveTimer.Start();
                else SetState(IslandState.Compact);
            }
            else if (_state == IslandState.Notify) { _notifTimer.Interval = TimeSpan.FromSeconds(1.2); _notifTimer.Start(); }
        };
        Island.MouseLeftButtonUp += (_, e) =>
        {
            if (_state == IslandState.Open || IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (_state == IslandState.Notify)
            {
                // Клик по уведомлению: если у него есть действие (например, «обновить») — выполняем, иначе просто убираем
                _notifTimer.Stop();
                var act = _notifClick; _notifClick = null;
                SetState(IslandState.Compact);
                act?.Invoke();
                return;
            }
            OpenPanel();
        };
        Island.DragEnter += (_, _) => { if (_state != IslandState.Open || _tab != "shelf") OpenPanel("shelf"); };

        _leaveTimer.Tick += (_, _) =>
        {
            if (!(_state is IslandState.Peek || _state == IslandState.Compact && _mini)) { _leaveTimer.Stop(); return; }
            if (!CursorOverIsland()) { _leaveTimer.Stop(); SetState(IslandState.Compact); }
        };
        _eyeTimer.Tick += (_, _) => MoveEyes();
        _meterTimer.Tick += (_, _) => DrawBars();
        _wxTimer.Tick += (_, _) => WxBurst();
        _wxStop.Tick += (_, _) => { _wxStop.Stop(); CompactWx.Pause(); };
        _osdTimer.Tick += (_, _) => { _osdTimer.Stop(); if (_state == IslandState.Osd) SetState(IslandState.Compact); };
        _notifTimer.Tick += (_, _) => { _notifTimer.Stop(); if (_state == IslandState.Notify) SetState(Island.IsMouseOver ? IslandState.Peek : IslandState.Compact); };
        _blinkTimer.Tick += (_, _) => BlinkOnce();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _trimTimer.Tick += (_, _) => Trim();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _hudTimer.Tick += (_, _) => HideHud();
        _prebuild.Tick += (_, _) =>
        {
            _prebuild.Stop();
            if (_state != IslandState.Compact && PageHost.Content == null) SelectTab(_tab);
        };
        _weatherTimer.Tick += async (_, _) => await Hub.Weather.RefreshAsync();
    }

    // ---------------- Запуск ----------------

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Не показывать в Alt+Tab
        SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x80);
        var src = HwndSource.FromHwnd(_hwnd)!;
        src.AddHook(WndProc);
        // Прозрачный фон рисует DWM на видеокарте — без копирования кадров в память, анимации плавные
        src.CompositionTarget!.BackgroundColor = Colors.Transparent;
        var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref m);
        int noRound = 1, noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(_hwnd, 33, ref noRound, 4);   // без системных скруглений
        DwmSetWindowAttribute(_hwnd, 34, ref noBorder, 4);  // без рамки
        UpdateRegion();
        AddClipboardFormatListener(_hwnd);
        RegisterHotKey(_hwnd, HotkeyId, 0x8 | 0x4000, 0xC0); // Win + `
        _appBarMsg = RegisterWindowMessage("DynamicIslandAppBar");
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;

        BuildBars();
        InitEyes();
        // Маленькие слои всегда рисуются одинаково (готовой картинкой) — текст не прыгает на пиксель
        foreach (var layer in new FrameworkElement[] { Peek, Notif, Osd }) // свёрнутый — без картинки: в нём всё время что-то движется
            if (!Off("cache")) layer.CacheMode = new BitmapCache { RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX, SnapsToDevicePixels = true };
        ApplySettings();
        UpdateRegion();
        UpdateClock();
        // Часы: сброс кэша часового пояса при смене времени/пояса в Windows
        Microsoft.Win32.SystemEvents.TimeChanged += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            System.Globalization.CultureInfo.CurrentCulture.ClearCachedData();
            TimeZoneInfo.ClearCachedData();
            UpdateClock();
        });
        _clockTimer.Start();
        _fullscreenTimer.Start();
        _weatherTimer.Start();

        Hub.Countdown.Changed += UpdateClock;
        Hub.Weather.Changed += UpdateTemp;
        Hub.Media.Changed += OnMedia;
        OnMedia();

        Hub.Notifications.Arrived += ShowNotification;
        Hub.Audio.VolumeChanged += () => Dispatcher.InvokeAsync(OnVolumeOsd);
        _lastVolume = Hub.Audio.Volume;
        InputWatcher.LanguageChanged += lang => ShowOsd("i.keyboard", lang, null, 130);
        InputWatcher.CapsChanged += on => ShowOsd("i.caps", on ? "Caps Lock включён" : "Caps Lock выключен", null, 220);
        if (!Off("input")) InputWatcher.Start();
        Hub.Reminders.Due += r => ShowNotification(new NotifItem("Напоминание", r.Text, r.WhenText(), null));
        Hub.Reminders.Start();
        Hub.Updates.Found += u => ShowNotification(new NotifItem("Обновление Dynamic Island", $"Доступна версия {u.Version}",
            "Нажми, чтобы обновить — это займёт несколько секунд", null, () => _ = Hub.Updates.InstallAsync(u)));
        Hub.Updates.Start();
        if (!Off("media")) await Hub.Media.InitAsync();
        if (!Off("notif")) await Hub.Notifications.InitAsync();
        await Hub.Weather.RefreshAsync();
        Trim();
    }

    // ---------------- Ширина по содержимому ----------------

    double _compactW = 232;

    /// <summary>Курсор над островком? Считаем по итоговому размеру островка и реальным координатам мыши.</summary>
    bool CursorOverIsland()
    {
        if (!GetCursorPos(out var p)) return false;
        var dpi = VisualTreeHelper.GetDpi(this);
        var o = PointToScreen(new Point((Width - _target.Width) / 2, 0));
        return p.X >= o.X && p.X <= o.X + _target.Width * dpi.DpiScaleX &&
               p.Y >= o.Y - 2 && p.Y <= o.Y + _target.Height * dpi.DpiScaleY;
    }

    /// <summary>Свёрнутый островок ровно такой ширины, как его содержимое: нет музыки — он уже.</summary>
    void FitCompact()
    {
        var left = (FrameworkElement)Eyes.Parent;
        var right = (FrameworkElement)CompactClock.Parent;
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        left.Measure(inf);
        right.Measure(inf);
        double cw = Math.Max(130, Math.Ceiling(8 + left.DesiredSize.Width + 20 + right.DesiredSize.Width + 11));
        if (Math.Abs(cw - _compactW) < 1) return;
        _compactW = cw;
        if (_state != IslandState.Compact) return;

        double w = cw + 2 * Ear;
        _target = new Size(w, 36);
        var token = ++_animToken;
        bool grow = w > _region.Width;
        if (grow) SetRegion(new Size(w, 36));
        var a = new DoubleAnimation(w, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase { Damping = 13 } };
        a.Completed += (_, _) => { if (token == _animToken) SetRegion(_target); };
        Island.BeginAnimation(WidthProperty, a);
    }

    void FitCompactLater() => Dispatcher.BeginInvoke(FitCompact, DispatcherPriority.Loaded);

    public void ApplySettings()
    {
        var s = Hub.Settings;
        SetAppBar(false); // полосу сверху больше не резервируем
        CheckFullscreen();
        s.ApplyAutostart();
        Eyes.Visibility = s.Eyes ? Visibility.Visible : Visibility.Collapsed;
        UpdateEyes();
        FitCompactLater();
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312 && w.ToInt32() == HotkeyId)
        {
            if (_state == IslandState.Open) SetState(IslandState.Compact); else OpenPanel();
            handled = true;
        }
        else if (msg == 0x031D) Hub.Clip.OnClipboardUpdate();
        else if (_appBarMsg != 0 && msg == (int)_appBarMsg && w.ToInt32() == 1 && _appBar) PlaceAppBar();
        return IntPtr.Zero;
    }

    // ---------------- Резерв полоски сверху ----------------

    void SetAppBar(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
        if (on)
        {
            if (!_appBar)
            {
                d.uCallbackMessage = _appBarMsg;
                SHAppBarMessage(0, ref d); // ABM_NEW
                _appBar = true;
            }
            PlaceAppBar();
        }
        else if (_appBar)
        {
            SHAppBarMessage(1, ref d); // ABM_REMOVE
            _appBar = false;
        }
    }

    void PlaceAppBar()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        int h = (int)Math.Round(BandHeight * dpi);
        var d = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uEdge = 1,
            rc = new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = h },
        };
        SHAppBarMessage(2, ref d); // ABM_QUERYPOS
        d.rc.Bottom = d.rc.Top + h;
        SHAppBarMessage(3, ref d); // ABM_SETPOS
    }

    public void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;
        SetAppBar(false);
        if (_hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            UnregisterHotKey(_hwnd, HotkeyId);
        }
        Hub.Settings.Save();
    }

    protected override void OnClosed(EventArgs e) { Cleanup(); base.OnClosed(e); }

    // ---------------- Состояния ----------------

    public void OpenPanel(string? tab = null)
    {
        if (tab != null && (tab != _tab || PageHost.Content == null)) SelectTab(tab);
        SetState(IslandState.Open);
        Activate();
    }

    public void SetState(IslandState s, bool force = false)
    {
        if (_hidden && s != IslandState.Compact) return;
        // Над развёрнутым окном «свёрнутый» островок — это тонкая полоска (если мышь не над ним)
        if (s == IslandState.Compact && _mini && !force && !_hidden) s = IslandState.Mini;
        if (_state == s) return;
        if (Probe) LogState(s);
        _state = s;

        var (w, h, r) = s switch
        {
            IslandState.Compact => (_compactW, 36.0, 18.0),
            IslandState.Peek => (372.0, 80.0, 28.0),
            IslandState.Notify => (400.0, 76.0, 30.0),
            IslandState.Osd => (_osdWidth, 36.0, 18.0),
            IslandState.Mini => (110.0, 6.0, 3.0),
            _ => (720.0, 404.0, 34.0),
        };
        w += 2 * Ear; // «ушки» по бокам сверху
        // Раскрытие — живая пружина; сворачивание — мягкое, почти без отскока, чтобы не дёргалось
        var dur = TimeSpan.FromMilliseconds(s == IslandState.Compact ? 520 : 600);
        var ease = new SpringEase { Damping = s == IslandState.Compact ? 14 : 11 };
        // Форму окна меняем один раз: сразу расширяем до запаса под пружину, а точный размер ставим в конце.
        // Пересчёт формы на каждом кадре заставлял Windows перерисовывать окно — отсюда были рывки.
        // Смена формы — дорогая операция (Windows перерисовывает окно), поэтому делаем её,
        // пока ничего не движется: расширяем ДО старта анимации, сужаем ПОСЛЕ её конца.
        _target = new Size(w, h);
        var token = ++_animToken;
        var grow = new Size(Math.Max(_region.Width, w * 1.08 + 8), Math.Max(_region.Height, h * 1.08 + 8));
        bool needGrow = grow.Width > _region.Width + 0.5 || grow.Height > _region.Height + 0.5;

        void Go()
        {
            if (token != _animToken) return;
            var from = new Size(Island.ActualWidth, Island.ActualHeight);
            var to = new Size(w, h);
            var wa = new DoubleAnimation(w, dur) { EasingFunction = ease };
            wa.Completed += (_, _) => { if (token == _animToken) SetRegion(_target); };
            StartFrameProbe(s.ToString());
            Island.BeginAnimation(WidthProperty, wa);
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            BeginAnimation(RadiusProperty, new DoubleAnimation(r, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // Содержимое двигается вместе с островком: та же пружина, тот же центр сверху
            Fade(Compact, s == IslandState.Compact, from, to, dur, ease);
            Fade(Peek, s == IslandState.Peek, from, to, dur, ease);
            Fade(Full, s == IslandState.Open, from, to, dur, ease);
            Fade(Notif, s == IslandState.Notify, from, to, dur, ease);
            Fade(Osd, s == IslandState.Osd, from, to, dur, ease);
        }

        if (needGrow) { SetRegion(grow); AfterFrames(2, Go); }
        else Go();

        if (s == IslandState.Open)
        {
            _trimTimer.Stop();
            if (PageHost.Content == null) SelectTab(_tab);
            Dispatcher.BeginInvoke(MoveHighlight, DispatcherPriority.Loaded);
        }
        if (s == IslandState.Compact) _trimTimer.Start();
        if (s == IslandState.Peek)
        {
            _trimTimer.Stop();
            // Готовим вкладку заранее, пока курсор над островком, — к клику она уже собрана
            // но только после анимации, чтобы сборка не отнимала кадры у пружины
            if (PageHost.Content == null) { _prebuild.Stop(); _prebuild.Start(); }
        }

        UpdateEyes();
        UpdateWave();
    }

    /// <summary>
    /// Показ/скрытие слоя островка. Слой масштабируется от размера островка «до» к «после» с той же пружиной,
    /// поэтому, например, уведомление при сворачивании сжимается в пилюлю, а не просто гаснет.
    /// </summary>
    void Fade(FrameworkElement el, bool on, Size from, Size to, TimeSpan dur, IEasingFunction ease)
    {
        if (el.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1, 1);
            el.RenderTransform = st;
            el.RenderTransformOrigin = new Point(0.5, 0);
        }
        // Собственный размер слоя (у компактного ширина тянется — берём его обычную ширину)
        double lw = double.IsNaN(el.Width) ? 232 : el.Width;
        double lh = double.IsNaN(el.Height) ? 36 : el.Height;
        double ScaleFor(Size isl) => Math.Clamp(Math.Min(isl.Width / lw, isl.Height / lh), 0.3, 1);

        // Пока слой масштабируется, видеокарта двигает готовую картинку, а не перерисовывает текст каждый кадр
        // Слой рисуется готовой картинкой на видеокарте. У маленьких слоёв (островок, плеер, уведомление)
        // картинка остаётся и после анимации: переключение на обычную отрисовку сдвигало текст на пиксель.
        // Большую панель после анимации возвращаем к обычной отрисовке — там много живых элементов.
        if (el != Compact && el.CacheMode == null)
            el.CacheMode = new BitmapCache { RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX, SnapsToDevicePixels = true };
        void Crisp() { if (el == Full && el.CacheMode != null) el.CacheMode = null; }

        if (on)
        {
            _shown.Add(el);
            el.Visibility = Visibility.Visible;
            // Содержимое масштабируется БЕЗ пружины: иначе оно «перелетает» вместе с островком и ползёт вбок
            // Масштаб заканчивается раньше, чем содержимое проявится полностью, —
            // последний пиксель движения не виден как «шаг»
            double s0 = Math.Max(0.9, ScaleFor(from));
            var smooth = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var sdur = TimeSpan.FromMilliseconds(dur.TotalMilliseconds * 0.38);
            var sx = new DoubleAnimation(s0, 1, sdur) { EasingFunction = smooth };
            sx.Completed += (_, _) => { if (_shown.Contains(el)) Crisp(); };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s0, 1, sdur) { EasingFunction = smooth });
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(dur.TotalMilliseconds * 0.45))
            {
                BeginTime = TimeSpan.FromMilliseconds(dur.TotalMilliseconds * 0.12),
            });
        }
        else
        {
            if (!_shown.Remove(el) && el.Visibility != Visibility.Visible) return;
            // Уходящее содержимое сжимается ровно в такт островку (та же кривая), чтобы края его не обрезали
            double s1 = ScaleFor(to);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s1, dur) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s1, dur) { EasingFunction = ease });
            // Когда островок растёт, старое содержимое уходит быстро, чтобы не наслаивалось на новое
            bool growing = to.Width * to.Height > from.Width * from.Height;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(dur.TotalMilliseconds * (growing ? 0.18 : 0.25)));
            a.Completed += (_, _) =>
            {
                // Hidden, а не Collapsed: слой остаётся разложенным, и следующее появление не тормозит
                if (_shown.Contains(el)) return;
                el.Visibility = Visibility.Hidden;
                Crisp();
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = st.ScaleY = 1;
            };
            el.BeginAnimation(OpacityProperty, a);
        }
    }

    Size _region = new(252, 36), _target = new(252, 36);
    int _animToken;

    /// <summary>Выполнить действие через n отрисованных кадров.</summary>
    static void AfterFrames(int n, Action a)
    {
        EventHandler? h = null;
        h = (_, _) => { if (--n > 0) return; CompositionTarget.Rendering -= h; a(); };
        CompositionTarget.Rendering += h;
    }
    readonly DispatcherTimer _prebuild = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>Форма окна = прямоугольник островка нужного размера (+ подсказка). Всё вне неё кликается насквозь.</summary>
    /// <summary>
    /// Форма окна = островок нужного размера (+ подсказка). Всё вне неё кликается насквозь.
    /// Размер самого окна не меняем: изменение размера окна в Windows даёт мерцание и рывки.
    /// </summary>
    void SetRegion(Size size)
    {
        _region = size;
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        double left = (Width - size.Width) / 2;
        var rgn = _hidden ? CreateRectRgn(0, 0, 0, 0)
            : CreateRectRgn((int)Math.Floor(left * sx), 0, (int)Math.Ceiling((left + size.Width) * sx), (int)Math.Ceiling(size.Height * sy));
        if (_hudVisible && !_hidden && Hud.IsLoaded)
        {
            double hl = (Width - Hud.ActualWidth) / 2 - 4, ht = Hud.Margin.Top - 10;
            var hr = CreateRectRgn((int)Math.Floor(hl * sx), (int)Math.Floor(ht * sy),
                (int)Math.Ceiling((hl + Hud.ActualWidth + 8) * sx), (int)Math.Ceiling((ht + Hud.ActualHeight + 20) * sy));
            CombineRgn(rgn, rgn, hr, 2);
            DeleteObject(hr);
        }
        // true = перерисовать окно целиком: иначе на новых местах оставались куски старой картинки
        if (SetWindowRgn(_hwnd, rgn, true) == 0) DeleteObject(rgn);
    }

    void UpdateRegion() => SetRegion(_region);

    void LogState(IslandState s)
    {
        try
        {
            var st = new System.Diagnostics.StackTrace(2, false).GetFrames().Take(3).Select(f => f.GetMethod()?.Name);
            System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "states.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {_state} -> {s} via {string.Join(" < ", st)}" + Environment.NewLine);
        }
        catch { }
    }

    // Замер плавности: при DI_FPS=1 пишет самый долгий кадр каждой анимации в fps.log
    static readonly bool Probe = Environment.GetEnvironmentVariable("DI_FPS") == "1";
    void StartFrameProbe(string name)
    {
        if (!Probe) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = 0, worst = 0; int frames = 0;
        var slow = new List<string>();
        EventHandler? h = null;
        h = (_, _) =>
        {
            var now = sw.Elapsed.TotalMilliseconds;
            if (frames > 0) { worst = Math.Max(worst, now - last); if (now - last > 14) slow.Add($"#{frames}@{last:F0}:{now - last:F0}"); }
            else slow.Add($"first@{now:F0}");
            last = now; frames++;
            if (now > 650)
            {
                CompositionTarget.Rendering -= h;
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "fps.log"), $"{name}: кадров {frames} за {now:F0} мс, худший кадр {worst:F1} мс [{string.Join(" ", slow)}]" + Environment.NewLine); } catch { }
            }
        };
        CompositionTarget.Rendering += h;
    }

    // Отладка: DI_OFF=eq,wx,eyes,... выключает части, чтобы замерить их нагрузку
    static readonly string OffList = Environment.GetEnvironmentVariable("DI_OFF") ?? "";
    static bool Off(string part) => OffList.Split(',').Contains(part);

    const double Ear = 10; // радиус «ушек» у верхнего края

    /// <summary>Форма «чёлки»: прямой верх во всю ширину, плавные ушки, скругление только снизу.</summary>
    void ApplyRadius()
    {
        double W = Island.ActualWidth, H = Island.ActualHeight;
        if (W <= 2 * Ear || H <= 0) return;
        double x0 = Ear, x1 = W - Ear;
        double e = Math.Min(Ear, H * 0.7); // у тонкой полоски «ушки» поменьше
        double r = Math.Max(0, Math.Min(Radius, Math.Min((x1 - x0) / 2, H - e)));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(W, 0), false, false);
            c.QuadraticBezierTo(new Point(x1, 0), new Point(x1, e), false, true);
            c.LineTo(new Point(x1, H - r), false, true);
            c.ArcTo(new Point(x1 - r, H), new Size(r, r), 0, false, SweepDirection.Clockwise, false, true);
            c.LineTo(new Point(x0 + r, H), false, true);
            c.ArcTo(new Point(x0, H - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, true);
            c.LineTo(new Point(x0, e), false, true);
            c.QuadraticBezierTo(new Point(x0, 0), new Point(0, 0), false, true);
        }
        g.Freeze();
        Shape.Data = g;
        Island.Clip = g;
    }

    static bool IsInsideButton(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase || d is TextBox) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>Через 15 с после сворачивания выгружаем вкладку и отдаём память системе.</summary>
    void Trim()
    {
        _trimTimer.Stop();
        if (_state != IslandState.Compact) return;
        PageHost.Content = null;
        PerfMonitor.Release();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        EmptyWorkingSet((IntPtr)(-1));
    }

    // ---------------- Вкладки ----------------

    void Tab_Click(object sender, RoutedEventArgs e) => SelectTab((string)((Button)sender).Tag);
    void Settings_Click(object sender, RoutedEventArgs e) => SelectTab(_tab == "set" ? "home" : "set");
    void Close_Click(object sender, RoutedEventArgs e) => SetState(IslandState.Compact);

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        PinBtn.Foreground = _pinned ? Ui.B("Orange") : Ui.B("Label2");
        Hub.Toast(_pinned ? "Панель закреплена" : "Панель откреплена");
    }

    public void SelectTab(string key)
    {
        _tab = key;
        foreach (var b in TabsPanel.Children.OfType<Button>())
            b.Foreground = (string)b.Tag == key ? Ui.B("Label") : Ui.B("Label2");
        SettingsBtn.Foreground = key == "set" ? Ui.B("Label") : Ui.B("Label2");
        SettingsBtn.Background = key == "set" ? Ui.B("Fill3") : Ui.B("Fill1");

        UserControl page = key switch
        {
            "clip" => new ClipPage(),
            "note" => new NotesPage(),
            "wth" => new WeatherPage(),
            "ctl" => new SystemPage(),
            "shelf" => new ShelfPage(),
            "set" => new SettingsPage(),
            _ => new HomePage(),
        };
        page.Opacity = 0;
        PageHost.Content = page;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        Dispatcher.BeginInvoke(MoveHighlight, DispatcherPriority.Loaded);
    }

    void MoveHighlight()
    {
        var b = TabsPanel.Children.OfType<Button>().FirstOrDefault(x => (string)x.Tag == _tab);
        if (b == null) { TabHl.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))); return; }
        TabHl.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        var x = b.TranslatePoint(new Point(0, 0), TabsPanel).X;
        var dur = TimeSpan.FromMilliseconds(380);
        var ease = new SpringEase { Damping = 10 };
        if (TabHl.Width == 0 || double.IsNaN(TabHl.Width))
        {
            TabHl.Width = b.ActualWidth;
            TabHlT.X = x;
            return;
        }
        TabHlT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, dur) { EasingFunction = ease });
        TabHl.BeginAnimation(WidthProperty, new DoubleAnimation(b.ActualWidth, dur) { EasingFunction = ease });
    }

    // ---------------- Музыка ----------------

    async void Play_Click(object sender, RoutedEventArgs e) => await Hub.Media.Toggle();
    async void Next_Click(object sender, RoutedEventArgs e) => await Hub.Media.Next();
    async void Prev_Click(object sender, RoutedEventArgs e) => await Hub.Media.Prev();

    public static Brush ArtBrush(ImageSource? img)
    {
        if (img == null) return Ui.B("ArtPlaceholder");
        var b = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        b.Freeze();
        return b;
    }

    void OnMedia()
    {
        var m = Hub.Media;
        // Обложка и эквалайзер — только когда музыка реально играет
        CompactMedia.Visibility = m.Playing ? Visibility.Visible : Visibility.Collapsed;
        var art = ArtBrush(m.Art);
        CompactArt.Background = art;
        PeekArt.Background = art;
        PeekTitle.Text = m.HasSession ? m.Title : "Ничего не играет";
        PeekArtist.Text = m.HasSession ? m.Artist : "Включи музыку в любом плеере";
        PeekControls.IsEnabled = m.HasSession;
        PeekPlay.Data = Ui.G(m.Playing ? "f.pause" : "f.play");
        UpdateWave();
        FitCompactLater();
    }

    void BuildBars()
    {
        int i = 0;
        foreach (Rectangle r in Wave.Children)
        {
            r.RenderTransformOrigin = new Point(0.5, 0.5);
            _bars[i] = new ScaleTransform(1, 0.25);
            r.RenderTransform = _bars[i];
            i++;
        }
    }

    /// <summary>Эквалайзер слушает реальный звук, только когда играет музыка и островок свёрнут.</summary>
    void UpdateWave()
    {
        // Значок погоды анимируется, только когда его видно
        // Значок погоды в свёрнутом виде оживает короткими вспышками (см. _wxTimer), в остальное время стоит
        if (!(_state == IslandState.Compact && !_hidden)) { CompactWx.Pause(); _wxTimer.Stop(); }
        else if (!_wxTimer.IsEnabled) { _wxTimer.Start(); WxBurst(); }

        bool on = Hub.Media.Playing && _state == IslandState.Compact && !_hidden;
        if (on == _waveOn) return;
        _waveOn = on;
        if (on && !Off("eq")) { Hub.Meter.Start(); _meterTimer.Start(); }
        else
        {
            _meterTimer.Stop();
            Hub.Meter.Stop();
            for (int i = 0; i < 4; i++) { _barLevel[i] = 0.25; _bars[i].ScaleY = 0.25; }
            EyesBob.BeginAnimation(TranslateTransform.YProperty, null); EyesBob.Y = 0;
        }
    }

    void DrawBars()
    {
        var bands = Hub.Meter.Bands;
        for (int i = 0; i < 4; i++)
        {
            double target = 0.18 + 0.82 * bands[i];
            double cur = _barLevel[i];
            // Быстро вверх на удар, плавно вниз
            cur += (target - cur) * (target > cur ? 0.65 : 0.2);
            _barLevel[i] = cur;
            if (i == 0) BobToMusic(cur);
            // Перерисовываем, только если полоска заметно сдвинулась (меньше лишних кадров)
            if (Math.Abs(_bars[i].ScaleY - cur) > 0.03) _bars[i].ScaleY = cur;
        }
    }

    // ---------------- Всплывашки: громкость, язык, Caps Lock ----------------

    double _osdWidth = 260;
    float _lastVolume;
    readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };

    void OnVolumeOsd()
    {
        var v = Hub.Audio.Volume;
        if (Math.Abs(v - _lastVolume) < 0.004) return;
        _lastVolume = v;
        if (_state == IslandState.Open) return; // в панели громкость и так видна
        ShowOsd(v < 0.005 ? "i.volmute" : "i.volume", null, v, 260);
    }

    /// <summary>Короткая всплывашка в островке. value = 0..1 для полоски, text — для надписи.</summary>
    void ShowOsd(string icon, string? text, double? value, double width)
    {
        if (_hidden || _state is IslandState.Open or IslandState.Peek or IslandState.Notify) return;
        bool bar = value != null;
        OsdIcon.Data = Ui.G(icon);
        OsdCenterIcon.Data = Ui.G(icon);
        // Громкость — значок слева и полоска на всю ширину; текст — значок с надписью по центру
        OsdIcon.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        OsdBar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        OsdCenter.Visibility = bar ? Visibility.Collapsed : Visibility.Visible;
        OsdText.Text = text ?? "";
        OsdValue.Text = bar ? $"{Math.Round(value!.Value * 100)}" : "";
        if (bar)
            OsdFillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(Math.Clamp(value!.Value, 0, 1), TimeSpan.FromMilliseconds(120)));
        bool resize = _state != IslandState.Osd || Math.Abs(_osdWidth - width) > 1;
        _osdWidth = width;
        _osdTimer.Stop();
        _osdTimer.Start();
        if (resize) { if (_state == IslandState.Osd) _state = IslandState.Compact; SetState(IslandState.Osd); }
    }

    // ---------------- Уведомления ----------------

    Action? _notifClick;

    void ShowNotification(NotifItem n)
    {
        _notifClick = n.OnClick;
        SurpriseLater(); // глазки удивятся, когда островок свернётся обратно
        if (_hidden) return;
        if (_state == IslandState.Open) { ShowHud(string.IsNullOrEmpty(n.App) ? n.Title : $"{n.App}: {n.Title}", true); return; }
        NotifApp.Text = string.IsNullOrEmpty(n.App) ? "Уведомление" : n.App;
        NotifTitle.Text = n.Title;
        NotifBody.Text = n.Body;
        NotifBody.Visibility = string.IsNullOrEmpty(n.Body) ? Visibility.Collapsed : Visibility.Visible;
        if (n.Icon != null)
        {
            NotifIcon.Background = new ImageBrush(n.Icon) { Stretch = Stretch.UniformToFill };
            NotifBell.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotifIcon.Background = Ui.B("Fill2");
            NotifBell.Visibility = Visibility.Visible;
        }
        _notifTimer.Stop();
        _notifTimer.Interval = TimeSpan.FromSeconds(n.OnClick != null ? 10 : 5); // с действием — подольше, чтобы успеть нажать
        if (!Island.IsMouseOver) _notifTimer.Start();
        SetState(IslandState.Notify);
    }

    // ---------------- Часы, погода ----------------

    int _lastMinute = -1;

    void UpdateClock()
    {
        int len = CompactClock.Text.Length;
        SetClockText();
        if (CompactClock.Text.Length != len) FitCompactLater(); // поменялась длина (таймер, 9:59 → 10:00) — подгоняем ширину
    }

    void SetClockText()
    {
        var c = Hub.Countdown;
        if (c.Running)
        {
            CompactClock.Text = Hub.Fmt(c.Left);
            CompactClock.Foreground = Ui.B("Orange");
        }
        else
        {
            var now = DateTime.Now;
            if (now.Minute != _lastMinute) { TimeZoneInfo.ClearCachedData(); now = DateTime.Now; _lastMinute = now.Minute; }
            // Формат как в Windows (12 или 24 часа), без секунд
            CompactClock.Text = now.ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Replace("hh", "h").Replace("HH", "H"));
            CompactClock.Foreground = Ui.B("Label");
        }
    }

    void UpdateTemp()
    {
        var w = Hub.Weather;
        CompactTemp.Text = w.Temp is double t ? $"{Math.Round(t)}°" : "";
        CompactWx.Visibility = w.Temp == null ? Visibility.Collapsed : Visibility.Visible;
        if (w.Temp != null)
        {
            CompactWx.Set(w.Code, w.Night, animate: !Off("wx"));
            if (_state == IslandState.Compact && !_hidden) WxBurst(); else CompactWx.Pause();
        }
        FitCompactLater();
    }

    // ---------------- Полноэкранные приложения ----------------

    static readonly int OwnPid = Environment.ProcessId;
    static readonly string[] OverlayProcesses = { "ScreenClippingHost", "SnippingTool", "ScreenSketch", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost" };

    /// <summary>Полноэкранные окна, из-за которых прятаться не нужно: наша пипетка и инструмент скриншотов.</summary>
    static bool IsOverlayTool(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        if (pid == OwnPid) return true;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return OverlayProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    bool _mini;

    static bool OnPrimary(IntPtr w)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(MonitorFromWindow(w, 2), ref mi) && (mi.dwFlags & 1) != 0;
    }

    void CheckFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return;
        if (IsOverlayTool(fg)) return; // пипетка и «Ножницы» тоже на весь экран, но это не игра и не видео
        var sb = new StringBuilder(64);
        GetClassName(fg, sb, 64);
        var cls = sb.ToString();
        bool full = false;
        if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && GetWindowRect(fg, out var r))
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi) && (mi.dwFlags & 1) != 0)
                full = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
                       r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }
        // Окно развёрнуто на весь экран (но это не игра/видео) — островок сжимается в тонкую полоску
        bool max = !full && Hub.Settings.MiniOverMaximized && cls != "Progman" && cls != "WorkerW" && IsZoomed(fg) && OnPrimary(fg);
        if (max != _mini)
        {
            _mini = max;
            if (max && _state == IslandState.Compact && !CursorOverIsland()) SetState(IslandState.Mini);
            else if (!max && _state == IslandState.Mini) SetState(IslandState.Compact);
        }
        if (full == _hidden) return;
        _hidden = full;
        if (full) SetState(IslandState.Compact);
        Root.Visibility = full ? Visibility.Hidden : Visibility.Visible;
        UpdateRegion();
        UpdateEyes();
        UpdateWave();
    }

    // ---------------- Подтверждения ----------------

    void ShowHud(string text, bool ok)
    {
        HudText.Text = text;
        HudIcon.Data = Ui.G(ok ? "i.check" : "i.x");
        HudIcon.Foreground = Ui.B(ok ? "Green" : "Red");
        double islandH = _state switch { IslandState.Compact or IslandState.Osd => 36, IslandState.Peek => 80, IslandState.Notify => 76, _ => 404 };
        Hud.Margin = new Thickness(0, Island.Margin.Top + islandH + 8, 0, 0);
        Hud.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        HudT.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new SpringEase() });
        _hudVisible = true;
        Hud.UpdateLayout(); // сначала раскладываем новый текст, потом считаем форму — иначе подсказка обрезалась
        UpdateRegion();
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    void HideHud()
    {
        _hudTimer.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        a.Completed += (_, _) => { if (!_hudTimer.IsEnabled) { _hudVisible = false; UpdateRegion(); } };
        Hud.BeginAnimation(OpacityProperty, a);
    }
}
