using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public static class Ui
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(0)));
    public static CornerRadius GetRadius(DependencyObject o) => (CornerRadius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, CornerRadius v) => o.SetValue(RadiusProperty, v);

    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached(
        "Hover", typeof(double), typeof(Ui), new FrameworkPropertyMetadata(0.07));
    public static double GetHover(DependencyObject o) => (double)o.GetValue(HoverProperty);
    public static void SetHover(DependencyObject o, double v) => o.SetValue(HoverProperty, v);

    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    public static Geometry G(string key) => (Geometry)Application.Current.FindResource(key);
}

/// <summary>Иконка в стиле SF Symbols: контур 24×24, цвет наследуется от Foreground.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(Icon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Icon() { Width = 18; Height = 18; SnapsToDevicePixels = false; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Data == null) return;
        double s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        if (Filled) dc.DrawGeometry(Foreground, null, Data);
        else
        {
            var pen = new Pen(Foreground, Thickness)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
            };
            dc.DrawGeometry(null, pen, Data);
        }
        dc.Pop(); dc.Pop();
    }
}

/// <summary>Пружинная кривая, как у анимаций iOS.</summary>
public sealed class SpringEase : EasingFunctionBase
{
    public SpringEase() => EasingMode = EasingMode.EaseIn;
    public double Damping { get; init; } = 9;
    protected override double EaseInCore(double t) => 1 - Math.Exp(-Damping * t) * Math.Cos(11 * t) * (1 - t * 0.02);
    protected override Freezable CreateInstanceCore() => new SpringEase { Damping = Damping };
}
