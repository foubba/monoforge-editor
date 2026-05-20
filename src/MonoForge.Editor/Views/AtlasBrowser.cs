using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

public sealed class AtlasBrowser : UserControl
{
    public AtlasBrowser(string atlasJsonPath)
    {
        AtlasResult? result = null;
        string pngPath = "";
        try
        {
            var json = File.ReadAllText(atlasJsonPath);
            result = JsonSerializer.Deserialize<AtlasResult>(json);
            pngPath = Path.ChangeExtension(atlasJsonPath, ".png");
        }
        catch (Exception ex)
        {
            Content = Center("Atlas load failed: " + ex.Message);
            return;
        }

        if (result is null || !File.Exists(pngPath))
        {
            Content = Center("Atlas PNG not found next to " + Path.GetFileName(atlasJsonPath));
            return;
        }

        Bitmap atlas;
        try { atlas = new Bitmap(pngPath); }
        catch (Exception ex) { Content = Center("PNG load failed: " + ex.Message); return; }

        var header = new TextBlock
        {
            Text = $"Atlas: {Path.GetFileName(pngPath)}   {result.Width}×{result.Height}   {result.Regions.Count} regions   — drag a region to the scene canvas",
            Foreground = Brush(TextMuted),
            FontSize = 12,
            Padding = new Thickness(12, 8)
        };

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8)
        };

        foreach (var region in result.Regions)
        {
            wrap.Children.Add(BuildRegionThumb(atlas, region, pngPath));
        }

        var scroll = new ScrollViewer
        {
            Content = wrap,
            Background = Brush(EditorBackground)
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush(EditorBackground)
        };
        root.Children.Add(header);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }

    private static Control BuildRegionThumb(Bitmap atlas, AtlasRegion region, string pngPath)
    {
        var thumbSize = 96.0;
        var ratio = (double)region.W / Math.Max(1, region.H);
        double w, h;
        if (ratio >= 1) { w = thumbSize; h = thumbSize / ratio; }
        else { h = thumbSize; w = thumbSize * ratio; }

        var img = new Image
        {
            Source = atlas,
            Stretch = Stretch.Fill,
            Width = w,
            Height = h
        };

        // Crop visually using a Viewbox+RenderTransform? Simpler: use Border with clipped Image via Canvas.
        var clipCanvas = new Canvas
        {
            Width = w,
            Height = h,
            ClipToBounds = true
        };
        var inner = new Image
        {
            Source = atlas,
            Stretch = Stretch.None
        };
        var scaleX = w / region.W;
        var scaleY = h / region.H;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scaleX, scaleY));
        group.Children.Add(new TranslateTransform(-region.X * scaleX, -region.Y * scaleY));
        inner.RenderTransform = group;
        inner.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        clipCanvas.Children.Add(inner);

        var tile = new StackPanel
        {
            Spacing = 4,
            Width = thumbSize + 8,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        tile.Children.Add(new Border
        {
            Background = Brush("#1a1a1a"),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = new Border
            {
                Width = thumbSize,
                Height = thumbSize,
                Background = Brush("#0e0f12"),
                Child = clipCanvas
            }
        });
        tile.Children.Add(new TextBlock
        {
            Text = region.Name,
            Foreground = Brush(TextDivider),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = thumbSize + 8
        });
        tile.Children.Add(new TextBlock
        {
            Text = $"{region.W}×{region.H}",
            Foreground = Brush(TextDim),
            FontSize = 10,
            TextAlignment = TextAlignment.Center
        });

        // Drag source: same DataFormat as Assets tree, but with region payload
        Point dragStart = default;
        bool armed = false;
        tile.PointerPressed += (_, e) => { dragStart = e.GetPosition(tile); armed = true; };
        tile.PointerMoved += async (_, e) =>
        {
            if (!armed) return;
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) { armed = false; return; }
            var p = e.GetPosition(tile);
            if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 6) return;
            armed = false;

            var data = new DataObject();
            data.Set(Panels.AssetsPanel.AssetPathDataFormat, pngPath);
            data.Set("monoforge.atlas-region", $"{region.X},{region.Y},{region.W},{region.H}|{region.Name}");
            try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy); }
            catch { /* ignored */ }
        };
        tile.PointerReleased += (_, _) => armed = false;

        return tile;
    }

    private static Control Center(string message)
    {
        return new TextBlock
        {
            Text = message,
            Foreground = Brush(TextDim),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(20)
        };
    }
}
