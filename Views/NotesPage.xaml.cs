using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class NotesPage : UserControl
{
    static int _active; // какая заметка открыта (помним между открытиями панели)

    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly List<Button> _presets = new();
    bool _loading, _idleShown = true, _deleteArmed;

    List<NoteDoc> Notes => Hub.Settings.NotesList;

    public NotesPage()
    {
        InitializeComponent();

        // Перенос старой одиночной заметки
        if (Notes.Count == 0)
        {
            Notes.Add(new NoteDoc());
            if (!string.IsNullOrWhiteSpace(Hub.Settings.Notes))
            {
                LoadPlain(Hub.Settings.Notes);
                _active = 0;
                SaveCurrent();
            }
        }
        if (_active >= Notes.Count) _active = 0;
        OpenNote(_active);

        Editor.TextChanged += (_, _) =>
        {
            UpdatePlaceholder();
            if (_loading) return;
            _deleteArmed = false;
            _save.Stop(); _save.Start();
        };
        Editor.PreviewMouseLeftButtonUp += ToggleCheckbox;
        _save.Tick += (_, _) => { _save.Stop(); SaveCurrent(); RenderTabs(); };

        BuildPresets();
        Ring.MouseLeftButtonUp += (_, _) => { if (Hub.Countdown.Idle) BeginEdit(); };
        TimeEdit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; }
        };
        TimeEdit.LostKeyboardFocus += (_, _) => EndEdit();

        RemInput.TextChanged += (_, _) => RemPlaceholder.Visibility = RemInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || RemInput.Text.Trim().Length == 0) return;
            var err = Hub.Reminders.Add(RemInput.Text);
            if (err != null) Hub.Toast(err, false);
            else { Hub.Toast("Напоминание добавлено"); RemInput.Clear(); }
            e.Handled = true;
        };

        Loaded += (_, _) =>
        {
            Hub.Countdown.Changed += RenderTimer;
            Hub.Reminders.Changed += RenderReminders;
            RenderTimer();
            RenderReminders();
        };
        Unloaded += (_, _) =>
        {
            Hub.Countdown.Changed -= RenderTimer;
            Hub.Reminders.Changed -= RenderReminders;
            if (_save.IsEnabled) { _save.Stop(); SaveCurrent(); }
        };
    }

    // ================= Заметки =================

    void OpenNote(int i)
    {
        _active = i;
        _loading = true;
        Editor.Document = new FlowDocument { PagePadding = new Thickness(0) };
        var xaml = Notes[i].Xaml;
        if (xaml.Length > 0)
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
                range.Load(ms, DataFormats.Xaml);
            }
            catch { }
        }
        _loading = false;
        _deleteArmed = false;
        UpdatePlaceholder();
        RenderTabs();
    }

    void LoadPlain(string text)
    {
        Editor.Document = new FlowDocument { PagePadding = new Thickness(0) };
        foreach (var line in text.Replace("\r", "").Split('\n'))
            Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
    }

    void SaveCurrent()
    {
        if (_active >= Notes.Count) return;
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        Notes[_active].Xaml = Encoding.UTF8.GetString(ms.ToArray());
        var first = range.Text.Replace("\r", "").Split('\n').Select(l => l.Trim(' ', '☐', '☑')).FirstOrDefault(l => l.Length > 0) ?? "";
        Notes[_active].Preview = first.Length > 14 ? first[..14] + "…" : first;
        Hub.Settings.Save();
    }

    void RenderTabs()
    {
        NoteTabs.Children.Clear();
        for (int i = 0; i < Notes.Count; i++)
        {
            int idx = i;
            var title = Notes[i].Preview.Length > 0 ? Notes[i].Preview : (Notes.Count == 1 ? "Заметка" : $"Заметка {i + 1}");
            var b = new Button
            {
                Style = (Style)FindResource("Tool"), Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 4, 0),
                Content = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Medium },
                Background = i == _active ? Ui.B("Fill3") : Brushes.Transparent,
                Foreground = Ui.B(i == _active ? "Label" : "Label2"),
            };
            b.Click += (_, _) =>
            {
                if (idx == _active) return;
                if (_save.IsEnabled) { _save.Stop(); SaveCurrent(); }
                OpenNote(idx);
            };
            NoteTabs.Children.Add(b);
        }
    }

    void UpdatePlaceholder()
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        Placeholder.Visibility = text.Trim().Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void AddNote_Click(object sender, RoutedEventArgs e)
    {
        if (_save.IsEnabled) { _save.Stop(); SaveCurrent(); }
        if (Notes.Count >= 6) { Hub.Toast("Не больше 6 заметок", false); return; }
        Notes.Add(new NoteDoc());
        Hub.Settings.Save();
        OpenNote(Notes.Count - 1);
        Editor.Focus();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteArmed) { _deleteArmed = true; Hub.Toast("Нажми ещё раз, чтобы удалить заметку", false); return; }
        _save.Stop();
        if (Notes.Count == 1) { Notes[0] = new NoteDoc(); OpenNote(0); }
        else { Notes.RemoveAt(_active); OpenNote(Math.Max(0, _active - 1)); }
        Hub.Settings.Save();
        Hub.Toast("Заметка удалена");
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.TrimEnd();
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); Hub.Toast("Текст скопирован"); } catch { }
    }

    void Exec(RoutedUICommand cmd) { Editor.Focus(); cmd.Execute(null, Editor); }
    void Bold_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleBold);
    void Italic_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleItalic);
    void Bullets_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleBullets);
    void Numbers_Click(object sender, RoutedEventArgs e) => Exec(EditingCommands.ToggleNumbering);

    void Strike_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        var cur = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        bool has = cur is TextDecorationCollection tdc && tdc.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }

    /// <summary>Чек-лист: ставит или убирает «☐ » в начале выделенных строк.</summary>
    void Check_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        var start = Editor.Selection.Start.Paragraph;
        var end = Editor.Selection.End.Paragraph;
        if (start == null) { Editor.CaretPosition.InsertTextInRun("☐ "); return; }
        for (var p = start; p != null; p = p.NextBlock as Paragraph)
        {
            var begin = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            var two = begin.GetPositionAtOffset(2);
            var head = two != null ? new TextRange(begin, two).Text : "";
            if (head is "☐ " or "☑ ") new TextRange(begin, two!).Text = "";
            else begin.InsertTextInRun("☐ ");
            if (p == end) break;
        }
    }

    /// <summary>Клик по квадратику отмечает пункт.</summary>
    void ToggleCheckbox(object sender, MouseButtonEventArgs e)
    {
        var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), false);
        if (pos == null) return;
        TextPointer? at = null;
        var fwd = pos.GetTextInRun(LogicalDirection.Forward);
        var back = pos.GetTextInRun(LogicalDirection.Backward);
        if (fwd.Length > 0 && fwd[0] is '☐' or '☑') at = pos;
        else if (back.Length > 0 && back[^1] is '☐' or '☑') at = pos.GetPositionAtOffset(-1);
        if (at == null) return;
        var end = at.GetPositionAtOffset(1);
        if (end == null) return;
        var r = new TextRange(at, end);
        bool check = r.Text == "☐";
        r.Text = check ? "☑" : "☐";
        // Отмеченный пункт делаем бледным
        if (at.Paragraph is Paragraph para)
            new TextRange(para.ContentStart, para.ContentEnd).ApplyPropertyValue(TextElement.ForegroundProperty, check ? Ui.B("Label3") : Ui.B("Label"));
    }

    /// <summary>«Напомнить»: выделенный текст или текущая строка уходят в поле напоминания.</summary>
    void Remind_Click(object sender, RoutedEventArgs e)
    {
        var text = Editor.Selection.Text.Trim();
        if (text.Length == 0 && Editor.CaretPosition.Paragraph is Paragraph p)
            text = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();
        text = text.TrimStart('☐', '☑', ' ');
        if (text.Length == 0) { Hub.Toast("Выдели строку, о которой напомнить", false); return; }
        RemInput.Text = text + " ";
        RemInput.Focus();
        RemInput.CaretIndex = RemInput.Text.Length;
        Hub.Toast("Допиши, когда напомнить, и нажми Enter");
    }

    // ================= Таймер =================

    void BuildPresets()
    {
        foreach (var m in new[] { 5, 10, 15, 25, 45 })
        {
            var b = new Button
            {
                Style = (Style)FindResource("Btn"), Content = new TextBlock { Text = m.ToString(), FontSize = 12, FontWeight = FontWeights.Medium },
                Width = 34, Height = 26, Margin = new Thickness(0, 0, 5, 0), Background = Ui.B("Fill2"), Tag = m,
            };
            Ui.SetRadius(b, new CornerRadius(7));
            b.Click += (_, _) => Hub.Countdown.Set(m);
            _presets.Add(b);
            Presets.Children.Add(b);
        }
        Presets.Children.Add(new TextBlock { Text = "мин", FontSize = 11, Foreground = Ui.B("Label3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.Countdown.Running) Hub.Countdown.Pause(); else Hub.Countdown.Start();
    }

    /// <summary>Вторая кнопка: «Своё время», пока таймер не начат, и «Сброс» — после.</summary>
    void Second_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.Countdown.Idle) BeginEdit(); else Hub.Countdown.Reset();
    }

    void BeginEdit()
    {
        TimeEdit.Text = Hub.Fmt(Hub.Countdown.Total);
        TimeEditBox.Visibility = Visibility.Visible;
        TimeEdit.Focus();
        TimeEdit.SelectAll();
    }

    void EndEdit() => TimeEditBox.Visibility = Visibility.Collapsed;

    void ApplyEdit()
    {
        var t = ParseTime(TimeEdit.Text);
        EndEdit();
        if (t == null) { Hub.Toast("Не понял время. Например: 7, 7:30 или 1:20:00", false); return; }
        Hub.Countdown.Set(t.Value);
    }

    /// <summary>«7» — минуты, «7:30» — минуты и секунды, «1:20:00» — часы, минуты, секунды.</summary>
    static TimeSpan? ParseTime(string s)
    {
        var parts = s.Trim().Replace('.', ':').Replace(',', ':').Replace(' ', ':')
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) return null;
        var n = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out n[i]) || n[i] < 0) return null;
        var t = parts.Length switch
        {
            1 => TimeSpan.FromMinutes(n[0]),
            2 => new TimeSpan(0, n[0], n[1]),
            _ => new TimeSpan(n[0], n[1], n[2]),
        };
        return t > TimeSpan.Zero && t <= TimeSpan.FromHours(24) ? t : null;
    }

    void RenderTimer()
    {
        var c = Hub.Countdown;
        TimeText.Text = Hub.Fmt(c.Left);
        TimeText.FontSize = c.Left.TotalHours >= 1 ? 13 : 17;
        Ring.Cursor = c.Idle ? Cursors.Hand : null;

        StartText.Text = c.Running ? "Пауза" : c.Idle ? "Старт" : "Дальше";
        StartIcon.Data = Ui.G(c.Running ? "f.pause" : "f.play");
        SecondText.Text = c.Idle ? "Своё время" : "Сброс";
        SecondIcon.Data = Ui.G(c.Idle ? "i.note" : "i.restart");

        foreach (var b in _presets)
        {
            bool on = c.Total == TimeSpan.FromMinutes((int)b.Tag);
            b.Background = Ui.B(on ? "Fill3" : "Fill2");
            b.Foreground = Ui.B(on ? "Label" : "Label2");
        }
        // Пока таймер идёт, быстрый выбор плавно прячется — место остаётся, ничего не прыгает
        if (c.Idle != _idleShown)
        {
            _idleShown = c.Idle;
            Presets.IsHitTestVisible = c.Idle;
            Presets.BeginAnimation(OpacityProperty, new DoubleAnimation(c.Idle ? 1 : 0, TimeSpan.FromMilliseconds(220)));
            if (!c.Idle) EndEdit();
        }
        DrawArc(c.Total.TotalSeconds > 0 ? c.Left.TotalSeconds / c.Total.TotalSeconds : 0);
    }

    void DrawArc(double frac)
    {
        frac = Math.Clamp(frac, 0, 1);
        const double cx = 38, cy = 38, r = 36;
        if (frac >= 0.999) { Arc.Data = new EllipseGeometry(new Point(cx, cy), r, r); return; }
        if (frac <= 0.001) { Arc.Data = null; return; }
        double a = frac * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        Arc.Data = g;
    }

    // ================= Напоминания =================

    void RenderReminders()
    {
        RemList.Children.Clear();
        RemEmpty.Visibility = Hub.Reminders.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in Hub.Reminders.Items)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = r.Text, FontSize = 13, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = r.WhenText(), FontSize = 11, Foreground = Ui.B("Orange") });
            g.Children.Add(sp);
            var x = new Button
            {
                Style = (Style)FindResource("Tool"), Height = 24, MinWidth = 24, Padding = new Thickness(0), ToolTip = "Удалить",
                Content = new Icon { Data = Ui.G("i.x"), Width = 11, Height = 11, Thickness = 2.4 },
            };
            x.Click += (_, _) => Hub.Reminders.Remove(r);
            Grid.SetColumn(x, 1);
            g.Children.Add(x);
            RemList.Children.Add(new Border
            {
                Background = Ui.B("Fill2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 6), Child = g,
            });
        }
    }
}
