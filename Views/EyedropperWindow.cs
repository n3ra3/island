using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Пипетка: прозрачное окно на весь экран, клик копирует цвет под курсором.</summary>
public sealed class EyedropperWindow : Window
{
    readonly Border _chip, _swatch;
    readonly TextBlock _hex;
    readonly Canvas _canvas = new();

    public EyedropperWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;

        _swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderBrush = Brushes.White, BorderThickness = new Thickness(2) };
        _hex = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("Font"),
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(_swatch);
        sp.Children.Add(_hex);
        _chip = new Border
        {
            Background = Ui.B("Fill1"), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(6, 6, 12, 6), Child = sp, IsHitTestVisible = false,
        };
        _canvas.Children.Add(_chip);
        Content = _canvas;

        MouseMove += (_, _) => Update();
        MouseLeftButtonDown += (_, _) => Pick();
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => { Activate(); Focus(); Update(); };
    }

    static Color Sample()
    {
        GetCursorPos(out var p);
        var dc = GetDC(IntPtr.Zero);
        uint c = GetPixel(dc, p.X, p.Y);
        ReleaseDC(IntPtr.Zero, dc);
        return Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    void Update()
    {
        var c = Sample();
        _swatch.Background = new SolidColorBrush(c);
        _hex.Text = Hex(c);
        var pos = Mouse.GetPosition(_canvas);
        Canvas.SetLeft(_chip, pos.X + 18);
        Canvas.SetTop(_chip, pos.Y + 18);
    }

    void Pick()
    {
        var hex = Hex(Sample());
        Close();
        try { Clipboard.SetText(hex); Hub.Toast($"Цвет {hex} скопирован"); }
        catch { Hub.Toast("Не получилось скопировать цвет", false); }
    }
}
