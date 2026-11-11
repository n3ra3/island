using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Глазки: плавно следят за мышью, засыпают, сонные ночью, удивляются, кружатся, качаются под музыку.</summary>
public partial class MainWindow
{
    enum Mood { Normal, Sleepy, Asleep, Surprised, Dizzy }

    Mood _mood = Mood.Normal;
    DateTime _moodUntil;
    double _lidY = 1;                 // насколько открыты глаза сейчас (1 — полностью)
    double _eyeTx, _eyeTy;            // куда смотрят зрачки
    DateTime _lastMove = DateTime.Now;
    bool _surprisePending, _busy;     // _busy — идёт «живая мелочь», мышь не перебивает
    Storyboard? _zzz;
    readonly List<(DateTime t, int dir)> _shakes = new();
    int _lastDirX;
    int _lastShakeX = int.MinValue;

    readonly DispatcherTimer _moodTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _lifeTimer = new();

    // 5 минут без мыши и клавиатуры (для проверки можно сократить файлом sleep-fast.on)
    static readonly double SleepAfter = System.IO.File.Exists(System.IO.Path.Combine(Settings.Dir, "sleep-fast.on")) ? 10 : 300;

    void InitEyes()
    {
        _moodTimer.Tick += (_, _) => EvaluateMood();
        _moodTimer.Start();
        _lifeTimer.Tick += (_, _) => DoLifeThing();
        ScheduleLife();
        EvaluateMood();
    }

    void UpdateEyes()
    {
        bool on = Hub.Settings.Eyes && _state == IslandState.Compact && !_hidden && !Off("eyes");
        if (on)
        {
            if (!_eyeTimer.IsEnabled) { _eyeTimer.Start(); ScheduleBlink(); }
            if (_surprisePending) { _surprisePending = false; Surprise(); }
        }
        else
        {
            _eyeTimer.Stop();
            _blinkTimer.Stop();
        }
    }

    // ---------- Слежение за мышью (плавно) ----------

    void MoveEyes()
    {
        if (!GetCursorPos(out var p)) return;
        if (p.X == _lastCursor.X && p.Y == _lastCursor.Y) return; // мышь стоит — ничего не делаем
        DetectShake(p);
        _lastCursor = p;
        _lastMove = DateTime.Now;
        if (_mood == Mood.Asleep) { EvaluateMood(); return; } // тронул мышь — сразу просыпается
        if (_mood is Mood.Asleep or Mood.Dizzy or Mood.Surprised || _busy || !Eyes.IsVisible) return;

        var c = Eyes.PointToScreen(new Point(8.5, 7));
        double dx = p.X - c.X, dy = p.Y - c.Y;
        double dist = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / 150);
        double ang = Math.Atan2(dy, dx);
        LookAt(Math.Cos(ang) * 2.5 * dist, Math.Sin(ang) * 2 * dist, 180);
    }

    /// <summary>Зрачки мягко «доплывают» до новой точки, а не прыгают.</summary>
    void LookAt(double x, double y, int ms)
    {
        if (Math.Abs(_eyeTx - x) < 0.4 && Math.Abs(_eyeTy - y) < 0.4) return;
        _eyeTx = x; _eyeTy = y;
        var d = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var t in new[] { PupilL, PupilR })
        {
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
        }
    }

    // ---------- Моргание ----------

    void ScheduleBlink()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_mood == Mood.Sleepy ? _rnd.Next(4000, 9000) : _rnd.Next(2500, 6000));
        _blinkTimer.Start();
    }

    void BlinkOnce()
    {
        _blinkTimer.Stop();
        if (_mood is Mood.Normal or Mood.Sleepy && !_busy)
        {
            int ms = _mood == Mood.Sleepy ? 180 : 80; // сонный моргает медленно
            Blink.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(_lidY, 0.1, TimeSpan.FromMilliseconds(ms)) { AutoReverse = true });
        }
        ScheduleBlink();
    }

    void Lids(double scaleX, double scaleY, int ms, IEasingFunction? ease = null)
    {
        _lidY = scaleY;
        ease ??= new CubicEase { EasingMode = EasingMode.EaseInOut };
        Blink.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleX, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleY, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    // ---------- Настроение ----------

    void EvaluateMood()
    {
        if (_mood is Mood.Surprised or Mood.Dizzy && DateTime.Now < _moodUntil) return;
        double idle = IdleSeconds();
        int hour = DateTime.Now.Hour;
        var m = idle >= SleepAfter ? Mood.Asleep : hour < 6 ? Mood.Sleepy : Mood.Normal;
        // Проснулся — опрос раз в 2 с слишком медленный, поэтому глазки открываются при первом же движении мыши
        SetMood(m);
    }

    void SetMood(Mood m)
    {
        if (m == _mood) return;
        var prev = _mood;
        _mood = m;
        StopDizzy();
        if (m != Mood.Asleep) StopZzz();

        switch (m)
        {
            case Mood.Normal:
                if (prev == Mood.Asleep) WakeUp();
                else Lids(1, 1, 300);
                break;
            case Mood.Sleepy:
                if (prev == Mood.Asleep) WakeUp(0.45); else Lids(1, 0.45, 600);
                break;
            case Mood.Asleep:
                LookAt(0, 1, 900);
                Lids(1.05, 0.14, 1400);   // медленно закрываются в черточки
                StartZzz();
                break;
            case Mood.Surprised:
                LookAt(0, 0, 120);
                Lids(1.25, 1.3, 220, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
                break;
            case Mood.Dizzy:
                StartDizzy();
                break;
        }
    }

    /// <summary>Просыпается: открывает глаза и дважды моргает.</summary>
    void WakeUp(double to = 1)
    {
        _lidY = to;
        var k = new DoubleAnimationUsingKeyFrames();
        k.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        k.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
        k.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
        k.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580))));
        k.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, k);
        Blink.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
        LookAt(0, 0, 300);
    }

    /// <summary>Пришло уведомление: глазки распахиваются (показываем, когда островок свернётся обратно).</summary>
    public void SurpriseLater() => _surprisePending = true;

    void Surprise()
    {
        if (_mood == Mood.Asleep) { _mood = Mood.Normal; StopZzz(); } // уведомление будит
        _moodUntil = DateTime.Now.AddSeconds(1.6);
        SetMood(Mood.Surprised);
        var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        back.Tick += (_, _) => { back.Stop(); if (_mood == Mood.Surprised) { _mood = Mood.Normal; Lids(1, 1, 350); EvaluateMood(); } };
        back.Start();
    }

    // ---------- Сон: «z z z» ----------

    void StartZzz()
    {
        if (_zzz != null) return;
        _zzz = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(_zzz, 15); // спит — значит, никто не смотрит: экономим
        void Float(UIElement el, TranslateTransform t, double delay)
        {
            var op = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(delay), Duration = TimeSpan.FromSeconds(2.4) };
            op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            op.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
            Storyboard.SetTarget(op, el); Storyboard.SetTargetProperty(op, new PropertyPath(OpacityProperty));
            var y = new DoubleAnimation(3, -5, TimeSpan.FromSeconds(2.4)) { BeginTime = TimeSpan.FromSeconds(delay) };
            Storyboard.SetTarget(y, t); Storyboard.SetTargetProperty(y, new PropertyPath(TranslateTransform.YProperty));
            var x = new DoubleAnimation(0, 3, TimeSpan.FromSeconds(2.4)) { BeginTime = TimeSpan.FromSeconds(delay) };
            Storyboard.SetTarget(x, t); Storyboard.SetTargetProperty(x, new PropertyPath(TranslateTransform.XProperty));
            _zzz!.Children.Add(op); _zzz.Children.Add(y); _zzz.Children.Add(x);
        }
        Float(Zzz1, Zzz1T, 0);
        Float(Zzz2, Zzz2T, 1.2);
        _zzz.Begin(this, true);
    }

    void StopZzz()
    {
        if (_zzz == null) return;
        _zzz.Stop(this);
        _zzz = null;
        Zzz1.Opacity = Zzz2.Opacity = 0;
    }

    // ---------- Головокружение от тряски мыши ----------

    void DetectShake(POINT p)
    {
        if (_lastShakeX == int.MinValue) { _lastShakeX = p.X; return; }
        int dx = p.X - _lastShakeX;
        _lastShakeX = p.X;
        if (Math.Abs(dx) < 35) return;          // медленное движение — не тряска
        int dir = Math.Sign(dx);
        var now = DateTime.Now;
        if (dir != _lastDirX) _shakes.Add((now, dir));
        _lastDirX = dir;
        _shakes.RemoveAll(s => (now - s.t).TotalSeconds > 1.2);
        if (_shakes.Count >= 5 && _mood != Mood.Dizzy && _mood != Mood.Asleep)
        {
            _shakes.Clear();
            _moodUntil = now.AddSeconds(2.5);
            SetMood(Mood.Dizzy);
            var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            back.Tick += (_, _) => { back.Stop(); if (_mood == Mood.Dizzy) { _mood = Mood.Normal; StopDizzy(); EvaluateMood(); } };
            back.Start();
        }
    }

    bool _orbitOn;

    void StartDizzy()
    {
        // Зрачки вместе описывают круг в одну сторону
        foreach (var t in new[] { PupilL, PupilR })
        {
            t.BeginAnimation(TranslateTransform.XProperty, Orbit(2.2, 0, 520, true));
            t.BeginAnimation(TranslateTransform.YProperty, Orbit(1.8, 0, 520, false));
        }
        // Над глазками — орбита, по ней бегут две звёздочки (в противофазе)
        Star1T.BeginAnimation(TranslateTransform.XProperty, Orbit(9.5, 0, 900, true, 9.5));
        Star1T.BeginAnimation(TranslateTransform.YProperty, Orbit(3, 0, 900, false, 4));
        Star2T.BeginAnimation(TranslateTransform.XProperty, Orbit(9.5, 0.5, 900, true, 9.5));
        Star2T.BeginAnimation(TranslateTransform.YProperty, Orbit(3, 0.5, 900, false, 4));
        _orbitOn = true;
        Dizzy.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>Движение по кругу (эллипсу): X = cos, Y = sin, по 12 точкам — плавно и без рывков.</summary>
    static DoubleAnimationUsingKeyFrames Orbit(double radius, double phase, int periodMs, bool cos, double center = 0)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(periodMs) };
        for (int i = 0; i <= 12; i++)
        {
            double ang = 2 * Math.PI * (i / 12.0 + phase);
            double v = center + radius * (cos ? Math.Cos(ang) : Math.Sin(ang));
            a.KeyFrames.Add(new SplineDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(periodMs * i / 12.0))));
        }
        return a;
    }

    void StopDizzy()
    {
        if (_orbitOn)
        {
            _orbitOn = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) =>
            {
                if (_orbitOn) return;
                foreach (var t in new[] { Star1T, Star2T })
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                }
            };
            Dizzy.BeginAnimation(OpacityProperty, fade);
        }
        _eyeTx = _eyeTy = double.NaN;
        LookAt(0, 0, 300);
        if (_mood == Mood.Normal) Lids(1, 1, 250);
    }

    // ---------- Покачивание под музыку ----------

    double _bassAvg;
    DateTime _lastNod;

    /// <summary>
    /// Вызывается из эквалайзера ~24 раза в секунду. Ловим удар баса (резкий всплеск выше среднего)
    /// и делаем короткий кивок вниз — как человек, качающий головой в такт.
    /// </summary>
    void BobToMusic(double bass)
    {
        _bassAvg = _bassAvg * 0.92 + bass * 0.08; // «средний» уровень баса за последние ~0,5 с
        if (_mood is not (Mood.Normal or Mood.Sleepy) || _busy) return;
        var now = DateTime.Now;
        bool hit = bass > 0.5 && bass > _bassAvg * 1.3 && (now - _lastNod).TotalMilliseconds > 280;
        if (!hit) return;
        _lastNod = now;
        var nod = new DoubleAnimationUsingKeyFrames();
        nod.KeyFrames.Add(new EasingDoubleKeyFrame(2.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        EyesBob.BeginAnimation(TranslateTransform.YProperty, nod);
    }

    // ---------- Живые мелочи ----------

    void ScheduleLife()
    {
        _lifeTimer.Interval = TimeSpan.FromSeconds(_rnd.Next(120, 300));
        _lifeTimer.Start();
    }

    void DoLifeThing()
    {
        ScheduleLife();
        if (_mood != Mood.Normal || _busy || _state != IslandState.Compact || _hidden || !Hub.Settings.Eyes) return;
        if ((DateTime.Now - _lastMove).TotalSeconds < 3) return; // ты как раз двигаешь мышь — не мешаем

        _busy = true;
        int pick = _rnd.Next(5);
        double total;
        switch (pick)
        {
            case 0: // посмотреть по сторонам
                foreach (var t in new[] { PupilL, PupilR })
                    t.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (350, -2.5), (900, -2.5), (1450, 2.5), (2000, 2.5), (2400, 0)));
                total = 2.4; break;
            case 1: // посмотреть вверх
                foreach (var t in new[] { PupilL, PupilR })
                    t.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (300, -2.2), (1300, -2.2), (1650, 0)));
                total = 1.7; break;
            case 2: // подмигнуть
                LidR.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (120, 0.1), (420, 0.1), (560, 1)));
                total = 0.6; break;
            case 3: // прищуриться
                Blink.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (250, 0.5), (1100, 0.5), (1400, 1)));
                total = 1.4; break;
            default: // дважды моргнуть
                Blink.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (80, 0.1), (160, 1), (300, 1), (380, 0.1), (460, 1)));
                total = 0.5; break;
        }
        var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(total + 0.1) };
        done.Tick += (_, _) => { done.Stop(); _busy = false; _eyeTx = _eyeTy = double.NaN; };
        done.Start();
    }

    static DoubleAnimationUsingKeyFrames Keys(params (int ms, double v)[] pts)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, v) in pts)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }
}
