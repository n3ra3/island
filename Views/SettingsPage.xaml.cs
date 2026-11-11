using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DynamicIsland;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        var s = Hub.Settings;
        AddRow("Сжиматься над развёрнутыми окнами", "Когда браузер или другое окно на весь экран, островок становится тонкой полоской и не мешает вкладкам. Наведи — раскроется", s.MiniOverMaximized, v => s.MiniOverMaximized = v);
        AddRow("Раскрывать при наведении", "Мини-плеер при наведении мыши. Выключи — островок будет раскрываться только по клику", s.HoverPeek, v => s.HoverPeek = v);
        AddRow("Глазки", "Следят за курсором, когда островок свёрнут", s.Eyes, v => s.Eyes = v);
        AddRow("Запускать вместе с Windows", "Островок появится сразу после входа", s.Autostart, v => s.Autostart = v);
        AddRow("Обновляться автоматически", "Раз в несколько часов проверять новую версию и предлагать обновиться", s.AutoUpdate, v => s.AutoUpdate = v);

        Loaded += (_, _) => { Hub.Updates.Changed += RenderUpdate; RenderUpdate(); };
        Unloaded += (_, _) => Hub.Updates.Changed -= RenderUpdate;

        CityNow.Text = $"Сейчас: {s.City}";
        CityBox.Text = s.City;
        CityBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) City_Click(this, new RoutedEventArgs()); };
    }

    void AddRow(string title, string desc, bool value, Action<bool> set)
    {
        if (Rows.Children.Count > 0) Rows.Children.Add(new Border { Height = 0.5, Background = Ui.B("Sep") });
        var g = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium });
        sp.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = Ui.B("Label2"), Margin = new Thickness(0, 1, 0, 0) });
        var sw = new ToggleButton { Style = (Style)FindResource("Switch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        sw.Click += (_, _) =>
        {
            set(sw.IsChecked == true);
            Hub.Settings.Save();
            Hub.Main?.ApplySettings();
        };
        Grid.SetColumn(sw, 1);
        g.Children.Add(sp);
        g.Children.Add(sw);
        Rows.Children.Add(g);
    }

    async void City_Click(object sender, RoutedEventArgs e)
    {
        var q = CityBox.Text.Trim();
        if (q.Length == 0) return;
        var found = await WeatherService.FindCity(q);
        if (found == null) { Hub.Toast("Город не найден", false); return; }
        var s = Hub.Settings;
        (s.City, s.Lat, s.Lon) = found.Value;
        s.Save();
        CityBox.Text = s.City;
        CityNow.Text = $"Сейчас: {s.City}";
        Hub.Toast($"Город: {s.City}");
        await Hub.Weather.RefreshAsync();
    }

    void RenderUpdate()
    {
        var u = Hub.Updates;
        VersionText.Text = $"Версия {UpdateService.Current}";
        if (u.Busy) { UpdateStatus.Text = "Проверяю…"; UpdateBtn.IsEnabled = false; return; }
        UpdateBtn.IsEnabled = true;
        if (u.Available is { } a)
        {
            UpdateStatus.Text = $"Доступна версия {a.Version}";
            UpdateBtnText.Text = $"Обновить до {a.Version}";
            UpdateBtn.Background = Ui.B("Blue");
        }
        else
        {
            UpdateStatus.Text = u.LastCheck == default ? "Обновления берутся с GitHub" : $"Последняя версия. Проверено в {u.LastCheck:H:mm}";
            UpdateBtnText.Text = "Проверить обновления";
            UpdateBtn.Background = Ui.B("Fill2");
        }
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.Updates.Available is { } a) await Hub.Updates.InstallAsync(a);
        else await Hub.Updates.CheckAsync(silent: false);
    }

    void Quit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();
}
