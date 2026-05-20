using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class SpritePickerDialog : Window
{
    public string? PickedPath { get; private set; }

    private readonly TextBox _filter = new();
    private readonly WrapPanel _grid = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
    private readonly List<string> _all;

    public SpritePickerDialog(string projectRoot, string? current)
    {
        Title = "Pick a Sprite";
        Width = 760; Height = 540;
        Background = Brush(MenuBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _all = EnumerateImages(projectRoot).ToList();

        _filter.Watermark = "Filter…";
        _filter.Background = Brush(InputBackground);
        _filter.Foreground = Brush(TextSecondary);
        _filter.BorderBrush = Brush(BorderColor);
        _filter.Padding = new Thickness(10, 6);
        _filter.Margin = new Thickness(12, 12, 12, 6);
        _filter.TextChanged += (_, _) => Rebuild();

        var clearBtn = MenuButton("Clear texture", (_, _) => { PickedPath = ""; Close(); });
        clearBtn.Margin = new Thickness(0, 0, 12, 0);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(_filter.At(column: 0));
        top.Children.Add(clearBtn.At(column: 1));

        var scroll = new ScrollViewer { Content = _grid, Background = Brush(EditorBackground) };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(top.At(row: 0));
        root.Children.Add(scroll.At(row: 1));
        Content = root;

        Opened += (_, _) => _filter.Focus();
        Rebuild();
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        var q = _filter.Text ?? "";
        IEnumerable<string> filtered = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(p => Path.GetFileName(p).Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var path in filtered.Take(400))
        {
            _grid.Children.Add(BuildThumb(path));
        }
    }

    private Control BuildThumb(string path)
    {
        var bmp = MonoForge.Editor.Services.TextureCache.Get(path);
        var inner = new Border
        {
            Width = 110,
            Height = 110,
            Background = Brush("#0e0f12"),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            Child = bmp is null
                ? new TextBlock { Text = "?", Foreground = Brush(TextDim), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : (Control)new Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(6) }
        };
        var label = new TextBlock
        {
            Text = Path.GetFileName(path),
            Foreground = Brush(TextDivider),
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 110
        };
        var tile = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(4),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        tile.Children.Add(inner);
        tile.Children.Add(label);
        tile.PointerPressed += (_, _) => { PickedPath = path; Close(); };
        return tile;
    }

    private static IEnumerable<string> EnumerateImages(string root)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", "packages" };
        return Walk(new DirectoryInfo(root), 0);

        IEnumerable<string> Walk(DirectoryInfo dir, int depth)
        {
            if (depth > 8) yield break;
            DirectoryInfo[] subs; FileInfo[] files;
            try { subs = dir.GetDirectories(); files = dir.GetFiles(); }
            catch { yield break; }
            foreach (var f in files)
            {
                var ext = f.Extension.ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
                    yield return f.FullName;
            }
            foreach (var d in subs)
            {
                if (d.Name.StartsWith('.') || skip.Contains(d.Name)) continue;
                foreach (var x in Walk(d, depth + 1)) yield return x;
            }
        }
    }
}
