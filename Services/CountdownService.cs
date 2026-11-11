using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>Таймер. Работает, даже когда панель закрыта — тогда отсчёт виден в островке.</summary>
public sealed class CountdownService
{
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };
    DateTime _end;

    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Left { get; private set; } = TimeSpan.FromMinutes(5);
    public bool Running { get; private set; }
    public event Action? Changed;

    public CountdownService()
    {
        _t.Tick += (_, _) =>
        {
            Left = _end - DateTime.Now;
            if (Left <= TimeSpan.Zero)
            {
                Halt();
                Left = Total;
                System.Media.SystemSounds.Asterisk.Play();
                Hub.Toast("Таймер закончился");
            }
            Changed?.Invoke();
        };
    }

    /// <summary>Таймер не запущен и не начат — можно менять время.</summary>
    public bool Idle => !Running && Left == Total;

    public void Set(int minutes) => Set(TimeSpan.FromMinutes(minutes));
    public void Set(TimeSpan t) { Halt(); Total = Left = t; Changed?.Invoke(); }
    public void Start() { if (Running) return; _end = DateTime.Now + Left; Running = true; _t.Start(); Changed?.Invoke(); }
    public void Pause() { Halt(); Changed?.Invoke(); }
    public void Reset() { Halt(); Left = Total; Changed?.Invoke(); }
    void Halt() { Running = false; _t.Stop(); }
}
