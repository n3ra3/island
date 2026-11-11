using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DynamicIsland;

public partial class App : Application
{
    Mutex? _mutex;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DynamicIsland.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += OnError;
        base.OnStartup(e);

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
        CreateTray();
    }

    void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "Dynamic Island", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть панель", null, (_, _) => Hub.Main?.OpenPanel());
        menu.Items.Add("Настройки", null, (_, _) => Hub.Main?.OpenPanel("set"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => Quit());
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) Hub.Main?.OpenPanel(); };
    }

    /// <summary>Значок для трея: белая «пилюля».</summary>
    static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new Drawing.Drawing2D.GraphicsPath();
            path.AddArc(2, 10, 12, 12, 90, 180);
            path.AddArc(18, 10, 12, 12, 270, 180);
            path.CloseFigure();
            g.FillPath(Drawing.Brushes.White, path);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public void Quit()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Hub.Main?.Cleanup();
        Hub.Settings.Save();
        Shutdown();
    }

    void OnError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:u}\n{e.Exception}\n\n");
        }
        catch { }
        e.Handled = true; // не падаем из-за мелочи
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hub.Main?.Cleanup();
        base.OnExit(e);
    }
}
