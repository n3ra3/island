using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland;

/// <summary>
/// Цветной значок погоды 24×24 с лёгкой анимацией: лучи солнца вращаются, облака плывут,
/// капли и снег падают, молния вспыхивает. Анимацию можно выключить или ограничить по частоте кадров.
/// </summary>
public sealed class WeatherIcon : Viewbox
{
    static readonly Brush Sun = Frozen(0xFF, 0xD6, 0x0A);
    static readonly Brush CloudFront = Frozen(0xF2, 0xF2, 0xF7);
    static readonly Brush CloudBack = Frozen(0x8E, 0x8E, 0x93);
    static readonly Brush Rain = Frozen(0x64, 0xD2, 0xFF);
    static readonly Brush Moon = Frozen(0xE5, 0xE5, 0xEA);

    static readonly Geometry CloudBig = Geometry.Parse("M7,18.5 H17 A4,4 0 0 0 17.5,10.5 A6,6 0 0 0 6.1,11.7 A3.5,3.5 0 0 0 7,18.5 Z");
    static readonly Geometry CloudHigh = Geometry.Parse("M7,15 H17 A3.5,3.5 0 0 0 17.4,8 A5.5,5.5 0 0 0 6.9,9 A3,3 0 0 0 7,15 Z");
    static readonly Geometry MoonShape = Geometry.Parse("M17.5,14.5 A7,7 0 0 1 9.5,6.5 A7,7 0 1 0 17.5,14.5 Z");
    static readonly Geometry Bolt = Geometry.Parse("M13,13 L9.5,18.5 H12.5 L11,23 L15.5,16.5 H12.5 L14,13 Z");

    readonly Canvas _c = new() { Width = 24, Height = 24 };
    Storyboard? _sb;
    int _code = -1;
    bool _night, _animate;

    /// <summary>Частота кадров анимации. Для свёрнутого островка — пониже, чтобы не тратить ресурсы.</summary>
    public int FrameRate { get; set; } = 30;

    public WeatherIcon()
    {
        Child = _c;
        Stretch = Stretch.Uniform;
        Width = Height = 18;
    }

    static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    public void Set(int code, bool night, bool animate)
    {
        if (code == _code && night == _night && animate == _animate) return;
        _code = code; _night = night; _animate = animate;
        _sb?.Stop(_c);
        _c.Children.Clear();
        _sb = new Storyboard();
        Timeline.SetDesiredFrameRate(_sb, FrameRate);

        switch (Kind(code))
        {
            case "clear": if (night) AddMoon(); else AddSun(12, 12, 1); break;
            case "partly":
                if (night) AddMoon(-3, -3, 0.75); else AddSun(8.5, 8.5, 0.7);
                AddCloud(CloudBig, CloudFront, 1.2, 4, 0);
                break;
            case "cloudy":
                AddCloud(CloudBig, CloudBack, 1.5, 5, -3, -3);
                AddCloud(CloudBig, CloudFront, 1.2, 4, 0);
                break;
            case "fog": AddFog(); break;
            case "rain": AddCloud(CloudHigh, CloudFront, 0.8, 4, 0); AddDrops(false); break;
            case "snow": AddCloud(CloudHigh, CloudFront, 0.8, 4, 0); AddDrops(true); break;
            case "storm": AddCloud(CloudHigh, CloudBack, 0.8, 4, 0); AddBolt(); break;
        }
        if (animate && _sb.Children.Count > 0) _sb.Begin(_c, true);
    }

    public void Pause() => _sb?.Pause(_c);
    public void Resume() { if (_animate) _sb?.Resume(_c); }

    static string Kind(int code) => code switch
    {
        0 => "clear",
        1 or 2 => "partly",
        3 => "cloudy",
        45 or 48 => "fog",
        (>= 51 and <= 67) or (>= 80 and <= 82) => "rain",
        (>= 71 and <= 77) or 85 or 86 => "snow",
        >= 95 => "storm",
        _ => "cloudy",
    };

    // ---------- Слои ----------

    void AddSun(double cx, double cy, double scale)
    {
        var g = new Canvas { Width = 24, Height = 24, RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale, 12, 12), new TranslateTransform(cx - 12, cy - 12) } } };
        var rays = new Canvas { Width = 24, Height = 24 };
        var rot = new RotateTransform(0, 12, 12);
        rays.RenderTransform = rot;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            rays.Children.Add(new Line
            {
                X1 = 12 + Math.Cos(a) * 7, Y1 = 12 + Math.Sin(a) * 7, X2 = 12 + Math.Cos(a) * 9.5, Y2 = 12 + Math.Sin(a) * 9.5,
                Stroke = Sun, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
        g.Children.Add(rays);
        var core = new Ellipse { Width = 9, Height = 9, Fill = Sun, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        Canvas.SetLeft(core, 7.5); Canvas.SetTop(core, 7.5);
        g.Children.Add(core);
        _c.Children.Add(g);

        Anim(rot, RotateTransform.AngleProperty, 0, 360, 14, repeat: true, reverse: false);
        Anim(core, "RenderTransform.ScaleX", 1, 1.08, 1.8);
        Anim(core, "RenderTransform.ScaleY", 1, 1.08, 1.8);
    }

    void AddMoon(double dx = 0, double dy = 0, double scale = 1)
    {
        var g = new Canvas { Width = 24, Height = 24, RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale, 12, 12), new TranslateTransform(dx, dy) } } };
        g.Children.Add(new Path { Data = MoonShape, Fill = Moon });
        foreach (var (x, y, delay) in new[] { (18.5, 5.0, 0.0), (20.5, 10.0, 1.1) })
        {
            var star = new Ellipse { Width = 1.8, Height = 1.8, Fill = Moon };
            Canvas.SetLeft(star, x); Canvas.SetTop(star, y);
            g.Children.Add(star);
            Anim(star, OpacityProperty, 1, 0.2, 1.4, begin: delay);
        }
        _c.Children.Add(g);
    }

    void AddCloud(Geometry geo, Brush fill, double drift, double seconds, double phase, double dy = 0)
    {
        var t = new TranslateTransform(0, dy);
        var p = new Path { Data = geo, Fill = fill, RenderTransform = t };
        _c.Children.Add(p);
        Anim(t, TranslateTransform.XProperty, -drift, drift, seconds, begin: phase);
    }

    void AddDrops(bool snow)
    {
        double[] xs = { 8, 12, 16 };
        for (int i = 0; i < 3; i++)
        {
            var t = new TranslateTransform();
            Shape drop = snow
                ? new Ellipse { Width = 2.4, Height = 2.4, Fill = CloudFront }
                : new Line { X1 = 0, Y1 = 0, X2 = -0.8, Y2 = 3, Stroke = Rain, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            drop.RenderTransform = t;
            Canvas.SetLeft(drop, xs[i] - (snow ? 1.2 : 0));
            Canvas.SetTop(drop, 15.5);
            _c.Children.Add(drop);
            double dur = snow ? 2.2 : 0.9, delay = i * dur / 3;
            Anim(t, TranslateTransform.YProperty, 0, snow ? 6 : 6.5, dur, reverse: false, begin: delay, ease: false);
            Anim(drop, OpacityProperty, 1, 0, dur, reverse: false, begin: delay, ease: false);
            if (snow) Anim(t, TranslateTransform.XProperty, -0.8, 0.8, 0.7, begin: delay);
        }
    }

    void AddBolt()
    {
        var bolt = new Path { Data = Bolt, Fill = Sun, Opacity = 0.35 };
        _c.Children.Add(bolt);
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(2.6) };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8))));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.9))));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
        Storyboard.SetTarget(a, bolt);
        Storyboard.SetTargetProperty(a, new PropertyPath(OpacityProperty));
        _sb!.Children.Add(a);
    }

    void AddFog()
    {
        double[] ys = { 8, 12, 16 };
        for (int i = 0; i < 3; i++)
        {
            var t = new TranslateTransform();
            var l = new Line
            {
                X1 = 4 + (i % 2) * 2, Y1 = ys[i], X2 = 20 - (i % 2) * 2, Y2 = ys[i], Stroke = CloudFront, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = t,
            };
            _c.Children.Add(l);
            Anim(t, TranslateTransform.XProperty, -1.5, 1.5, 2.5 + i * 0.4, begin: i * 0.5);
        }
    }

    // ---------- Анимации ----------

    void Anim(DependencyObject target, object prop, double from, double to, double seconds,
        bool repeat = true, bool reverse = true, double begin = 0, bool ease = true)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = reverse,
            RepeatBehavior = repeat ? RepeatBehavior.Forever : new RepeatBehavior(1),
            BeginTime = TimeSpan.FromSeconds(begin),
        };
        if (ease && reverse) a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, prop is DependencyProperty dp ? new PropertyPath(dp) : new PropertyPath((string)prop));
        _sb!.Children.Add(a);
    }
}
