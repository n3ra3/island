using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace DynamicIsland;

public partial class SystemPage : UserControl
{
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<(double[] Data, Path Line, Path Area, TextBlock Value, Func<bool> Ok)> _graphs = new();

    public SystemPage()
    {
        InitializeComponent();
        AddGraph("Процессор", PerfMonitor.Cpu, Color.FromRgb(0x30, 0xD1, 0x58), () => true);
        AddGraph("Память", PerfMonitor.Ram, Color.FromRgb(0x0A, 0x84, 0xFF), () => true);
        AddGraph("Видеокарта", PerfMonitor.Gpu, Color.FromRgb(0xBF, 0x5A, 0xF2), () => PerfMonitor.GpuAvailable);

        AddRadioToggle("Wi-Fi", "i.wifi", RadioKind.WiFi);
        AddRadioToggle("Bluetooth", "i.bt", RadioKind.Bluetooth);
        AddToggle("Тёмная тема", "i.half", () => QuickActions.DarkTheme,
            on => { QuickActions.DarkTheme = on; return Task.FromResult(true); },
            "Включена", "Выключена", "Тёмная тема включена", "Тёмная тема выключена");

        AddPower("Заблокировать", "i.lock", false, QuickActions.Lock);
        AddPower("Сон", "i.moon", false, QuickActions.Sleep);
        AddPower("Перезагрузить", "i.restart", true, QuickActions.Restart);
        AddPower("Выключить", "i.power", true, QuickActions.Shutdown, danger: true);

        _tick.Tick += async (_, _) => { await PerfMonitor.SampleAsync(); Draw(); };
        Loaded += async (_, _) => { _tick.Start(); await PerfMonitor.SampleAsync(); Draw(); };
        Unloaded += (_, _) => _tick.Stop();
    }

    // ---------- Графики ----------

    void AddGraph(string name, double[] data, Color color, Func<bool> ok)
    {
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        var value = new TextBlock { FontSize = 20, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Right };
        head.Children.Add(value);

        var line = new Path { Stroke = new SolidColorBrush(color), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        var area = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)) };
        var plot = new Grid { Height = 44, Margin = new Thickness(-14, 6, -14, 0), ClipToBounds = true };
        plot.Children.Add(area);
        plot.Children.Add(line);
        plot.SizeChanged += (_, _) => Draw();

        var sp = new StackPanel();
        sp.Children.Add(head);
        sp.Children.Add(plot);
        var card = new Border
        {
            Background = Ui.B("Fill1"), CornerRadius = new CornerRadius(20), Padding = new Thickness(14, 10, 14, 0),
            Margin = new Thickness(5, 0, 5, 0), Child = sp, ClipToBounds = true,
        };
        Perf.Children.Add(card);
        _graphs.Add((data, line, area, value, ok));
    }

    void Draw()
    {
        foreach (var (data, line, area, value, ok) in _graphs)
        {
            if (!ok())
            {
                value.Inlines.Clear();
                value.Inlines.Add(new System.Windows.Documents.Run("—"));
                line.Data = area.Data = null;
                continue;
            }
            value.Inlines.Clear();
            value.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(data[^1])}"));
            value.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 12, Foreground = Ui.B("Label2"), FontWeight = FontWeights.Normal });

            var host = (FrameworkElement)line.Parent;
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 0) continue;
            var pts = new Point[data.Length];
            for (int i = 0; i < data.Length; i++)
                pts[i] = new Point(i * w / (data.Length - 1), h - 2 - data[i] / 100 * (h - 4));

            var lg = new StreamGeometry();
            using (var c = lg.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToArray(), true, true); }
            lg.Freeze();
            var ag = new StreamGeometry();
            using (var c = ag.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                c.PolyLineTo(pts.Append(new Point(w, h)).ToArray(), false, false);
            }
            ag.Freeze();
            line.Data = lg;
            area.Data = ag;
        }
    }

    // ---------- Переключатели ----------

    void AddRadioToggle(string name, string icon, RadioKind kind)
    {
        Radio? radio = null;
        AddToggle(name, icon,
            () => radio?.State == RadioState.On,
            async on =>
            {
                radio ??= await QuickActions.GetRadio(kind);
                if (radio == null) return false;
                var r = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
                return r == RadioAccessStatus.Allowed;
            },
            "Включён", "Выключен", $"{name} включён", $"{name} выключен",
            async () => { radio = await QuickActions.GetRadio(kind); return radio != null; });
    }

    void AddToggle(string name, string icon, Func<bool> isOn, Func<bool, Task<bool>> set,
        string onText, string offText, string onHud, string offHud, Func<Task<bool>>? init = null)
    {
        var circle = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Ui.B("Fill2"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new Icon { Data = Ui.G(icon), Width = 20, Height = 20 },
        };
        var status = new TextBlock { FontSize = 12, Foreground = Ui.B("Label2") };
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        bottom.Children.Add(status);
        var g = new Grid();
        g.Children.Add(circle);
        g.Children.Add(bottom);

        var b = new Button
        {
            Style = (Style)FindResource("Btn"), Content = g, Background = Ui.B("Fill1"), Padding = new Thickness(14),
            Margin = new Thickness(5, 0, 5, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        Ui.SetRadius(b, new CornerRadius(20));

        bool available = init == null;
        void Paint()
        {
            bool on = isOn();
            circle.Background = Ui.B(on ? "Blue" : "Fill2");
            status.Text = !available ? "Недоступно" : on ? onText : offText;
        }

        b.Click += async (_, _) =>
        {
            bool target = !isOn();
            if (await set(target)) Hub.Toast(target ? onHud : offHud);
            else Hub.Toast($"Не получилось переключить: {name}", false);
            Paint();
        };
        Paint();
        if (init != null) Loaded += async (_, _) => { available = await init(); Paint(); };
        Toggles.Children.Add(b);
    }

    void AddPower(string label, string icon, bool confirm, Action run, bool danger = false)
    {
        var text = new TextBlock { Text = label, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Icon { Data = Ui.G(icon), Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
        sp.Children.Add(text);
        var b = new Button
        {
            Style = (Style)FindResource("Btn"), Content = sp, Height = 54, Background = Ui.B("Fill1"),
            Margin = new Thickness(5, 0, 5, 0), Foreground = danger ? Ui.B("Red") : Ui.B("Label"),
        };
        Ui.SetRadius(b, new CornerRadius(16));

        // Перезагрузка и выключение — только после второго нажатия
        var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        bool armed = false;
        reset.Tick += (_, _) => { reset.Stop(); armed = false; text.Text = label; };
        b.Click += (_, _) =>
        {
            if (confirm && !armed) { armed = true; text.Text = "Нажми ещё раз"; reset.Start(); return; }
            reset.Stop();
            try { run(); } catch { Hub.Toast("Не получилось", false); }
        };
        Power.Children.Add(b);
    }
}
