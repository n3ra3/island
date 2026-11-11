using System.Windows;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

public sealed class ClipItem
{
    public string Kind = "text"; // text | link | image
    public string Text = "";
    public BitmapSource? Image;
    public DateTime Time = DateTime.Now;
}

/// <summary>История буфера обмена: 30 последних записей, картинок не больше 2 (они тяжёлые).</summary>
public sealed class ClipboardHistory
{
    public readonly List<ClipItem> Items = new();
    public event Action? Changed;
    public event Action<BitmapSource>? ImageCopied;
    bool _ignoreNext;

    public void OnClipboardUpdate()
    {
        if (_ignoreNext) { _ignoreNext = false; return; }
        try
        {
            if (Clipboard.ContainsText())
            {
                var t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (t.Length > 4000) t = t[..4000];
                Items.RemoveAll(i => i.Image == null && i.Text == t);
                var link = Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
                Items.Insert(0, new ClipItem { Kind = link ? "link" : "text", Text = t });
            }
            else if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img == null) return;
                img.Freeze();
                Items.Insert(0, new ClipItem { Kind = "image", Text = $"Изображение {img.PixelWidth} × {img.PixelHeight}", Image = img });
                ImageCopied?.Invoke(img);
                int n = 0;
                for (int i = 0; i < Items.Count; i++)
                    if (Items[i].Image != null && ++n > 2) { Items.RemoveAt(i); i--; }
            }
            else return;

            if (Items.Count > 30) Items.RemoveRange(30, Items.Count - 30);
            Changed?.Invoke();
        }
        catch { }
    }

    public void Copy(ClipItem it)
    {
        try
        {
            _ignoreNext = true;
            if (it.Image != null) Clipboard.SetImage(it.Image);
            else Clipboard.SetText(it.Text);
            Items.Remove(it);
            it.Time = DateTime.Now;
            Items.Insert(0, it);
            Changed?.Invoke();
        }
        catch { _ignoreNext = false; }
    }

    public void Clear() { Items.Clear(); Changed?.Invoke(); }
}
