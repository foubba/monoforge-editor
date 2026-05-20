using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

public sealed class SceneCanvas : Control
{
    private SceneObject? _dragObject;
    private Point _dragOffset;
    private bool _isPanning;
    private Point _panStart;
    private Vector _panCameraStart;

    private bool _marqueeActive;
    private Point _marqueeStartScreen;
    private Point _marqueeCurrentScreen;
    private readonly Dictionary<string, (double X, double Y)> _groupDragOffsets = new();
    private SceneObject? _rotatingObject;
    private double _rotateStartAngle;
    private double _rotateStartObjectRotation;
    private readonly List<double> _snapGuidesX = new();
    private readonly List<double> _snapGuidesY = new();
    public bool SnapToObjects { get; set; } = true;
    public double SnapThreshold { get; set; } = 6;
    public bool PixelPerfect { get; set; }
    public event Action<Point>? CursorWorldChanged;

    public SceneDocument Scene { get; set; } = new();
    public string? SelectedId
    {
        get => SelectedIds.LastOrDefault();
        set
        {
            SelectedIds.Clear();
            if (value is not null) SelectedIds.Add(value);
        }
    }
    public HashSet<string> SelectedIds { get; } = new();
    public string Tool { get; set; } = "select";
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public int SnapSize { get; set; } = 4;
    public double Zoom { get; set; } = 1;
    public Vector Camera { get; set; } = new(90, 78);

    public event Action<string?>? SelectionChanged;
    public event Action? ObjectChanged;
    public event Action<Point>? AddSpriteRequested;
    public event Action? DragCompleted;

    public event Action<string, Point>? AssetDropped;
    public event Action<string, Point, int, int, int, int, string>? AtlasRegionDropped;

    public event Action? ContextDuplicate;
    public event Action? ContextDelete;
    public event Action? ContextFrame;

    private static readonly Avalonia.Threading.DispatcherTimer AnimationTicker = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private static double _animationTime;
    private static readonly Dictionary<string, MonoForge.Editor.Models.AnimationClip?> AnimationCache = new();
    public static event Action? AnimationFrameRequested;

    static SceneCanvas()
    {
        AnimationTicker.Tick += (_, _) =>
        {
            _animationTime += AnimationTicker.Interval.TotalSeconds;
            AnimationFrameRequested?.Invoke();
        };
        AnimationTicker.Start();
    }

    public SceneCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AnimationFrameRequested += InvalidateVisual;

        var menu = new ContextMenu();
        var dup = new MenuItem { Header = "Duplicate   ⌘D" };
        dup.Click += (_, _) => ContextDuplicate?.Invoke();
        var del = new MenuItem { Header = "Delete   ⌫" };
        del.Click += (_, _) => ContextDelete?.Invoke();
        var frame = new MenuItem { Header = "Frame view   F" };
        frame.Click += (_, _) => ContextFrame?.Invoke();
        menu.Items.Add(dup);
        menu.Items.Add(del);
        menu.Items.Add(new Separator());
        menu.Items.Add(frame);
        ContextMenu = menu;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("monoforge.asset-path") || e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        string? path = null;
        if (e.Data.Get("monoforge.asset-path") is string s)
        {
            path = s;
        }
        else if (e.Data.GetFiles() is { } files)
        {
            path = files.Select(f => f.Path.LocalPath).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(path)) return;
        var screen = e.GetPosition(this);
        var world = ScreenToWorld(screen);

        if (e.Data.Get("monoforge.atlas-region") is string regionData)
        {
            var parts = regionData.Split('|');
            var coords = parts[0].Split(',');
            if (coords.Length == 4
                && int.TryParse(coords[0], out var rx)
                && int.TryParse(coords[1], out var ry)
                && int.TryParse(coords[2], out var rw)
                && int.TryParse(coords[3], out var rh))
            {
                var name = parts.Length > 1 ? parts[1] : "region";
                AtlasRegionDropped?.Invoke(path, world, rx, ry, rw, rh, name);
                e.Handled = true;
                return;
            }
        }

        AssetDropped?.Invoke(path, world);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Brush(CanvasBackground), bounds);
        if (ShowGrid)
        {
            DrawGrid(context, bounds);
        }

        foreach (var item in Scene.Flatten().Where(o => o.Visible).OrderBy(o => o.Layer))
        {
            DrawObject(context, item);
        }

        if (_marqueeActive)
        {
            DrawMarquee(context);
        }

        DrawSnapGuides(context, bounds);
        DrawHud(context, bounds);
    }

    private record AnimFrame(Avalonia.Media.Imaging.Bitmap Bitmap, Rect Src);

    private static AnimFrame? TryGetAnimationFrame(MonoForge.Editor.Models.ComponentData comp)
    {
        if (!AnimationCache.TryGetValue(comp.Source, out var clip))
        {
            try { clip = System.Text.Json.JsonSerializer.Deserialize<MonoForge.Editor.Models.AnimationClip>(File.ReadAllText(comp.Source)); }
            catch { clip = null; }
            AnimationCache[comp.Source] = clip;
        }
        if (clip is null || clip.Frames.Count == 0) return null;
        var sheet = MonoForge.Editor.Services.TextureCache.Get(clip.TexturePath);
        if (sheet is null) return null;
        var speed = comp.Properties.TryGetValue("Speed", out var s) && double.TryParse(s, out var sp) ? sp : 1.0;
        var fps = Math.Max(0.001, clip.Fps * speed);
        var idx = (int)Math.Floor(_animationTime * fps) % clip.Frames.Count;
        var frame = clip.Frames[idx];
        var perRow = Math.Max(1, sheet.PixelSize.Width / Math.Max(1, clip.FrameWidth));
        var sx = (frame % perRow) * clip.FrameWidth;
        var sy = (frame / perRow) * clip.FrameHeight;
        return new AnimFrame(sheet, new Rect(sx, sy, clip.FrameWidth, clip.FrameHeight));
    }

    private static readonly Dictionary<string, MonoForge.Editor.Models.Tilemap?> TilemapCache = new();

    private void DrawTilemap(DrawingContext context, SceneObject item, Point topLeft)
    {
        if (!TilemapCache.TryGetValue(item.TilemapPath, out var map))
        {
            try
            {
                var json = File.ReadAllText(item.TilemapPath);
                map = System.Text.Json.JsonSerializer.Deserialize<MonoForge.Editor.Models.Tilemap>(json);
            }
            catch { map = null; }
            TilemapCache[item.TilemapPath] = map;
        }
        if (map is null) return;

        var sheet = MonoForge.Editor.Services.TextureCache.Get(map.TilesetPath);
        var tilesPerRow = sheet is null ? 1 : Math.Max(1, sheet.PixelSize.Width / Math.Max(1, map.TileSize));
        var cellW = (item.Width / Math.Max(1, map.Width)) * Zoom;
        var cellH = (item.Height / Math.Max(1, map.Height)) * Zoom;

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var idx = map.Tiles[y * map.Width + x];
                if (idx < 0) continue;
                var dst = new Rect(topLeft.X + x * cellW, topLeft.Y + y * cellH, cellW, cellH);
                if (sheet is not null)
                {
                    var sx = (idx % tilesPerRow) * map.TileSize;
                    var sy = (idx / tilesPerRow) * map.TileSize;
                    context.DrawImage(sheet, new Rect(sx, sy, map.TileSize, map.TileSize), dst);
                }
                else
                {
                    context.FillRectangle(Brush("#5d8cd7"), dst);
                }
            }
        }
    }

    private void DrawSnapGuides(DrawingContext context, Rect bounds)
    {
        if (_snapGuidesX.Count == 0 && _snapGuidesY.Count == 0) return;
        var pen = new Pen(Brush("#42b7ff"), 1, dashStyle: new DashStyle(new[] { 4.0, 3.0 }, 0));
        foreach (var wx in _snapGuidesX)
        {
            var sx = wx * Zoom + Camera.X;
            context.DrawLine(pen, new Point(sx, 0), new Point(sx, bounds.Height));
        }
        foreach (var wy in _snapGuidesY)
        {
            var sy = wy * Zoom + Camera.Y;
            context.DrawLine(pen, new Point(0, sy), new Point(bounds.Width, sy));
        }
    }

    private void DrawMarquee(DrawingContext context)
    {
        var x1 = Math.Min(_marqueeStartScreen.X, _marqueeCurrentScreen.X);
        var y1 = Math.Min(_marqueeStartScreen.Y, _marqueeCurrentScreen.Y);
        var x2 = Math.Max(_marqueeStartScreen.X, _marqueeCurrentScreen.X);
        var y2 = Math.Max(_marqueeStartScreen.Y, _marqueeCurrentScreen.Y);
        var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
        context.FillRectangle(Brush("#1f3553aa"), rect);
        context.DrawRectangle(new Pen(Brush("#5d8cd7"), 1, dashStyle: new DashStyle(new[] { 4.0, 2.0 }, 0)), rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            _isPanning = true;
            _panStart = point;
            _panCameraStart = Camera;
            e.Pointer.Capture(this);
            return;
        }

        var world = ScreenToWorld(point);

        // Rotation handle hit-test (only on single-selected object)
        if (SelectedId is { } selId && SelectedIds.Count == 1 && Scene.Find(selId) is { } selObj && !selObj.Locked)
        {
            var rect = new Rect(selObj.X * Zoom + Camera.X, selObj.Y * Zoom + Camera.Y, selObj.Width * Zoom, selObj.Height * Zoom);
            var pivotX = rect.Left + rect.Width * selObj.PivotX;
            var pivotY = rect.Top + rect.Height * selObj.PivotY;
            var handle = ComputeRotationHandle(selObj, rect, pivotX, pivotY);
            if (Math.Abs(point.X - handle.X) < 8 && Math.Abs(point.Y - handle.Y) < 8)
            {
                _rotatingObject = selObj;
                _rotateStartAngle = Math.Atan2(point.Y - pivotY, point.X - pivotX);
                _rotateStartObjectRotation = selObj.Rotation;
                e.Pointer.Capture(this);
                return;
            }
        }

        if (Tool == "rect")
        {
            AddSpriteRequested?.Invoke(world);
            return;
        }

        var hit = Scene.Flatten()
            .Where(o => o.Visible && !o.Locked)
            .OrderByDescending(o => o.Layer)
            .FirstOrDefault(o => world.X >= o.X && world.Y >= o.Y && world.X <= o.X + o.Width && world.Y <= o.Y + o.Height);

        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (hit is null)
        {
            // Empty area: start marquee selection
            if (!additive) SelectedIds.Clear();
            _marqueeActive = true;
            _marqueeStartScreen = point;
            _marqueeCurrentScreen = point;
            e.Pointer.Capture(this);
            SelectionChanged?.Invoke(SelectedId);
            InvalidateVisual();
            return;
        }

        if (additive)
        {
            if (!SelectedIds.Add(hit.Id)) SelectedIds.Remove(hit.Id);
        }
        else
        {
            if (!SelectedIds.Contains(hit.Id))
            {
                SelectedIds.Clear();
                SelectedIds.Add(hit.Id);
            }
        }

        _dragObject = hit;
        _dragOffset = new Point(world.X - hit.X, world.Y - hit.Y);
        _groupDragOffsets.Clear();
        foreach (var id in SelectedIds)
        {
            if (Scene.Find(id) is { } obj)
            {
                _groupDragOffsets[id] = (obj.X - hit.X, obj.Y - hit.Y);
            }
        }
        e.Pointer.Capture(this);

        SelectionChanged?.Invoke(SelectedId);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        CursorWorldChanged?.Invoke(ScreenToWorld(point));
        if (_isPanning)
        {
            Camera = _panCameraStart + (point - _panStart);
            InvalidateVisual();
            return;
        }

        if (_marqueeActive)
        {
            _marqueeCurrentScreen = point;
            InvalidateVisual();
            return;
        }

        if (_rotatingObject is not null)
        {
            var rect = new Rect(_rotatingObject.X * Zoom + Camera.X, _rotatingObject.Y * Zoom + Camera.Y, _rotatingObject.Width * Zoom, _rotatingObject.Height * Zoom);
            var pivotX = rect.Left + rect.Width * _rotatingObject.PivotX;
            var pivotY = rect.Top + rect.Height * _rotatingObject.PivotY;
            var currentAngle = Math.Atan2(point.Y - pivotY, point.X - pivotX);
            var degDelta = (currentAngle - _rotateStartAngle) * 180.0 / Math.PI;
            var newRot = _rotateStartObjectRotation + degDelta;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) newRot = Math.Round(newRot / 15) * 15;
            _rotatingObject.Rotation = newRot;
            ObjectChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        if (_dragObject is null)
        {
            return;
        }

        var world = ScreenToWorld(point);
        var anchorX = world.X - _dragOffset.X;
        var anchorY = world.Y - _dragOffset.Y;
        if (SnapToGrid && SnapSize > 0)
        {
            anchorX = Math.Round(anchorX / SnapSize) * SnapSize;
            anchorY = Math.Round(anchorY / SnapSize) * SnapSize;
        }
        if (PixelPerfect)
        {
            anchorX = Math.Round(anchorX);
            anchorY = Math.Round(anchorY);
        }

        // Snap to other objects' edges and centers
        _snapGuidesX.Clear();
        _snapGuidesY.Clear();
        if (SnapToObjects && _dragObject is not null)
        {
            var thr = SnapThreshold / Math.Max(0.01, Zoom);
            var dragLeft = anchorX;
            var dragRight = anchorX + _dragObject.Width;
            var dragCenterX = anchorX + _dragObject.Width / 2;
            var dragTop = anchorY;
            var dragBottom = anchorY + _dragObject.Height;
            var dragCenterY = anchorY + _dragObject.Height / 2;

            double? bestDx = null, bestDy = null;
            double bestAdx = thr, bestAdy = thr;

            foreach (var other in Scene.Flatten())
            {
                if (!other.Visible || _groupDragOffsets.ContainsKey(other.Id)) continue;
                var oLeft = other.X;
                var oRight = other.X + other.Width;
                var oCenterX = other.X + other.Width / 2;
                var oTop = other.Y;
                var oBottom = other.Y + other.Height;
                var oCenterY = other.Y + other.Height / 2;

                foreach (var (targetX, dragRefX) in new[] {
                    (oLeft, dragLeft), (oLeft, dragRight), (oLeft, dragCenterX),
                    (oRight, dragLeft), (oRight, dragRight), (oRight, dragCenterX),
                    (oCenterX, dragLeft), (oCenterX, dragRight), (oCenterX, dragCenterX)
                })
                {
                    var diff = targetX - dragRefX;
                    if (Math.Abs(diff) < bestAdx)
                    {
                        bestAdx = Math.Abs(diff);
                        bestDx = diff;
                    }
                }
                foreach (var (targetY, dragRefY) in new[] {
                    (oTop, dragTop), (oTop, dragBottom), (oTop, dragCenterY),
                    (oBottom, dragTop), (oBottom, dragBottom), (oBottom, dragCenterY),
                    (oCenterY, dragTop), (oCenterY, dragBottom), (oCenterY, dragCenterY)
                })
                {
                    var diff = targetY - dragRefY;
                    if (Math.Abs(diff) < bestAdy)
                    {
                        bestAdy = Math.Abs(diff);
                        bestDy = diff;
                    }
                }
            }

            if (bestDx is { } dx) { anchorX += dx; _snapGuidesX.Add(anchorX); _snapGuidesX.Add(anchorX + _dragObject.Width); _snapGuidesX.Add(anchorX + _dragObject.Width / 2); }
            if (bestDy is { } dy) { anchorY += dy; _snapGuidesY.Add(anchorY); _snapGuidesY.Add(anchorY + _dragObject.Height); _snapGuidesY.Add(anchorY + _dragObject.Height / 2); }
        }

        foreach (var kv in _groupDragOffsets)
        {
            if (Scene.Find(kv.Key) is { } obj)
            {
                obj.X = anchorX + kv.Value.X;
                obj.Y = anchorY + kv.Value.Y;
            }
        }
        ObjectChanged?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var wasDragging = _dragObject is not null;

        if (_marqueeActive)
        {
            var startW = ScreenToWorld(_marqueeStartScreen);
            var endW = ScreenToWorld(_marqueeCurrentScreen);
            var x1 = Math.Min(startW.X, endW.X);
            var y1 = Math.Min(startW.Y, endW.Y);
            var x2 = Math.Max(startW.X, endW.X);
            var y2 = Math.Max(startW.Y, endW.Y);

            if (Math.Abs(x2 - x1) > 2 || Math.Abs(y2 - y1) > 2)
            {
                foreach (var o in Scene.Flatten().Where(o => o.Visible))
                {
                    if (o.X >= x1 && o.Y >= y1 && o.X + o.Width <= x2 && o.Y + o.Height <= y2)
                    {
                        SelectedIds.Add(o.Id);
                    }
                }
            }
            _marqueeActive = false;
            SelectionChanged?.Invoke(SelectedId);
        }

        var wasRotating = _rotatingObject is not null;
        _dragObject = null;
        _rotatingObject = null;
        _isPanning = false;
        _groupDragOffsets.Clear();
        _snapGuidesX.Clear();
        _snapGuidesY.Clear();
        e.Pointer.Capture(null);
        if (wasDragging || wasRotating) DragCompleted?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var point = e.GetPosition(this);
        var worldBefore = ScreenToWorld(point);
        var factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
        Zoom = Math.Clamp(Zoom * factor, 0.2, 4.0);
        var worldAfter = ScreenToWorld(point);
        Camera = new Vector(
            Camera.X + (worldAfter.X - worldBefore.X) * Zoom,
            Camera.Y + (worldAfter.Y - worldBefore.Y) * Zoom);
        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var snapWorld = Math.Max(1, SnapSize);
        var minorWorld = Math.Max(8.0, snapWorld * 2.0);
        var majorWorld = minorWorld * 4.0;

        var minorPx = minorWorld * Zoom;
        var majorPx = majorWorld * Zoom;

        // If grid is too tight, only draw major
        var minorPen = new Pen(Brush(GridLine), 1);
        var majorPen = new Pen(Brush("#2e3640"), 1);

        if (minorPx >= 8)
        {
            for (var x = Camera.X % minorPx; x < bounds.Width; x += minorPx)
                context.DrawLine(minorPen, new Point(x, 0), new Point(x, bounds.Height));
            for (var y = Camera.Y % minorPx; y < bounds.Height; y += minorPx)
                context.DrawLine(minorPen, new Point(0, y), new Point(bounds.Width, y));
        }

        if (majorPx >= 16)
        {
            for (var x = Camera.X % majorPx; x < bounds.Width; x += majorPx)
                context.DrawLine(majorPen, new Point(x, 0), new Point(x, bounds.Height));
            for (var y = Camera.Y % majorPx; y < bounds.Height; y += majorPx)
                context.DrawLine(majorPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawObject(DrawingContext context, SceneObject item)
    {
        var topLeft = WorldToScreen(new Point(item.X, item.Y));
        var rect = new Rect(topLeft.X, topLeft.Y, item.Width * Zoom, item.Height * Zoom);

        // Apply transform around pivot (rotation + flip)
        var pivotX = rect.Left + rect.Width * item.PivotX;
        var pivotY = rect.Top + rect.Height * item.PivotY;
        var needsTransform = item.Rotation != 0 || item.FlipX || item.FlipY;
        IDisposable? pushed = null;
        if (needsTransform)
        {
            var m = Matrix.CreateTranslation(-pivotX, -pivotY);
            if (item.FlipX) m *= Matrix.CreateScale(-1, 1);
            if (item.FlipY) m *= Matrix.CreateScale(1, -1);
            if (item.Rotation != 0) m *= Matrix.CreateRotation(item.Rotation * Math.PI / 180.0);
            m *= Matrix.CreateTranslation(pivotX, pivotY);
            pushed = context.PushTransform(m);
        }

        var animComp = item.Components.FirstOrDefault(c => c.Kind == "Animation" && !string.IsNullOrEmpty(c.Source));
        var animFrame = animComp is not null ? TryGetAnimationFrame(animComp) : null;

        if (item.Type == "Model3D")
        {
            DrawModel3DProxy(context, item, rect);
        }
        else if (item.Type == "Tilemap" && !string.IsNullOrEmpty(item.TilemapPath))
        {
            DrawTilemap(context, item, topLeft);
        }
        else if (animFrame is { Bitmap: { } animBmp, Src: { } animSrc })
        {
            context.DrawImage(animBmp, animSrc, rect);
        }
        else
        {
            var bitmap = MonoForge.Editor.Services.TextureCache.Get(item.TexturePath);
            if (bitmap is not null)
            {
                var src = item.SourceW > 0 && item.SourceH > 0
                    ? new Rect(item.SourceX, item.SourceY, item.SourceW, item.SourceH)
                    : new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                context.DrawImage(bitmap, src, rect);
            }
            else
            {
                IBrush fill;
                try { fill = Brush(item.Color); }
                catch { fill = Brush("#65a7ff"); }
                context.FillRectangle(fill, rect, item.Type == "Marker" ? 0.35f : 0.92f);
            }
        }

        var selected = SelectedIds.Contains(item.Id);
        context.DrawRectangle(new Pen(Brush(selected ? SelectionStroke : ObjectStroke), selected ? 2 : 1), rect);

        if (item.Type == "Marker")
        {
            IBrush markerColor;
            try { markerColor = Brush(item.Color); } catch { markerColor = Brush("#65a7ff"); }
            var pen = new Pen(markerColor, 1);
            context.DrawLine(pen, rect.TopLeft, rect.BottomRight);
            context.DrawLine(pen, rect.TopRight, rect.BottomLeft);
        }

        pushed?.Dispose();

        var label = new FormattedText(item.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brush("#dfe6f0"));
        context.DrawText(label, rect.TopLeft - new Point(0, 18));

        // Component badges
        if (item.Components.Any(c => c.Kind == "Particles"))
        {
            DrawParticleBadge(context, rect);
        }

        var camComp = item.Components.FirstOrDefault(c => c.Kind == "Camera");
        if (camComp is not null)
        {
            DrawCameraViewport(context, item, camComp);
        }

        // Pivot marker + rotation handle for the selected single object
        if (item.Id == SelectedId && SelectedIds.Count == 1)
        {
            DrawPivotAndHandle(context, item, rect, pivotX, pivotY);
        }
    }

    private void DrawPivotAndHandle(DrawingContext context, SceneObject item, Rect rect, double pivotX, double pivotY)
    {
        // Pivot crosshair
        var pivotBrush = Brush("#ff6b6b");
        context.DrawEllipse(null, new Pen(pivotBrush, 1.5), new Point(pivotX, pivotY), 4, 4);
        context.DrawLine(new Pen(pivotBrush, 1), new Point(pivotX - 7, pivotY), new Point(pivotX + 7, pivotY));
        context.DrawLine(new Pen(pivotBrush, 1), new Point(pivotX, pivotY - 7), new Point(pivotX, pivotY + 7));

        // Rotation handle
        var handle = ComputeRotationHandle(item, rect, pivotX, pivotY);
        context.DrawLine(new Pen(Brush("#65a7ff"), 1), new Point(pivotX, pivotY), handle);
        context.DrawEllipse(Brush("#65a7ff"), new Pen(Brush("#ffffff"), 1), handle, 5, 5);
    }

    private static Point ComputeRotationHandle(SceneObject item, Rect rect, double pivotX, double pivotY)
    {
        // handle sits above the rect by 24 px, rotated around pivot
        var localX = rect.Left + rect.Width * item.PivotX;
        var localY = rect.Top - 24;
        var dx = localX - pivotX;
        var dy = localY - pivotY;
        var theta = item.Rotation * Math.PI / 180.0;
        var rx = dx * Math.Cos(theta) - dy * Math.Sin(theta);
        var ry = dx * Math.Sin(theta) + dy * Math.Cos(theta);
        return new Point(pivotX + rx, pivotY + ry);
    }

    private void DrawModel3DProxy(DrawingContext context, SceneObject item, Rect rect)
    {
        // Gradient body
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        bg.GradientStops.Add(new GradientStop(Color.Parse("#5e3aa8"), 0));
        bg.GradientStops.Add(new GradientStop(Color.Parse("#9b6cff"), 1));
        context.FillRectangle(bg, rect, 6);

        var icon = new FormattedText("◈", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, Math.Min(rect.Width, rect.Height) * 0.55, Brush("#ffffff"));
        var ix = rect.Left + (rect.Width - icon.Width) / 2;
        var iy = rect.Top + (rect.Height - icon.Height) / 2 - 6;
        context.DrawText(icon, new Point(ix, iy));

        var name = new FormattedText(Path.GetFileNameWithoutExtension(item.ModelPath), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brush("#e8e0ff"));
        var nx = rect.Left + (rect.Width - name.Width) / 2;
        context.DrawText(name, new Point(nx, rect.Bottom - 14));
    }

    private void DrawCameraViewport(DrawingContext context, SceneObject item, MonoForge.Editor.Models.ComponentData comp)
    {
        var w = comp.Properties.TryGetValue("Width", out var ws) && double.TryParse(ws, out var ww) ? ww : 1280;
        var h = comp.Properties.TryGetValue("Height", out var hs) && double.TryParse(hs, out var hh) ? hh : 720;
        var zoom = comp.Properties.TryGetValue("Zoom", out var zs) && double.TryParse(zs, out var zz) ? zz : 1.0;
        var active = !comp.Properties.TryGetValue("IsActive", out var ia) || ia.Equals("true", StringComparison.OrdinalIgnoreCase);

        var vw = w / Math.Max(0.01, zoom);
        var vh = h / Math.Max(0.01, zoom);
        var center = WorldToScreen(new Point(item.X + item.Width / 2, item.Y + item.Height / 2));
        var rect = new Rect(center.X - vw * Zoom / 2, center.Y - vh * Zoom / 2, vw * Zoom, vh * Zoom);
        var color = active ? "#ffd166" : "#717b85";
        context.DrawRectangle(new Pen(Brush(color), 2, dashStyle: new DashStyle(new[] { 6.0, 4.0 }, 0)), rect);
        // Crosshair
        context.DrawLine(new Pen(Brush(color), 1), new Point(center.X - 6, center.Y), new Point(center.X + 6, center.Y));
        context.DrawLine(new Pen(Brush(color), 1), new Point(center.X, center.Y - 6), new Point(center.X, center.Y + 6));
        var label = new FormattedText($"📷 {w:0}×{h:0}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brush(color));
        context.DrawText(label, new Point(rect.Left + 4, rect.Top + 4));
    }

    private void DrawParticleBadge(DrawingContext context, Rect rect)
    {
        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        var brush = Brush("#ffd166");
        var pen = new Pen(brush, 1);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var r = 18 * Zoom;
            context.DrawLine(pen, new Point(cx, cy), new Point(cx + Math.Cos(angle) * r, cy + Math.Sin(angle) * r));
        }
        context.DrawEllipse(brush, null, new Point(cx, cy), 3, 3);
    }

    private void DrawHud(DrawingContext context, Rect bounds)
    {
        var text = new FormattedText($"{Zoom * 100:0}%   2D   {(SnapToGrid ? "snap" : "free")}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brush(TextSecondary));
        context.DrawText(text, new Point(bounds.Width - 160, bounds.Height - 28));
    }

    private Point WorldToScreen(Point point)
    {
        return new Point(point.X * Zoom + Camera.X, point.Y * Zoom + Camera.Y);
    }

    private Point ScreenToWorld(Point point)
    {
        return new Point((point.X - Camera.X) / Zoom, (point.Y - Camera.Y) / Zoom);
    }
}
