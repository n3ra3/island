using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static DynamicIsland.Native;

namespace DynamicIsland;

public partial class ShelfPage : UserControl
{
    Point _dragStart;

    public ShelfPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        Drop.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            Frame.Stroke = Ui.B("Blue");
            e.Handled = true;
        };
        Drop.DragLeave += (_, _) => Frame.Stroke = Ui.B("Fill3");
        Drop.Drop += (_, e) =>
        {
            Frame.Stroke = Ui.B("Fill3");
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                int added = 0;
                foreach (var f in files) if (!Hub.Shelf.Contains(f)) { Hub.Shelf.Add(f); added++; }
                Hub.Settings.Save();
                Render();
                if (added > 0) Hub.Toast(added == 1 ? "Положил на полку" : $"Положил на полку: {added}");
            }
            e.Handled = true;
        };
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        Hub.Shelf.Clear();
        Hub.Settings.Save();
        Render();
    }

    void Render()
    {
        Items.Children.Clear();
        Hub.Shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        Empty.Visibility = Hub.Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = Hub.Shelf.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var path in Hub.Shelf)
        {
            var sp = new StackPanel { Width = 84 };
            sp.Children.Add(new Image { Source = SystemIcon(path), Width = 40, Height = 40, Margin = new Thickness(0, 4, 0, 6) });
            sp.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path.TrimEnd('\\')), FontSize = 11, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 28,
            });
            var tile = new Border
            {
                Background = Ui.B("Fill1"), CornerRadius = new CornerRadius(16), Padding = new Thickness(6, 8, 6, 8),
                Margin = new Thickness(0, 0, 8, 8), Child = sp, Cursor = Cursors.Hand,
                ToolTip = $"{path}\nПеретащи куда нужно · двойной клик — открыть · правый клик — убрать",
            };
            tile.MouseEnter += (_, _) => tile.Background = Ui.B("Fill2");
            tile.MouseLeave += (_, _) => tile.Background = Ui.B("Fill1");
            tile.MouseLeftButtonDown += (_, e) =>
            {
                _dragStart = e.GetPosition(this);
                if (e.ClickCount == 2)
                {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                    catch { Hub.Toast("Не получилось открыть", false); }
                }
            };
            tile.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var d = e.GetPosition(this) - _dragStart;
                if (Math.Abs(d.X) < 5 && Math.Abs(d.Y) < 5) return;
                DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { path }),
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            };
            tile.MouseRightButtonUp += (_, _) => { Hub.Shelf.Remove(path); Hub.Settings.Save(); Render(); };
            Items.Children.Add(tile);
        }
    }

    /// <summary>Настоящий значок файла или папки из Windows.</summary>
    static ImageSource? SystemIcon(string path)
    {
        var info = new SHFILEINFO();
        var r = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /*ICON*/ | 0x0 /*LARGE*/);
        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }
}
