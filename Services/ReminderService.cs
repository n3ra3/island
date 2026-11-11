using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace DynamicIsland;

public sealed class Reminder
{
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
    public bool Daily { get; set; }

    static readonly CultureInfo Ru = new("ru-RU");

    public string WhenText()
    {
        var now = DateTime.Now;
        var time = Due.ToString("H:mm");
        if (Daily) return $"каждый день в {time}";
        var left = Due - now;
        if (left.TotalMinutes is > 0 and < 60) return $"через {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} мин";
        if (Due.Date == now.Date) return $"сегодня в {time}";
        if (Due.Date == now.Date.AddDays(1)) return $"завтра в {time}";
        return $"{Due.ToString("d MMMM", Ru)} в {time}";
    }
}

/// <summary>Напоминания, заданные обычной фразой: «позвонить маме завтра в 9», «через 20 минут выключить плиту».</summary>
public sealed class ReminderService
{
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(10) };
    public List<Reminder> Items => Hub.Settings.Reminders;
    public event Action<Reminder>? Due;
    public event Action? Changed;

    public void Start()
    {
        _t.Tick += (_, _) => Check();
        _t.Start();
        Check();
    }

    void Check()
    {
        var now = DateTime.Now;
        bool changed = false;
        foreach (var r in Items.Where(r => r.Due <= now).ToList())
        {
            System.Media.SystemSounds.Exclamation.Play();
            Due?.Invoke(r);
            if (r.Daily) { while (r.Due <= now) r.Due = r.Due.AddDays(1); }
            else Items.Remove(r);
            changed = true;
        }
        if (changed) { Hub.Settings.Save(); Changed?.Invoke(); }
    }

    /// <summary>Добавляет напоминание. Возвращает null или текст ошибки.</summary>
    public string? Add(string input)
    {
        var r = Parse(input, DateTime.Now);
        if (r == null) return "Не понял, когда напомнить. Например: «позвонить маме завтра в 9»";
        Items.Add(r);
        Items.Sort((a, b) => a.Due.CompareTo(b.Due));
        Hub.Settings.Save();
        Changed?.Invoke();
        return null;
    }

    public void Remove(Reminder r) { Items.Remove(r); Hub.Settings.Save(); Changed?.Invoke(); }

    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static Reminder? Parse(string input, DateTime now)
    {
        var s = " " + input.Trim() + " ";
        DateTime? due = null;
        bool daily = false;

        // «через 20 минут», «через час», «через 2 часа»
        var m = Regex.Match(s, @"\sчерез\s+(\d+)?\s*(минут[уы]?|мин\.?|час(?:а|ов)?|ч\.?|секунд[уы]?|сек\.?)(?=\s|[,.!?])", O);
        if (m.Success)
        {
            int n = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1;
            var unit = m.Groups[2].Value.ToLowerInvariant();
            due = unit.StartsWith("ч") ? now.AddHours(n) : unit.StartsWith("с") ? now.AddSeconds(n) : now.AddMinutes(n);
            s = s.Remove(m.Index, m.Length).Insert(m.Index, " ");
        }
        else
        {
            var d = Regex.Match(s, @"\s(каждый\s+день|ежедневно)(?=\s|[,.!?])", O);
            if (d.Success) { daily = true; s = s.Remove(d.Index, d.Length).Insert(d.Index, " "); }

            int? dayOffset = null;
            var day = Regex.Match(s, @"\s(послезавтра|завтра|сегодня)(?=\s|[,.!?])", O);
            if (day.Success)
            {
                dayOffset = day.Groups[1].Value.ToLowerInvariant() switch { "послезавтра" => 2, "завтра" => 1, _ => 0 };
                s = s.Remove(day.Index, day.Length).Insert(day.Index, " ");
            }

            var t = Regex.Match(s, @"\sв\s+(\d{1,2})(?:[:.](\d{2}))?(?:\s*час(?:а|ов)?)?(?:\s*(утра|вечера|дня|ночи))?(?=\s|[,.!?])", O);
            int? hh = null, mm = null;
            if (t.Success)
            {
                hh = int.Parse(t.Groups[1].Value);
                mm = t.Groups[2].Success ? int.Parse(t.Groups[2].Value) : 0;
                var part = t.Groups[3].Value.ToLowerInvariant();
                if ((part is "вечера" or "дня") && hh < 12) hh += 12;
                if (part == "ночи" && hh == 12) hh = 0;
                s = s.Remove(t.Index, t.Length).Insert(t.Index, " ");
            }
            if (hh is > 23 || mm is > 59) return null;

            if (hh != null)
            {
                var at = now.Date.AddHours(hh.Value).AddMinutes(mm!.Value).AddDays(dayOffset ?? 0);
                if (dayOffset == null && at <= now) at = at.AddDays(1);
                due = at;
            }
            else if (dayOffset != null) due = now.Date.AddDays(dayOffset.Value).AddHours(9);
        }
        if (due == null) return null;

        var text = Regex.Replace(s, @"^\s*напомни(ть)?(\s+мне)?\s*", "", O);
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '.', '!', '-', '—');
        if (text.Length == 0) text = "Напоминание";
        text = char.ToUpper(text[0]) + text[1..];
        return new Reminder { Text = text, Due = due.Value, Daily = daily };
    }
}
