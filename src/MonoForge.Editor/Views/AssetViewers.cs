using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

internal static class AssetViewers
{
    public static Control For(ProjectTreeNode node, PixelEditor pixelEditor, Action<string>? onPaletteColor)
    {
        if (node.IsDirectory)
        {
            return Directory(node);
        }

        if (!File.Exists(node.FullPath))
        {
            return Message("File not found", node.FullPath);
        }

        var extension = Path.GetExtension(node.Name).ToLowerInvariant();
        if (AssetIcon.IsImage(extension))
        {
            return Image(node.FullPath);
        }

        if (extension == ".sprite")
        {
            return SpriteEditor(node, pixelEditor, onPaletteColor);
        }

        if (extension is ".wav" or ".mp3" or ".ogg" or ".aiff" or ".flac")
        {
            return new AudioPreview(node.FullPath);
        }

        if (extension is ".glb" or ".gltf")
        {
            return new Model3DViewer(node.FullPath);
        }

        if (extension == ".json" && IsAtlasJson(node.FullPath))
        {
            return new AtlasBrowser(node.FullPath);
        }

        if (AssetIcon.IsTextLike(node.Name, extension))
        {
            return Code(node.FullPath);
        }

        return Message("No preview available", node.FullPath);
    }

    private static Control Directory(ProjectTreeNode node)
    {
        var panel = new StackPanel
        {
            Background = Brush(EditorBackground),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8
        };
        panel.Children.Add(new TextBlock { Text = "📁", FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = node.Name, Foreground = Brush(TextDivider), FontFamily = FontFamily.Parse(UiFont), FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = node.FullPath, Foreground = Brush(TextFaint), FontFamily = FontFamily.Parse("Menlo"), FontSize = 11 });
        return panel;
    }

    private static Control Image(string path)
    {
        try
        {
            return new Border
            {
                Background = Brush(EditorBackground),
                Child = new Avalonia.Controls.Image
                {
                    Source = new Bitmap(path),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(28)
                }
            };
        }
        catch (Exception ex)
        {
            return Message("Could not load image", ex.Message);
        }
    }

    private static bool IsAtlasJson(string path)
    {
        try
        {
            var twin = Path.ChangeExtension(path, ".png");
            if (!File.Exists(twin)) return false;
            var snippet = File.ReadAllText(path);
            return snippet.Contains("\"Regions\"") || snippet.Contains("\"regions\"");
        }
        catch { return false; }
    }

    private static Control Code(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return new CodeEditor(path, text);
        }
        catch (Exception ex)
        {
            return Message("Could not load file", ex.Message);
        }
    }

    private static Control SpriteEditor(ProjectTreeNode node, PixelEditor pixelEditor, Action<string>? onPaletteColor)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*"),
            Background = Brush(EditorBackground)
        };
        grid.Children.Add(new TextBlock
        {
            Text = "  " + node.Name,
            Foreground = Brush(TextDivider),
            Background = Brush(ConsoleBackground),
            FontFamily = FontFamily.Parse(UiFont),
            FontSize = 12,
            Padding = new Thickness(10, 8, 0, 0)
        }.At(row: 0));

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            Background = Brush(EditorBackground)
        };
        var tools = new StackPanel { Background = Brush(ConsoleBackground), Margin = new Thickness(18), Spacing = 8 };
        tools.Children.Add(Text("Sprite Editor", TextSecondary, FontWeight.Bold).WithMargin(10, 10, 10, 4));
        tools.Children.Add(Text("Palette", TextDim).WithMargin(10, 0, 10, 0));

        var palette = RowStack(ConsoleBackground);
        palette.Margin = new Thickness(10, 0, 10, 0);
        foreach (var color in new[] { "#65a7ff", "#7bd88f", "#ffd166", "#ff6b6b", "#d7dce5", "#15171b" })
        {
            palette.Children.Add(new Button
            {
                Width = 24, Height = 24,
                Background = Brush(color),
                BorderBrush = Brush(ObjectStroke),
                BorderThickness = new Thickness(1),
                Tag = color
            }.OnClick((sender, _) =>
            {
                if (sender is Button { Tag: string selected })
                {
                    onPaletteColor?.Invoke(selected);
                }
            }));
        }

        tools.Children.Add(palette);
        body.Children.Add(tools.At(column: 0));
        body.Children.Add(new Viewbox
        {
            Child = pixelEditor,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }.At(column: 1));
        grid.Children.Add(body.At(row: 1));
        return grid;
    }

    private static Control Message(string title, string detail)
    {
        var panel = new StackPanel
        {
            Background = Brush(EditorBackground),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8
        };
        panel.Children.Add(new TextBlock { Text = title, Foreground = Brush(TextDivider), FontFamily = FontFamily.Parse(UiFont), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = detail, Foreground = Brush(TextFaint), FontFamily = FontFamily.Parse("Menlo"), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 720 });
        return panel;
    }
}
