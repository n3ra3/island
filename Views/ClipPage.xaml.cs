using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

public partial class ClipPage : UserControl
{
    public ClipPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Hub.Clip.Changed += Render; Render(); };
        Unloaded += (_, _) => Hub.Clip.Changed -= Render;
        Search.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Render();
        };
    }

    void Clear_Click(object sender, RoutedEventArgs e) => Hub.Clip.Clear();

    void Render()
    {
        List.Children.Clear();
        var q = Search.Text.Trim();
        var items = Hub.Clip.Items.Where(i => q.Length == 0 || i.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Empty.Text = Hub.Clip.Items.Count == 0
            ? "Здесь появится всё, что ты скопируешь.\nНажми на запись, чтобы скопировать её снова."
            : items.Count == 0 ? "Ничего не нашлось" : "";

        foreach (var it in items)
        {
            var icon = it.Kind switch { "link" => "i.link", "image" => "i.photo", _ => "i.t" };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Ui.B("Fill2"),
                Child = new Icon { Data = Ui.G(icon), Width = 16, Height = 16, Foreground = Ui.B("Label2") },
            };
            if (it.Image != null)
                box.Child = new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(it.Image) { Stretch = Stretch.UniformToFill } };
            g.Children.Add(box);

            var text = new TextBlock
            {
                Text = it.Text.Replace("\r", " ").Replace("\n", " "), TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
            };
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var time = new TextBlock { Text = Ago(it.Time), FontSize = 12, Foreground = Ui.B("Label3"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(time, 2);
            g.Children.Add(time);

            var b = new Button
            {
                Style = (Style)FindResource("Btn"), Content = g, Padding = new Thickness(10, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2),
            };
            Ui.SetRadius(b, new CornerRadius(12));
            b.Click += (_, _) => { Hub.Clip.Copy(it); Hub.Toast("Скопировано"); };
            List.Children.Add(b);
        }
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "сейчас";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} мин";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} ч";
        return t.ToString("d MMM");
    }
}
