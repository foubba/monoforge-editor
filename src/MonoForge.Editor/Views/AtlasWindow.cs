using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class AtlasWindow : Window
{
    private readonly List<string> _images = new();
    private readonly StackPanel _list = new() { Spacing = 4, Margin = new Thickness(10) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(10) };
    private readonly TextBlock _status = new() { Foreground = Brush(TextMuted), FontSize = 12, Padding = new Thickness(10, 6) };

    public AtlasWindow()
    {
        Title = "Atlas Builder";
        Width = 900; Height = 600;
        Background = Brush(EditorBackground);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Background = Brush(EditorBackground)
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        toolbar.Children.Add(MenuButton("Add images...", async (_, _) => await AddImages()));
        toolbar.Children.Add(MenuButton("Clear", (_, _) => { _images.Clear(); RebuildList(); }));
        toolbar.Children.Add(PrimaryButton("Pack → PNG + JSON", async (_, _) => await Pack()));
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar.At(row: 0, column: 0));

        var scroll = new ScrollViewer
        {
            Content = _list,
            Background = Brush(PanelBackground)
        };
        root.Children.Add(scroll.At(row: 1, column: 0));

        var previewHost = new Border
        {
            Background = Brush("#1a1a1a"),
            Child = _preview
        };
        root.Children.Add(previewHost.At(row: 1, column: 1));

        Grid.SetColumnSpan(_status, 2);
        root.Children.Add(_status.At(row: 2, column: 0));

        Content = root;
        RebuildList();
    }

    private async Task AddImages()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }]
        });
        foreach (var f in files)
        {
            var path = f.Path.LocalPath;
            if (!_images.Contains(path)) _images.Add(path);
        }
        RebuildList();
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        if (_images.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "No images yet — click \"Add images...\"",
                Foreground = Brush(TextDim),
                FontSize = 12
            });
            _status.Text = "0 images";
            return;
        }

        foreach (var path in _images)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = Brush(TextDivider),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }.At(column: 0));
            var rm = new Button
            {
                Content = "×",
                Background = Avalonia.Media.Brushes.Transparent,
                Foreground = Brush(TextDim),
                FontSize = 14,
                BorderBrush = Avalonia.Media.Brushes.Transparent,
                Tag = path
            };
            rm.Click += (_, _) => { _images.Remove(path); RebuildList(); };
            row.Children.Add(rm.At(column: 1));
            _list.Children.Add(row);
        }
        _status.Text = $"{_images.Count} images";
    }

    private async Task Pack()
    {
        if (_images.Count == 0)
        {
            _status.Text = "Add at least one image first";
            return;
        }
        var outFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "atlas.png",
            FileTypeChoices = [new FilePickerFileType("Atlas PNG") { Patterns = ["*.png"] }]
        });
        if (outFile is null) return;

        var pngPath = outFile.Path.LocalPath;
        var jsonPath = Path.ChangeExtension(pngPath, ".json");
        try
        {
            var result = AtlasPacker.Pack(_images, pngPath, jsonPath);
            _preview.Source = new Bitmap(pngPath);
            _status.Text = $"Packed {result.Regions.Count} regions into {result.Width}×{result.Height} → {Path.GetFileName(pngPath)}";
        }
        catch (Exception ex)
        {
            _status.Text = "Pack failed: " + ex.Message;
        }
    }
}
