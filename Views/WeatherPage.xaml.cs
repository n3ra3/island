using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

public partial class WeatherPage : UserControl
{
    public WeatherPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Hub.Weather.Changed += Render;
            Render();
            if (Hub.Weather.Temp == null && !Hub.Weather.Loading) _ = Hub.Weather.RefreshAsync();
        };
        Unloaded += (_, _) => Hub.Weather.Changed -= Render;
    }

    async void Retry_Click(object sender, RoutedEventArgs e) => await Hub.Weather.RefreshAsync();

    void Render()
    {
        var w = Hub.Weather;
        if (w.Temp is not double t)
        {
            Body.Visibility = Visibility.Collapsed;
            State.Visibility = Visibility.Visible;
            StateText.Text = w.Loading ? "Загружаю погоду…" : "Нет связи с сервером погоды.\nПроверь интернет.";
            Retry.Visibility = w.Loading ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        Body.Visibility = Visibility.Visible;
        State.Visibility = Visibility.Collapsed;

        City.Text = Hub.Settings.City;
        Big.Text = $"{Math.Round(t)}°";
        Desc.Text = WeatherService.Describe(w.Code, w.Night);
        BigIcon.Set(w.Code, w.Night, animate: true);
        if (w.Days.Count > 0) HiLo.Text = $"Макс. {Math.Round(w.Days[0].Max)}°, мин. {Math.Round(w.Days[0].Min)}°";
        Extra.Text = $"Ветер {Math.Round(w.Wind)} м/с, влажность {w.Humidity}%";

        Hours.Children.Clear();
        foreach (var h in w.Hours)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = h.Label, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Ui.B("Label2"), HorizontalAlignment = HorizontalAlignment.Center });
            var hourIcon = new WeatherIcon { Width = 22, Height = 22, Margin = new Thickness(0, 7, 0, 7) };
            hourIcon.Set(h.Code, h.Night, animate: false);
            sp.Children.Add(hourIcon);
            sp.Children.Add(new TextBlock { Text = $"{Math.Round(h.Temp)}°", FontSize = 14, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center });
            Hours.Children.Add(sp);
        }

        Days.Children.Clear();
        if (w.Days.Count == 0) return;
        double lo = w.Days.Min(d => d.Min), hi = w.Days.Max(d => d.Max), span = Math.Max(1, hi - lo);
        var grad = new LinearGradientBrush(Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xFF, 0xD6, 0x0A), 0);
        grad.Freeze();
        for (int i = 0; i < w.Days.Count; i++)
        {
            var d = w.Days[i];
            var g = new Grid { Height = 32 };
            foreach (var width in new[] { 74.0, 30, 34, double.NaN, 34 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(width) ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });

            var name = new TextBlock { Text = d.Label, VerticalAlignment = VerticalAlignment.Center };
            var ic = new WeatherIcon { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
            ic.Set(d.Code, false, animate: false);
            var min = new TextBlock { Text = $"{Math.Round(d.Min)}°", Foreground = Ui.B("Label2"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var max = new TextBlock { Text = $"{Math.Round(d.Max)}°", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Ui.B("Fill2") });
            var fill = new Border { CornerRadius = new CornerRadius(2), Background = grad };
            track.Children.Add(fill);
            double a = (d.Min - lo) / span, b = (hi - d.Max) / span;
            track.SizeChanged += (_, e) => fill.Margin = new Thickness(e.NewSize.Width * a, 0, e.NewSize.Width * b, 0);

            Grid.SetColumn(ic, 1); Grid.SetColumn(min, 2); Grid.SetColumn(track, 3); Grid.SetColumn(max, 4);
            g.Children.Add(name); g.Children.Add(ic); g.Children.Add(min); g.Children.Add(track); g.Children.Add(max);

            if (i > 0) Days.Children.Add(new Border { Height = 0.5, Background = Ui.B("Sep") });
            Days.Children.Add(g);
        }
    }
}
