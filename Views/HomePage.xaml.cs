using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class HomePage : UserControl
{
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<Action> _repaint = new();
    bool _scrubbing;
    readonly DispatcherTimer _lyTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    List<LyricLine>? _lyrics;
    string _lyKey = "";
    int _lyIndex = -2;

    public HomePage()
    {
        InitializeComponent();
        BuildCommands();

        Loaded += (_, _) =>
        {
            Hub.Media.Changed += RenderMedia;
            Hub.Audio.VolumeChanged += OnVolumeExternal;
            _tick.Tick += (_, _) => RenderProgress();
            _tick.Start();
            RenderMedia();
            RenderVolume();
            _lyTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            Hub.Media.Changed -= RenderMedia;
            Hub.Audio.VolumeChanged -= OnVolumeExternal;
            _tick.Stop();
            _lyTimer.Stop();
        };
        _lyTimer.Tick += (_, _) => RenderLyrics();

        // Перемотка
        Scrub.MouseLeftButtonDown += (_, e) => { _scrubbing = true; Scrub.CaptureMouse(); ScrubTo(e); };
        Scrub.MouseMove += (_, e) => { if (_scrubbing) ScrubTo(e); };
        Scrub.MouseLeftButtonUp += async (_, e) =>
        {
            if (!_scrubbing) return;
            _scrubbing = false;
            Scrub.ReleaseMouseCapture();
            var f = Math.Clamp(e.GetPosition(Scrub).X / Scrub.ActualWidth, 0, 1);
            await Hub.Media.Seek(TimeSpan.FromTicks((long)(Hub.Media.Duration.Ticks * f)));
        };

        // Громкость
        Vol.MouseLeftButtonDown += (_, e) => { Vol.CaptureMouse(); SetVolumeFrom(e); };
        Vol.MouseMove += (_, e) => { if (Vol.IsMouseCaptured) SetVolumeFrom(e); };
        Vol.MouseLeftButtonUp += (_, _) => Vol.ReleaseMouseCapture();
        Vol.MouseWheel += (_, e) =>
        {
            Hub.Audio.Volume += e.Delta > 0 ? 0.02f : -0.02f;
            RenderVolume();
            e.Handled = true;
        };
        Vol.SizeChanged += (_, _) => RenderVolume();
        // Маска растворения — по высоте окошка с текстом, а не всего текста песни
        Lyrics.SizeChanged += (_, e) =>
        {
            LyMask.EndPoint = new Point(0, e.NewSize.Height);
            LyLines.Width = e.NewSize.Width; // строки переносятся по ширине окошка
            _lyIndex = -2;                   // пересчитать прокрутку
        };
    }

    // ---------- Музыка ----------

    void RenderMedia()
    {
        var m = Hub.Media;
        Art.Background = MainWindow.ArtBrush(m.Art);
        TitleText.Text = m.HasSession ? m.Title : "Ничего не играет";
        ArtistText.Text = m.HasSession ? m.Artist : "Включи музыку в любом плеере";
        SrcText.Text = m.Source;
        SrcDot.Visibility = m.HasSession ? Visibility.Visible : Visibility.Collapsed;
        PlayIcon.Data = Ui.G(m.Playing ? "f.pause" : "f.play");
        Transport.IsEnabled = m.HasSession;
        RenderProgress();
        _ = LoadLyricsAsync();
    }

    void RenderProgress()
    {
        var m = Hub.Media;
        bool has = m.HasSession && m.Duration > TimeSpan.Zero;
        Scrub.Visibility = Times.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has || _scrubbing) return;
        var pos = m.Position;
        ScrubFill.Width = ScrubTrack.ActualWidth * pos.TotalSeconds / m.Duration.TotalSeconds;
        CurText.Text = Hub.Fmt(pos);
        RemText.Text = "−" + Hub.Fmt(m.Duration - pos);
    }

    // ---------- Текст песни ----------

    async Task LoadLyricsAsync()
    {
        var m = Hub.Media;
        var key = m.HasSession ? $"{m.Artist}|{m.Title}" : "";
        if (key == _lyKey) return;
        _lyKey = key;
        _lyrics = null;
        _lyIndex = -2;
        if (!m.HasSession) { ShowLyricsEmpty("Включи музыку — здесь появится текст"); return; }
        ShowLyricsEmpty("Ищу текст песни…");
        // Длительность у браузеров приходит чуть позже названия — даём ей секунду
        if (m.Duration <= TimeSpan.Zero) await Task.Delay(1000);
        var lines = await Hub.Lyrics.GetAsync(m.Artist, m.Title, Hub.Media.Duration);
        if (key != _lyKey) return; // трек уже сменился
        _lyrics = lines;
        BuildLyrics();
        if (lines == null) ShowLyricsEmpty("Текст этой песни не найден");
        else RenderLyrics();
    }

    void ShowLyricsEmpty(string text)
    {
        Lyrics.Visibility = Visibility.Collapsed;
        LySource.Visibility = Visibility.Collapsed;
        LyEmpty.Visibility = Visibility.Visible;
        LyEmptyText.Text = text;
    }

    readonly List<TextBlock> _lyBlocks = new();

    /// <summary>Строит столбик из всех строк песни. Клик по строке — перемотка на неё.</summary>
    void BuildLyrics()
    {
        LyLines.Children.Clear();
        _lyBlocks.Clear();
        if (_lyrics == null) return;
        foreach (var line in _lyrics)
        {
            var tb = new TextBlock
            {
                Text = line.Text.Length == 0 ? "♪" : line.Text,
                FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 4, 12), Opacity = 0.32, Cursor = Cursors.Hand,
                ToolTip = "Перемотать сюда",
            };
            var t = line.Time;
            tb.MouseLeftButtonUp += async (_, e) => { e.Handled = true; await Hub.Media.Seek(t); _lyIndex = -2; };
            tb.MouseEnter += (_, _) => { if (_lyBlocks.IndexOf(tb) != _lyIndex) tb.Opacity = 0.6; };
            tb.MouseLeave += (_, _) => { if (_lyBlocks.IndexOf(tb) != _lyIndex) tb.Opacity = _lyBlocks.IndexOf(tb) < _lyIndex ? 0.22 : 0.32; };
            _lyBlocks.Add(tb);
            LyLines.Children.Add(tb);
        }
        LyScroll.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        LyScroll.Y = 0;
    }

    void RenderLyrics()
    {
        try { RenderLyricsCore(); }
        catch (Exception ex)
        {
            _lyIndex = -2; // попробуем снова на следующем тике
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:u} lyrics {ex}" + Environment.NewLine); } catch { }
        }
    }

    void RenderLyricsCore()
    {
        if (_lyrics == null) return;
        if (_lyBlocks.Count != _lyrics.Count) BuildLyrics();
        Lyrics.Visibility = Visibility.Visible;
        LySource.Visibility = Visibility.Visible;
        LyEmpty.Visibility = Visibility.Collapsed;

        // Чуть опережаем, чтобы строка загоралась вместе с голосом
        int i = LyricsService.IndexAt(_lyrics, Hub.Media.Position + TimeSpan.FromMilliseconds(200));
        if (i == _lyIndex) return;
        _lyIndex = i;

        var fade = TimeSpan.FromMilliseconds(320);
        for (int k = 0; k < _lyBlocks.Count; k++)
        {
            // Текущая — ярко-белая, прошедшие — бледнее будущих, как в Spotify
            double target = k == i ? 1 : k < i ? 0.22 : 0.32;
            if (Math.Abs(_lyBlocks[k].Opacity - target) > 0.01 || k == i)
                _lyBlocks[k].BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(target, fade));
        }

        // Плавная прокрутка: текущая строка встаёт примерно на треть высоты
        Lyrics.UpdateLayout();
        double anchor = Lyrics.ActualHeight * 0.3;
        double lineTop = i >= 0 ? _lyBlocks[i].TranslatePoint(new Point(0, 0), LyLines).Y : 0;
        double y = anchor - lineTop;
        if (Environment.GetEnvironmentVariable("DI_FPS") == "1")
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "lyrics.log"),
                $"i={i} n={_lyBlocks.Count} boxH={Lyrics.ActualHeight:F0} boxW={Lyrics.ActualWidth:F0} linesH={LyLines.ActualHeight:F0} lineTop={lineTop:F0} y={y:F0} vis={Lyrics.IsVisible} op0={(_lyBlocks.Count > 0 ? _lyBlocks[0].Opacity : -1):F2} txt0={(_lyBlocks.Count > 0 ? _lyBlocks[0].Text : "")}" + Environment.NewLine); } catch { }
        LyScroll.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(y, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new System.Windows.Media.Animation.QuinticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
            });
    }

    void ScrubTo(MouseEventArgs e)
    {
        var f = Math.Clamp(e.GetPosition(Scrub).X / Scrub.ActualWidth, 0, 1);
        ScrubFill.Width = ScrubTrack.ActualWidth * f;
        CurText.Text = Hub.Fmt(TimeSpan.FromTicks((long)(Hub.Media.Duration.Ticks * f)));
    }

    async void Play_Click(object sender, RoutedEventArgs e) => await Hub.Media.Toggle();
    async void Next_Click(object sender, RoutedEventArgs e) => await Hub.Media.Next();
    async void Prev_Click(object sender, RoutedEventArgs e) => await Hub.Media.Prev();

    // ---------- Громкость ----------

    void OnVolumeExternal() => Dispatcher.InvokeAsync(RenderVolume);

    void SetVolumeFrom(MouseEventArgs e)
    {
        Hub.Audio.Volume = (float)Math.Clamp(e.GetPosition(Vol).X / Vol.ActualWidth, 0, 1);
        RenderVolume();
    }

    void RenderVolume()
    {
        var v = Hub.Audio.Volume;
        VolFill.Width = Math.Max(28, Vol.ActualWidth * v);
        VolText.Text = $"{Math.Round(v * 100)}%";
        VolIcon.Data = Ui.G(v < 0.005 ? "i.volmute" : "i.volume");
    }

    // ---------- Быстрые действия ----------

    void BuildCommands()
    {
        Add("Снимок области", "i.crop", QuickActions.SnipArea);
        Add("Текст с экрана", "i.text", QuickActions.TextFromScreen);
        Add("Пипетка", "i.drop", QuickActions.Eyedropper);
        Add("Исправить раскладку", "i.lang", QuickActions.FixLayout);
        Add("Аудиовыход", "i.phones", QuickActions.NextOutput);
        Add("Микрофон", "i.mic", QuickActions.ToggleMic, () => Hub.Audio.MicMuted, "Red", "i.micoff");
        Add("Не беспокоить", "i.moon", QuickActions.ToggleDnd, () => QuickActions.DndOn, "Indigo");
        Add("Загрузки", "i.down", QuickActions.OpenDownloads);
    }

    void Add(string label, string icon, Action run, Func<bool>? isOn = null, string onColor = "Label", string? onIcon = null)
    {
        var ic = new Icon { Data = Ui.G(icon), Width = 22, Height = 22 };
        var circle = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = Ui.B("Fill2"), Child = ic,
        };
        var text = new TextBlock
        {
            Text = label, FontSize = 11, Foreground = Ui.B("Label2"), TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), Height = 28, LineHeight = 13,
        };
        var sp = new StackPanel();
        sp.Children.Add(circle);
        sp.Children.Add(text);
        var b = new Button { Style = (Style)FindResource("Btn"), Content = sp, Margin = new Thickness(2, 0, 2, 10), VerticalContentAlignment = VerticalAlignment.Top };
        Ui.SetHover(b, 0);
        b.MouseEnter += (_, _) => { if (!(isOn?.Invoke() ?? false)) circle.Background = Ui.B("Fill3"); };
        b.MouseLeave += (_, _) => Paint();

        void Paint()
        {
            bool on = isOn?.Invoke() ?? false;
            circle.Background = on ? Ui.B(onColor) : Ui.B("Fill2");
            ic.Foreground = on && onColor == "Label" ? Brushes.Black : Brushes.White;
            if (onIcon != null) ic.Data = Ui.G(on ? onIcon : icon);
        }

        b.Click += (_, _) => { run(); Paint(); };
        _repaint.Add(Paint);
        Paint();
        Cmds.Children.Add(b);
    }
}
