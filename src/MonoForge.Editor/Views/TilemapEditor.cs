using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class TilemapEditor : Window
{
    private Tilemap _map = Tilemap.Empty(32, 24, 16);
    private Bitmap? _tileset;
    private int _activeTile = 0;
    private string _tool = "paint"; // paint | erase | fill
    private readonly TileGridCanvas _canvas;
    private readonly TilePaletteCanvas _palette;
    private readonly TextBlock _status = new() { Foreground = Brush(TextMuted), FontSize = 12, Padding = new Thickness(10, 4) };
    private string? _filePath;

    public TilemapEditor()
    {
        Title = "Tilemap Editor";
        Width = 1100; Height = 720;
        Background = Brush(EditorBackground);

        _canvas = new TileGridCanvas(this);
        _palette = new TilePaletteCanvas(this);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,260"),
            Background = Brush(EditorBackground)
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        toolbar.Children.Add(MenuButton("Load Tileset...", async (_, _) => await LoadTileset()));
        toolbar.Children.Add(MenuButton("New Map", (_, _) => NewMap()));
        toolbar.Children.Add(MenuButton("Open...", async (_, _) => await Open()));
        toolbar.Children.Add(MenuButton("Save...", async (_, _) => await Save()));
        toolbar.Children.Add(ToolBtn("Paint  (B)", "paint"));
        toolbar.Children.Add(ToolBtn("Erase  (E)", "erase"));
        toolbar.Children.Add(ToolBtn("Fill  (F)", "fill"));
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar.At(row: 0, column: 0));

        root.Children.Add(_canvas.At(row: 1, column: 0));
        root.Children.Add(new ScrollViewer
        {
            Content = _palette,
            Background = Brush(PanelBackground)
        }.At(row: 1, column: 1));

        Grid.SetColumnSpan(_status, 2);
        root.Children.Add(_status.At(row: 2, column: 0));
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.B) _tool = "paint";
            else if (e.Key == Key.E) _tool = "erase";
            else if (e.Key == Key.F) _tool = "fill";
            UpdateStatus();
        };

        UpdateStatus();
    }

    public Tilemap Map => _map;
    public Bitmap? Tileset => _tileset;
    public int ActiveTile { get => _activeTile; set { _activeTile = value; _palette.InvalidateVisual(); } }
    public string Tool => _tool;

    public void UpdateStatus()
    {
        _status.Text = $"{_map.Width}×{_map.Height} @ {_map.TileSize}px    tool: {_tool}    tile: {_activeTile}";
        _canvas.InvalidateVisual();
    }

    private Button ToolBtn(string label, string tool)
    {
        var b = MenuButton(label, (_, _) => { _tool = tool; UpdateStatus(); });
        return b;
    }

    private async Task LoadTileset()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Image") { Patterns = ["*.png", "*.jpg"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            _tileset = new Bitmap(f.Path.LocalPath);
            _map.TilesetPath = f.Path.LocalPath;
            _palette.InvalidateVisual();
            _canvas.InvalidateVisual();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _status.Text = "Load failed: " + ex.Message;
        }
    }

    private void NewMap()
    {
        _map = Tilemap.Empty(_map.Width, _map.Height, _map.TileSize);
        if (_tileset is not null) _map.TilesetPath = _tileset is null ? "" : _map.TilesetPath;
        UpdateStatus();
    }

    private async Task Save()
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "level.tilemap.json",
            FileTypeChoices = [new FilePickerFileType("Tilemap JSON") { Patterns = ["*.json"] }]
        });
        if (f is null) return;
        _filePath = f.Path.LocalPath;
        File.WriteAllText(_filePath, System.Text.Json.JsonSerializer.Serialize(_map, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        _status.Text = "Saved " + Path.GetFileName(_filePath);
    }

    private async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Tilemap JSON") { Patterns = ["*.json"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            var json = File.ReadAllText(f.Path.LocalPath);
            var map = System.Text.Json.JsonSerializer.Deserialize<Tilemap>(json);
            if (map is null) return;
            _map = map;
            _filePath = f.Path.LocalPath;
            if (!string.IsNullOrEmpty(_map.TilesetPath) && File.Exists(_map.TilesetPath))
            {
                _tileset = new Bitmap(_map.TilesetPath);
                _palette.InvalidateVisual();
            }
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _status.Text = "Open failed: " + ex.Message;
        }
    }
}

internal sealed class TileGridCanvas : Control
{
    private readonly TilemapEditor _host;
    private const double Cell = 24;
    private bool _painting;

    public TileGridCanvas(TilemapEditor host)
    {
        _host = host;
        ClipToBounds = true;
        Focusable = true;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Avalonia.Media.Brush.Parse("#111316"), bounds);

        var map = _host.Map;
        var ts = _host.Tileset;
        var tileSrc = map.TileSize;
        var tilesPerRow = ts is null ? 1 : Math.Max(1, ts.PixelSize.Width / Math.Max(1, tileSrc));

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var idx = map.Tiles[y * map.Width + x];
                var dst = new Rect(x * Cell, y * Cell, Cell, Cell);
                if (idx >= 0 && ts is not null)
                {
                    var sx = (idx % tilesPerRow) * tileSrc;
                    var sy = (idx / tilesPerRow) * tileSrc;
                    context.DrawImage(ts, new Rect(sx, sy, tileSrc, tileSrc), dst);
                }
            }
        }

        var pen = new Pen(Avalonia.Media.Brush.Parse("#252a33"), 1);
        for (var x = 0; x <= map.Width; x++)
            context.DrawLine(pen, new Point(x * Cell, 0), new Point(x * Cell, map.Height * Cell));
        for (var y = 0; y <= map.Height; y++)
            context.DrawLine(pen, new Point(0, y * Cell), new Point(map.Width * Cell, y * Cell));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _painting = true;
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_painting) return;
        Apply(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _painting = false;
        e.Pointer.Capture(null);
    }

    private void Apply(Point p)
    {
        var map = _host.Map;
        var x = (int)(p.X / Cell);
        var y = (int)(p.Y / Cell);
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return;

        switch (_host.Tool)
        {
            case "paint":
                map.Tiles[y * map.Width + x] = _host.ActiveTile;
                break;
            case "erase":
                map.Tiles[y * map.Width + x] = -1;
                break;
            case "fill":
                FloodFill(map, x, y, map.Tiles[y * map.Width + x], _host.ActiveTile);
                _painting = false;
                break;
        }
        _host.UpdateStatus();
    }

    private static void FloodFill(Tilemap map, int x0, int y0, int target, int replace)
    {
        if (target == replace) return;
        var stack = new Stack<(int, int)>();
        stack.Push((x0, y0));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) continue;
            if (map.Tiles[y * map.Width + x] != target) continue;
            map.Tiles[y * map.Width + x] = replace;
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }
}

internal sealed class TilePaletteCanvas : Control
{
    private readonly TilemapEditor _host;
    private const double Cell = 28;

    public TilePaletteCanvas(TilemapEditor host)
    {
        _host = host;
        ClipToBounds = false;
        Focusable = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ts = _host.Tileset;
        if (ts is null) return new Size(240, 200);
        var tile = _host.Map.TileSize;
        var perRow = Math.Max(1, ts.PixelSize.Width / Math.Max(1, tile));
        var rows = (int)Math.Ceiling((double)(ts.PixelSize.Height / Math.Max(1, tile)));
        return new Size(perRow * Cell + 8, rows * Cell + 8);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Avalonia.Media.Brush.Parse("#202329"), Bounds);
        var ts = _host.Tileset;
        if (ts is null)
        {
            var ft = new FormattedText("Load a tileset (PNG) to start.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Avalonia.Media.Brush.Parse("#8e96a3"));
            context.DrawText(ft, new Point(10, 10));
            return;
        }

        var tile = _host.Map.TileSize;
        var perRow = Math.Max(1, ts.PixelSize.Width / Math.Max(1, tile));
        var total = (ts.PixelSize.Width / Math.Max(1, tile)) * (ts.PixelSize.Height / Math.Max(1, tile));
        for (var i = 0; i < total; i++)
        {
            var col = i % perRow;
            var row = i / perRow;
            var dst = new Rect(4 + col * Cell, 4 + row * Cell, Cell, Cell);
            var sx = col * tile;
            var sy = row * tile;
            context.DrawImage(ts, new Rect(sx, sy, tile, tile), dst);
            if (i == _host.ActiveTile)
            {
                context.DrawRectangle(new Pen(Avalonia.Media.Brush.Parse("#ffffff"), 2), dst);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var ts = _host.Tileset;
        if (ts is null) return;
        var p = e.GetPosition(this);
        var col = (int)((p.X - 4) / Cell);
        var row = (int)((p.Y - 4) / Cell);
        if (col < 0 || row < 0) return;
        var perRow = Math.Max(1, ts.PixelSize.Width / Math.Max(1, _host.Map.TileSize));
        var idx = row * perRow + col;
        _host.ActiveTile = idx;
        _host.UpdateStatus();
    }
}
