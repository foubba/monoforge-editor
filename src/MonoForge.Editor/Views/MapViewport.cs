using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

/// <summary>
/// 3D orbit viewport for a <see cref="MapDocument"/>. Renders entities as colored
/// gizmos (wireframe boxes / spheres / icons) on top of a ground grid. Drawing is
/// done directly via Avalonia's DrawingContext — light enough that we don't need a
/// software rasterizer for V1; we add real GLB rendering in a later iteration.
/// </summary>
public sealed class MapViewport : Control
{
    private readonly MapDocument _map;
    private double _yaw = 0.6, _pitch = -0.5, _distance = 18.0;
    private Vector3 _target;
    private Point _lastPointer;
    private bool _orbiting, _panning;
    // Translate-gizmo state: while not None we're dragging the selected entity along an axis.
    private enum GizmoAxis { None, X, Y, Z }
    public enum GizmoMode { Move, Rotate, Scale }
    public GizmoMode Mode { get; private set; } = GizmoMode.Move;
    public event Action<GizmoMode>? ModeChanged;
    private GizmoAxis _draggingAxis = GizmoAxis.None;
    private Vector3 _gizmoStartPos;
    private Vector3 _gizmoStartRot;
    private Vector3 _gizmoStartScale;
    private Point _gizmoStartMouse;

    public void SetMode(GizmoMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        ModeChanged?.Invoke(mode);
        InvalidateVisual();
    }

    /// <summary>0 means no snapping. When &gt; 0 the Move gizmo rounds new positions
    /// to the nearest multiple along each axis.</summary>
    public float SnapStep { get; set; }

    /// <summary>Move the orbit target to the entity's position and pull in slightly.</summary>
    public void FrameOnEntity(MapEntity ent)
    {
        _target = ent.PositionVec;
        _distance = Math.Max(_distance * 0.6, 4.0);
        WriteCameraBack();
        InvalidateVisual();
    }

    /// <summary>Fires once at the end of every gizmo drag — the host uses this as a
    /// natural undo snapshot point.</summary>
    public event Action? GizmoDragCompleted;
    // Global GLB cache so we only parse each file once across the whole editor.
    private static readonly Dictionary<string, ModelData?> _modelCache = new();

    /// <summary>Primary (anchor) selected entity — what the inspector shows and what
    /// the gizmo centers on. Null when nothing is selected.</summary>
    public MapEntity? Selected { get; private set; }
    /// <summary>Full multi-selection set. Always contains <see cref="Selected"/> when
    /// non-null. Empty when nothing is selected.</summary>
    public HashSet<MapEntity> SelectedSet { get; } = new();
    public event Action<MapEntity?>? SelectionChanged;
    public event Action? EntitiesMutated;
    // Per-entity transform snapshot at the start of a gizmo drag — needed so the
    // delta computed from the primary entity can be re-applied to all selected
    // entities relative to their own starting transform.
    private readonly Dictionary<MapEntity, (Vector3 pos, Vector3 rot, Vector3 scale)> _gizmoStartAll = new();
    // Marquee (drag-to-select rectangle) state. While active, the gizmo and orbit
    // controls are skipped and pointer moves just resize the rect.
    private bool _marqueeActive;
    private Point _marqueeStart, _marqueeEnd;

    public MapViewport(MapDocument map)
    {
        _map = map;
        ClipToBounds = true;
        Focusable = true;
        // Pick up the camera position from the document on first load.
        var camPos = map.Camera.GetPosition();
        var camTgt = map.Camera.GetTarget();
        _target = camTgt;
        var d = (camPos - camTgt).Length();
        if (d > 0.1f) _distance = d;
        // Derive yaw/pitch from camera vector.
        var dir = Vector3.Normalize(camPos - camTgt);
        _pitch = Math.Asin(dir.Y);
        _yaw = Math.Atan2(dir.X, dir.Z);
    }

    public void Invalidate()
    {
        InvalidateVisual();
    }

    public void SelectEntity(MapEntity? e)
    {
        Selected = e;
        SelectedSet.Clear();
        if (e is not null) SelectedSet.Add(e);
        SelectionChanged?.Invoke(e);
        InvalidateVisual();
    }

    /// <summary>Add an entity to the multi-selection, making it the new primary.</summary>
    public void AddToSelection(MapEntity e)
    {
        SelectedSet.Add(e);
        Selected = e;
        SelectionChanged?.Invoke(e);
        InvalidateVisual();
    }

    /// <summary>Toggle an entity's membership in the multi-selection (Shift+click).</summary>
    public void ToggleSelection(MapEntity e)
    {
        if (SelectedSet.Remove(e))
        {
            if (Selected == e) Selected = SelectedSet.FirstOrDefault();
        }
        else
        {
            SelectedSet.Add(e);
            Selected = e;
        }
        SelectionChanged?.Invoke(Selected);
        InvalidateVisual();
    }

    /// <summary>Replace the entire selection set with the given entities. Primary becomes
    /// the last item in the set (or null when empty).</summary>
    public void SetSelection(IEnumerable<MapEntity> entities)
    {
        SelectedSet.Clear();
        MapEntity? last = null;
        foreach (var e in entities) { SelectedSet.Add(e); last = e; }
        Selected = last;
        SelectionChanged?.Invoke(Selected);
        InvalidateVisual();
    }

    // ── Camera math ─────────────────────────────────────────────────────────────

    private Vector3 CameraPosition()
    {
        var x = (float)(Math.Cos(_pitch) * Math.Sin(_yaw));
        var y = (float)Math.Sin(_pitch);
        var z = (float)(Math.Cos(_pitch) * Math.Cos(_yaw));
        return _target + new Vector3(x, y, z) * (float)_distance;
    }

    private Matrix4x4 ViewProj(double width, double height)
    {
        var eye = CameraPosition();
        var view = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitY);
        var aspect = (float)(width / Math.Max(1, height));
        var fov = (_map.Camera.FovDegrees) * MathF.PI / 180f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 500f);
        return view * proj;
    }

    private Point? Project(Vector3 world, Matrix4x4 vp, double width, double height)
    {
        var v4 = Vector4.Transform(new Vector4(world, 1f), vp);
        if (v4.W <= 0.001f) return null;
        var ndcX = v4.X / v4.W;
        var ndcY = v4.Y / v4.W;
        if (v4.Z / v4.W is < -1 or > 1) return null;
        return new Point((ndcX * 0.5 + 0.5) * width, (1 - (ndcY * 0.5 + 0.5)) * height);
    }

    /// <summary>
    /// Project a line segment with proper near-plane clipping. If one endpoint is
    /// behind the camera (W ≤ 0) we lerp it forward along the line until it's just in
    /// front, so the visible part of the line still draws. Returns (null, null) if the
    /// whole segment is behind the camera.
    /// </summary>
    private (Point? a, Point? b) ProjectLine(Vector3 a, Vector3 b, Matrix4x4 vp, double width, double height)
    {
        var v4a = Vector4.Transform(new Vector4(a, 1f), vp);
        var v4b = Vector4.Transform(new Vector4(b, 1f), vp);
        const float epsW = 0.05f;

        if (v4a.W <= epsW && v4b.W <= epsW) return (null, null);

        if (v4a.W <= epsW)
        {
            // Lerp toward b until W crosses epsW.
            var t = (epsW - v4a.W) / (v4b.W - v4a.W);
            v4a = Vector4.Lerp(v4a, v4b, t);
        }
        else if (v4b.W <= epsW)
        {
            var t = (epsW - v4b.W) / (v4a.W - v4b.W);
            v4b = Vector4.Lerp(v4b, v4a, t);
        }

        return (NdcToScreen(v4a, width, height), NdcToScreen(v4b, width, height));
    }

    private static Point NdcToScreen(Vector4 v, double width, double height)
    {
        var ndcX = v.X / v.W;
        var ndcY = v.Y / v.W;
        return new Point((ndcX * 0.5 + 0.5) * width, (1 - (ndcY * 0.5 + 0.5)) * height);
    }

    // ── Pointer input ───────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _lastPointer = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _panning = true;
            e.Pointer.Capture(this);
        }
        else if (props.IsRightButtonPressed)
        {
            _orbiting = true;
            e.Pointer.Capture(this);
        }
        else
        {
            // Gizmo axes take priority over entity picking — if the user clicked an
            // axis arrow / ring / handle of the selected entity, start dragging.
            if (Selected is not null)
            {
                var axis = Mode switch
                {
                    GizmoMode.Rotate => HitTestRotateGizmo(_lastPointer),
                    _ => HitTestGizmo(_lastPointer),
                };
                if (axis != GizmoAxis.None)
                {
                    _draggingAxis = axis;
                    _gizmoStartPos = Selected.PositionVec;
                    _gizmoStartRot = Selected.RotationVec;
                    _gizmoStartScale = Selected.ScaleVec;
                    _gizmoStartMouse = _lastPointer;
                    // Snapshot every selected entity's transform so the same delta the
                    // user drags on the primary can be re-applied to each one relative
                    // to its own starting transform.
                    _gizmoStartAll.Clear();
                    foreach (var s in SelectedSet)
                        _gizmoStartAll[s] = (s.PositionVec, s.RotationVec, s.ScaleVec);
                    e.Pointer.Capture(this);
                    return;
                }
            }
            // Try to pick an entity at the cursor; Shift adds/removes from selection.
            var hit = HitTest(_lastPointer);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (hit is not null)
            {
                if (shift) ToggleSelection(hit);
                else SelectEntity(hit);
            }
            else
            {
                // Empty space + left click → start a marquee. Shift preserves the
                // existing selection so the marquee adds to it on release.
                if (!shift) SelectEntity(null);
                _marqueeActive = true;
                _marqueeStart = _marqueeEnd = _lastPointer;
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var dx = pos.X - _lastPointer.X;
        var dy = pos.Y - _lastPointer.Y;
        if (_marqueeActive)
        {
            _marqueeEnd = pos;
            InvalidateVisual();
            _lastPointer = pos;
            return;
        }
        if (_draggingAxis != GizmoAxis.None && Selected is not null)
        {
            ApplyGizmoDrag(pos);
            _lastPointer = pos;
            return;
        }
        if (_orbiting)
        {
            _yaw -= dx * 0.01;
            _pitch = Math.Clamp(_pitch + dy * 0.01, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01);
            InvalidateVisual();
        }
        else if (_panning)
        {
            // Move target along the view-aligned right / up axes.
            var eye = CameraPosition();
            var forward = Vector3.Normalize(_target - eye);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Cross(right, forward);
            var k = (float)(_distance * 0.0015);
            _target -= right * (float)dx * k;
            _target += up * (float)dy * k;
            InvalidateVisual();
        }
        _lastPointer = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_marqueeActive)
        {
            _marqueeActive = false;
            FinalizeMarquee(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            InvalidateVisual();
        }
        if (_draggingAxis != GizmoAxis.None)
        {
            _draggingAxis = GizmoAxis.None;
            _gizmoStartAll.Clear();
            EntitiesMutated?.Invoke();
            GizmoDragCompleted?.Invoke();
        }
        _orbiting = _panning = false;
        e.Pointer.Capture(null);
        WriteCameraBack();
    }

    /// <summary>
    /// Convert the marquee rect (in screen space) into an entity selection. An entity
    /// is selected if its projected origin lies inside the rect AND is in front of the
    /// camera. With <paramref name="additive"/> = true (Shift) the existing selection
    /// is preserved.
    /// </summary>
    private void FinalizeMarquee(bool additive)
    {
        var dx = Math.Abs(_marqueeEnd.X - _marqueeStart.X);
        var dy = Math.Abs(_marqueeEnd.Y - _marqueeStart.Y);
        if (dx < 3 && dy < 3) return; // treat as click — already handled by Press path
        var rect = new Rect(
            Math.Min(_marqueeStart.X, _marqueeEnd.X),
            Math.Min(_marqueeStart.Y, _marqueeEnd.Y),
            dx, dy);
        var vp = ViewProj(Bounds.Width, Bounds.Height);
        var hits = new List<MapEntity>();
        foreach (var ent in _map.Entities)
        {
            var p = Project(ent.PositionVec, vp, Bounds.Width, Bounds.Height);
            if (p is null) continue;
            if (rect.Contains(new Point(p.Value.X, p.Value.Y))) hits.Add(ent);
        }
        if (additive)
        {
            foreach (var h in hits) SelectedSet.Add(h);
            if (hits.Count > 0) Selected = hits[^1];
            SelectionChanged?.Invoke(Selected);
        }
        else
        {
            SetSelection(hits);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 0.9 : 1.1;
        _distance = Math.Clamp(_distance * factor, 1.5, 200);
        InvalidateVisual();
        WriteCameraBack();
    }

    private void WriteCameraBack()
    {
        _map.Camera.SetPosition(CameraPosition());
        _map.Camera.SetTarget(_target);
    }

    // ── Translate gizmo ─────────────────────────────────────────────────────────

    /// <summary>Length (in world units) of the gizmo arrow legs.</summary>
    private const float GizmoLength = 1.5f;

    /// <summary>If the click landed within ~8 px of an axis arrow, return that axis.</summary>
    private GizmoAxis HitTestGizmo(Point screen)
    {
        if (Selected is null) return GizmoAxis.None;
        var vp = ViewProj(Bounds.Width, Bounds.Height);
        var origin = Project(Selected.PositionVec, vp, Bounds.Width, Bounds.Height);
        if (origin is null) return GizmoAxis.None;

        var pos = Selected.PositionVec;
        (GizmoAxis axis, Vector3 dir)[] axes =
        {
            (GizmoAxis.X, Vector3.UnitX),
            (GizmoAxis.Y, Vector3.UnitY),
            (GizmoAxis.Z, Vector3.UnitZ),
        };
        foreach (var (axis, dir) in axes)
        {
            var (a, b) = ProjectLine(pos, pos + dir * GizmoLength, vp, Bounds.Width, Bounds.Height);
            if (a is null || b is null) continue;
            if (DistanceToSegment(screen, a.Value, b.Value) < 8) return axis;
        }
        return GizmoAxis.None;
    }

    /// <summary>
    /// Apply the active gizmo's drag math. Translate and Scale both project the mouse
    /// delta onto the axis' screen-space direction. Rotate uses the screen-space angle
    /// around the entity's center, which is approximate but feels natural for any view
    /// orientation.
    /// </summary>
    private void ApplyGizmoDrag(Point mouse)
    {
        if (Selected is null) return;
        var vp = ViewProj(Bounds.Width, Bounds.Height);
        var dir = _draggingAxis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        if (Mode == GizmoMode.Rotate)
        {
            // Screen-space angle from the entity origin gives a stable rotation feel
            // regardless of how the camera is oriented relative to the axis.
            var origin = Project(_gizmoStartPos, vp, Bounds.Width, Bounds.Height);
            if (origin is null) return;
            var start = Math.Atan2(_gizmoStartMouse.Y - origin.Value.Y, _gizmoStartMouse.X - origin.Value.X);
            var cur = Math.Atan2(mouse.Y - origin.Value.Y, mouse.X - origin.Value.X);
            var deltaDeg = (float)((cur - start) * 180.0 / Math.PI);
            // Apply the same rotation delta to every selected entity relative to its own
            // start orientation. Y inverted to match Unity/Blender convention.
            var targets = _gizmoStartAll.Count > 0 ? _gizmoStartAll.Keys.ToList() : new List<MapEntity> { Selected };
            foreach (var ent in targets)
            {
                var startRot = _gizmoStartAll.TryGetValue(ent, out var snap) ? snap.rot : _gizmoStartRot;
                var r = startRot;
                switch (_draggingAxis)
                {
                    case GizmoAxis.X: r.X = startRot.X + deltaDeg; break;
                    case GizmoAxis.Y: r.Y = startRot.Y - deltaDeg; break;
                    case GizmoAxis.Z: r.Z = startRot.Z + deltaDeg; break;
                }
                ent.RotationVec = r;
            }
            InvalidateVisual();
            return;
        }

        // Move / Scale both project the mouse delta onto the axis' on-screen direction.
        var originScreen = Project(_gizmoStartPos, vp, Bounds.Width, Bounds.Height);
        var endScreen = Project(_gizmoStartPos + dir, vp, Bounds.Width, Bounds.Height);
        if (originScreen is null || endScreen is null) return;

        var axisVecX = endScreen.Value.X - originScreen.Value.X;
        var axisVecY = endScreen.Value.Y - originScreen.Value.Y;
        var axisLen2 = axisVecX * axisVecX + axisVecY * axisVecY;
        if (axisLen2 < 1) return;

        var mouseDx = mouse.X - _gizmoStartMouse.X;
        var mouseDy = mouse.Y - _gizmoStartMouse.Y;
        var worldDelta = (mouseDx * axisVecX + mouseDy * axisVecY) / axisLen2;

        if (Mode == GizmoMode.Scale)
        {
            // Apply the same scale delta to every selected entity, relative to its own
            // starting scale (snapshot taken at drag start). Falls back to Selected-only
            // when no per-entity snapshot exists.
            var targets = _gizmoStartAll.Count > 0 ? _gizmoStartAll.Keys.ToList() : new List<MapEntity> { Selected };
            foreach (var ent in targets)
            {
                var start = _gizmoStartAll.TryGetValue(ent, out var snap) ? snap.scale : _gizmoStartScale;
                var s = start;
                switch (_draggingAxis)
                {
                    case GizmoAxis.X: s.X = Math.Max(0.05f, start.X + (float)worldDelta); break;
                    case GizmoAxis.Y: s.Y = Math.Max(0.05f, start.Y + (float)worldDelta); break;
                    case GizmoAxis.Z: s.Z = Math.Max(0.05f, start.Z + (float)worldDelta); break;
                }
                ent.ScaleVec = s;
            }
        }
        else
        {
            var targets = _gizmoStartAll.Count > 0 ? _gizmoStartAll.Keys.ToList() : new List<MapEntity> { Selected };
            foreach (var ent in targets)
            {
                var start = _gizmoStartAll.TryGetValue(ent, out var snap) ? snap.pos : _gizmoStartPos;
                var pos = start + dir * (float)worldDelta;
                if (SnapStep > 0)
                {
                    pos = new Vector3(
                        MathF.Round(pos.X / SnapStep) * SnapStep,
                        MathF.Round(pos.Y / SnapStep) * SnapStep,
                        MathF.Round(pos.Z / SnapStep) * SnapStep);
                }
                ent.PositionVec = pos;
            }
        }
        InvalidateVisual();
    }

    /// <summary>Hit test for the rotation rings — closest sample point on any ring within ~9 px.</summary>
    private GizmoAxis HitTestRotateGizmo(Point screen)
    {
        if (Selected is null) return GizmoAxis.None;
        var vp = ViewProj(Bounds.Width, Bounds.Height);
        var pos = Selected.PositionVec;
        (GizmoAxis axis, Vector3 u, Vector3 v)[] rings =
        {
            (GizmoAxis.X, Vector3.UnitY, Vector3.UnitZ),
            (GizmoAxis.Y, Vector3.UnitX, Vector3.UnitZ),
            (GizmoAxis.Z, Vector3.UnitX, Vector3.UnitY),
        };
        var bestAxis = GizmoAxis.None;
        var bestDist = 9.0;
        const int samples = 48;
        foreach (var (axis, u, v) in rings)
        {
            for (var i = 0; i < samples; i++)
            {
                var a = i * 2.0 * Math.PI / samples;
                var p = pos + u * (float)(Math.Cos(a) * GizmoLength) + v * (float)(Math.Sin(a) * GizmoLength);
                var sp = Project(p, vp, Bounds.Width, Bounds.Height);
                if (sp is null) continue;
                var dx = sp.Value.X - screen.X;
                var dy = sp.Value.Y - screen.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < bestDist) { bestDist = d; bestAxis = axis; }
            }
        }
        return bestAxis;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var px = a.X + t * dx;
        var py = a.Y + t * dy;
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    /// <summary>Closest entity (in screen space) to the given pixel, within 18 px.</summary>
    private MapEntity? HitTest(Point screen)
    {
        var vp = ViewProj(Bounds.Width, Bounds.Height);
        MapEntity? best = null;
        var bestDist = 18.0;
        foreach (var ent in _map.Entities)
        {
            var p = Project(ent.PositionVec, vp, Bounds.Width, Bounds.Height);
            if (p is null) continue;
            var d = Math.Sqrt((p.Value.X - screen.X) * (p.Value.X - screen.X) + (p.Value.Y - screen.Y) * (p.Value.Y - screen.Y));
            if (d < bestDist) { bestDist = d; best = ent; }
        }
        return best;
    }

    // ── Rendering ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 4 || h < 4) return;

        // Neutral gray background — slight gradient so the floor reads as a separate
        // surface, but without any blue tint.
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = {
                new GradientStop(Color.Parse("#2a2a2a"), 0),
                new GradientStop(Color.Parse("#1c1c1c"), 1),
            }
        };
        ctx.FillRectangle(bg, new Rect(0, 0, w, h));

        var vp = ViewProj(w, h);

        DrawGroundGrid(ctx, vp, w, h);

        foreach (var ent in _map.Entities)
        {
            // Highlight every member of the multi-selection (the primary is just one
            // of them — it gets the gizmo on top).
            DrawEntity(ctx, ent, vp, w, h, isSelected: SelectedSet.Contains(ent));
        }

        // Gizmo on top of everything for the primary selected entity. The gizmo drag
        // already applies its delta to every member of SelectedSet.
        if (Selected is not null)
        {
            switch (Mode)
            {
                case GizmoMode.Move:   DrawTranslateGizmo(ctx, Selected, vp, w, h); break;
                case GizmoMode.Rotate: DrawRotateGizmo(ctx, Selected, vp, w, h); break;
                case GizmoMode.Scale:  DrawScaleGizmo(ctx, Selected, vp, w, h); break;
            }
        }

        // Marquee rectangle drawn over everything while the user is dragging it out.
        if (_marqueeActive)
        {
            var rect = new Rect(
                Math.Min(_marqueeStart.X, _marqueeEnd.X),
                Math.Min(_marqueeStart.Y, _marqueeEnd.Y),
                Math.Abs(_marqueeEnd.X - _marqueeStart.X),
                Math.Abs(_marqueeEnd.Y - _marqueeStart.Y));
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#5fb0ff"), 0.12), rect);
            ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#5fb0ff"), 0.8), 1), rect);
        }

        // Tiny axis triad in the bottom-left corner.
        DrawAxisTriad(ctx, w, h);
    }

    private void DrawTranslateGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h)
    {
        var pos = ent.PositionVec;
        DrawArrow(ctx, pos, pos + Vector3.UnitX * GizmoLength, "#ff5252", _draggingAxis == GizmoAxis.X, vp, w, h);
        DrawArrow(ctx, pos, pos + Vector3.UnitY * GizmoLength, "#5fff7a", _draggingAxis == GizmoAxis.Y, vp, w, h);
        DrawArrow(ctx, pos, pos + Vector3.UnitZ * GizmoLength, "#5fb0ff", _draggingAxis == GizmoAxis.Z, vp, w, h);
    }

    /// <summary>Three orthogonal rings around the selected entity. Each ring is the rotation
    /// plane perpendicular to its named axis.</summary>
    private void DrawRotateGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h)
    {
        var pos = ent.PositionVec;
        DrawRing(ctx, pos, Vector3.UnitY, Vector3.UnitZ, "#ff5252", _draggingAxis == GizmoAxis.X, vp, w, h);
        DrawRing(ctx, pos, Vector3.UnitX, Vector3.UnitZ, "#5fff7a", _draggingAxis == GizmoAxis.Y, vp, w, h);
        DrawRing(ctx, pos, Vector3.UnitX, Vector3.UnitY, "#5fb0ff", _draggingAxis == GizmoAxis.Z, vp, w, h);
    }

    private void DrawRing(DrawingContext ctx, Vector3 center, Vector3 u, Vector3 v, string color, bool active, Matrix4x4 vp, double w, double h)
    {
        var brush = Avalonia.Media.Brush.Parse(color);
        var pen = new Pen(brush, active ? 3.0 : 2.0);
        const int samples = 48;
        Vector3 LastWorld() => center + u * (float)(Math.Cos((samples - 1) * 2.0 * Math.PI / samples) * GizmoLength) + v * (float)(Math.Sin((samples - 1) * 2.0 * Math.PI / samples) * GizmoLength);
        var prevWorld = LastWorld();
        for (var i = 0; i < samples; i++)
        {
            var a = i * 2.0 * Math.PI / samples;
            var p = center + u * (float)(Math.Cos(a) * GizmoLength) + v * (float)(Math.Sin(a) * GizmoLength);
            var (pa, pb) = ProjectLine(prevWorld, p, vp, w, h);
            if (pa is not null && pb is not null) ctx.DrawLine(pen, pa.Value, pb.Value);
            prevWorld = p;
        }
    }

    /// <summary>Scale gizmo — same arrows as translate but with a filled cube at each tip.</summary>
    private void DrawScaleGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h)
    {
        var pos = ent.PositionVec;
        DrawScaleHandle(ctx, pos, pos + Vector3.UnitX * GizmoLength, "#ff5252", _draggingAxis == GizmoAxis.X, vp, w, h);
        DrawScaleHandle(ctx, pos, pos + Vector3.UnitY * GizmoLength, "#5fff7a", _draggingAxis == GizmoAxis.Y, vp, w, h);
        DrawScaleHandle(ctx, pos, pos + Vector3.UnitZ * GizmoLength, "#5fb0ff", _draggingAxis == GizmoAxis.Z, vp, w, h);
    }

    private void DrawScaleHandle(DrawingContext ctx, Vector3 from, Vector3 to, string color, bool active, Matrix4x4 vp, double w, double h)
    {
        var (a, b) = ProjectLine(from, to, vp, w, h);
        if (a is null || b is null) return;
        var brush = Avalonia.Media.Brush.Parse(color);
        var pen = new Pen(brush, active ? 3.5 : 2.5);
        ctx.DrawLine(pen, a.Value, b.Value);
        // Square handle at the tip — clearly different from the translate arrowhead.
        var size = active ? 9 : 7;
        ctx.FillRectangle(brush, new Rect(b.Value.X - size / 2.0, b.Value.Y - size / 2.0, size, size));
    }

    private void DrawArrow(DrawingContext ctx, Vector3 from, Vector3 to, string color, bool active, Matrix4x4 vp, double w, double h)
    {
        var (a, b) = ProjectLine(from, to, vp, w, h);
        if (a is null || b is null) return;
        var brush = Avalonia.Media.Brush.Parse(color);
        var pen = new Pen(brush, active ? 4.0 : 2.5, lineCap: PenLineCap.Round);
        ctx.DrawLine(pen, a.Value, b.Value);

        // Arrowhead — small filled triangle at the tip.
        var dx = b.Value.X - a.Value.X;
        var dy = b.Value.Y - a.Value.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 6) return;
        var ux = dx / len; var uy = dy / len;
        var perpX = -uy; var perpY = ux;
        const double head = 10;
        const double headW = 4.5;
        var baseX = b.Value.X - ux * head;
        var baseY = b.Value.Y - uy * head;
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(b.Value, true);
            c.LineTo(new Point(baseX + perpX * headW, baseY + perpY * headW));
            c.LineTo(new Point(baseX - perpX * headW, baseY - perpY * headW));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geo);
    }

    private void DrawGroundGrid(DrawingContext ctx, Matrix4x4 vp, double w, double h)
    {
        // Fixed 20×20 grid centred on world origin. We tried larger camera-following
        // grids and distance-fade variants but every iteration caused either
        // criss-cross at the horizon or inconsistent line visibility while the
        // camera moved. A small, fixed, bounded grid is predictable and matches
        // what Unity/Godot scene views show by default.
        const int N = 10;
        const int Major = 5;
        var pen = new Pen(Avalonia.Media.Brush.Parse("#404040"), 1);
        var penMajor = new Pen(Avalonia.Media.Brush.Parse("#525252"), 1);
        var penAxisX = new Pen(Avalonia.Media.Brush.Parse("#b04545"), 1.6);
        var penAxisZ = new Pen(Avalonia.Media.Brush.Parse("#4567b0"), 1.6);
        void DrawSegment(Vector3 a, Vector3 b, Pen p)
        {
            // No off-screen reject: when zoomed in close, perspective pushes the
            // projected endpoints far past the canvas edges and any reject check
            // would silently drop visible lines. Avalonia's ClipToBounds handles
            // the actual clipping so drawing wildly-off-screen coords is safe.
            var (pa, pb) = ProjectLine(a, b, vp, w, h);
            if (pa is null || pb is null) return;
            ctx.DrawLine(p, pa.Value, pb.Value);
        }

        for (var i = -N; i <= N; i++)
        {
            var pX = i == 0 ? penAxisX : (i % Major == 0 ? penMajor : pen);
            var pZ = i == 0 ? penAxisZ : (i % Major == 0 ? penMajor : pen);
            DrawSegment(new Vector3(-N, 0, i), new Vector3(N, 0, i), pX);
            DrawSegment(new Vector3(i, 0, -N), new Vector3(i, 0, N), pZ);
        }
    }

    private static (Color fill, string label) Palette(string type) => type switch
    {
        "Model3D" => (Color.Parse("#5fb0ff"), "M"),
        "Sprite" => (Color.Parse("#ff9a5f"), "S"),
        "Light" => (Color.Parse("#ffd166"), "L"),
        "Camera" => (Color.Parse("#a78bfa"), "C"),
        "Trigger" => (Color.Parse("#5fffa3"), "T"),
        "Text" => (Color.Parse("#e0e0e0"), "Tx"),
        _ => (Color.Parse("#cdd5dc"), "·"),
    };

    private void DrawEntity(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h, bool isSelected)
    {
        var (col, label) = Palette(ent.Type);
        var pos = ent.PositionVec;
        var screen = Project(pos, vp, vp, w, h);
        if (screen is null) return;

        // World matrix for the entity (TRS) — applied to model mesh and the fallback gizmo box.
        var world = BuildWorldMatrix(ent);
        var penFill = new Pen(new SolidColorBrush(col, isSelected ? 1.0 : 0.55), isSelected ? 1.6 : 1.0);

        // Model3D entities with a loaded mesh: render the actual triangle wireframe so
        // the user sees the real shape (capped via stride for heavy meshes).
        var drewMesh = false;
        if (ent.Type == "Model3D" && !string.IsNullOrEmpty(ent.ModelPath))
        {
            var model = GetCachedModel(ent.ModelPath);
            if (model is not null)
            {
                DrawMeshWireframe(ctx, model, world, vp, w, h, penFill);
                drewMesh = true;
            }
        }

        if (!drewMesh)
        {
            // Pick a fitting gizmo per entity type so the user can tell them apart at a glance.
            switch (ent.Type)
            {
                case "Light":     DrawLightGizmo(ctx, ent, vp, w, h, penFill); break;
                case "Camera":    DrawCameraGizmo(ctx, ent, vp, w, h, penFill); break;
                case "Trigger":   DrawTriggerGizmo(ctx, ent, vp, w, h, col, isSelected); break;
                default:          DrawBoxGizmo(ctx, ent, vp, w, h, penFill); break;
            }
        }

        // Center dot + name label.
        var c = screen.Value;
        ctx.FillRectangle(new SolidColorBrush(col), new Rect(c.X - 3, c.Y - 3, 6, 6));
        var ft = new FormattedText(
            ent.Name, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Menlo"), isSelected ? 12 : 11,
            new SolidColorBrush(col, isSelected ? 1.0 : 0.7));
        ctx.DrawText(ft, new Point(c.X + 8, c.Y - 6));

        if (isSelected)
        {
            // Bright outline ring on the screen-space center for unmissable selection feedback.
            ctx.DrawEllipse(null, new Pen(Avalonia.Media.Brush.Parse("#ffffff"), 1.4), c, 10, 10);
        }
    }

    // Overload for clarity (also accepts vp by value).
    private Point? Project(Vector3 world, Matrix4x4 vp, Matrix4x4 _, double width, double height)
        => Project(world, vp, width, height);

    /// <summary>Fallback wireframe box (default gizmo).</summary>
    private void DrawBoxGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h, Pen pen)
    {
        var pos = ent.PositionVec;
        var size = ent.ScaleVec;
        if (size.X < 0.05f) size.X = 0.5f;
        if (size.Y < 0.05f) size.Y = 0.5f;
        if (size.Z < 0.05f) size.Z = 0.5f;
        var half = size * 0.5f;
        var corners = new[]
        {
            new Vector3(-half.X, -half.Y, -half.Z), new Vector3( half.X, -half.Y, -half.Z),
            new Vector3( half.X,  half.Y, -half.Z), new Vector3(-half.X,  half.Y, -half.Z),
            new Vector3(-half.X, -half.Y,  half.Z), new Vector3( half.X, -half.Y,  half.Z),
            new Vector3( half.X,  half.Y,  half.Z), new Vector3(-half.X,  half.Y,  half.Z),
        };
        int[][] edges =
        {
            new[] {0,1}, new[] {1,2}, new[] {2,3}, new[] {3,0},
            new[] {4,5}, new[] {5,6}, new[] {6,7}, new[] {7,4},
            new[] {0,4}, new[] {1,5}, new[] {2,6}, new[] {3,7},
        };
        foreach (var e in edges)
        {
            var (a, b) = ProjectLine(pos + corners[e[0]], pos + corners[e[1]], vp, w, h);
            if (a is not null && b is not null) ctx.DrawLine(pen, a.Value, b.Value);
        }
    }

    /// <summary>Light: small star icon at the origin plus a direction ray for directional/spot lights.</summary>
    private void DrawLightGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h, Pen pen)
    {
        var pos = ent.PositionVec;
        var origin = Project(pos, vp, w, h);
        if (origin is null) return;
        // Star rays.
        var c = origin.Value;
        foreach (var ang in new[] { 0.0, Math.PI / 4, Math.PI / 2, 3 * Math.PI / 4 })
        {
            var dx = Math.Cos(ang) * 8;
            var dy = Math.Sin(ang) * 8;
            ctx.DrawLine(pen, new Point(c.X - dx, c.Y - dy), new Point(c.X + dx, c.Y + dy));
        }
        // Direction ray for directional / spot — pointed along the entity's local -Z axis (rotated by Rotation).
        if (ent.LightType is "directional" or "spot")
        {
            var deg = MathF.PI / 180f;
            var rot = ent.RotationVec;
            var r = Matrix4x4.CreateRotationX(rot.X * deg) *
                    Matrix4x4.CreateRotationY(rot.Y * deg) *
                    Matrix4x4.CreateRotationZ(rot.Z * deg);
            var dir = Vector3.TransformNormal(new Vector3(0, 0, -1), r);
            var (a, b) = ProjectLine(pos, pos + dir * 2.5f, vp, w, h);
            if (a is not null && b is not null)
                ctx.DrawLine(pen, a.Value, b.Value);
        }
    }

    /// <summary>Camera: a small frustum pyramid pointing along the entity's -Z.</summary>
    private void DrawCameraGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h, Pen pen)
    {
        var pos = ent.PositionVec;
        var deg = MathF.PI / 180f;
        var rot = ent.RotationVec;
        var r = Matrix4x4.CreateRotationX(rot.X * deg) *
                Matrix4x4.CreateRotationY(rot.Y * deg) *
                Matrix4x4.CreateRotationZ(rot.Z * deg);
        Vector3 Rot(Vector3 v) => Vector3.TransformNormal(v, r);
        // Frustum tip at pos, far rectangle 2 units forward, 1.6 wide × 1 tall (16:10 ish).
        var tip = pos;
        var fwd = Rot(new Vector3(0, 0, -1));
        var right = Rot(new Vector3(1, 0, 0));
        var up = Rot(new Vector3(0, 1, 0));
        var far = tip + fwd * 2.0f;
        var halfW = 0.8f; var halfH = 0.5f;
        var ftl = far + up * halfH - right * halfW;
        var ftr = far + up * halfH + right * halfW;
        var fbl = far - up * halfH - right * halfW;
        var fbr = far - up * halfH + right * halfW;
        void Line(Vector3 a, Vector3 b)
        {
            var (pa, pb) = ProjectLine(a, b, vp, w, h);
            if (pa is not null && pb is not null) ctx.DrawLine(pen, pa.Value, pb.Value);
        }
        Line(tip, ftl); Line(tip, ftr); Line(tip, fbl); Line(tip, fbr);
        Line(ftl, ftr); Line(ftr, fbr); Line(fbr, fbl); Line(fbl, ftl);
    }

    /// <summary>Trigger: wireframe box but in a translucent fill style so the shape reads as a volume.</summary>
    private void DrawTriggerGizmo(DrawingContext ctx, MapEntity ent, Matrix4x4 vp, double w, double h, Color col, bool selected)
    {
        var size = new Vector3(ent.TriggerSize[0], ent.TriggerSize[1], ent.TriggerSize[2]) * 0.5f;
        if (size.X < 0.05f) size.X = 0.5f;
        if (size.Y < 0.05f) size.Y = 0.5f;
        if (size.Z < 0.05f) size.Z = 0.5f;
        var pos = ent.PositionVec;
        var corners = new[]
        {
            pos + new Vector3(-size.X, -size.Y, -size.Z), pos + new Vector3( size.X, -size.Y, -size.Z),
            pos + new Vector3( size.X,  size.Y, -size.Z), pos + new Vector3(-size.X,  size.Y, -size.Z),
            pos + new Vector3(-size.X, -size.Y,  size.Z), pos + new Vector3( size.X, -size.Y,  size.Z),
            pos + new Vector3( size.X,  size.Y,  size.Z), pos + new Vector3(-size.X,  size.Y,  size.Z),
        };
        int[][] edges =
        {
            new[] {0,1}, new[] {1,2}, new[] {2,3}, new[] {3,0},
            new[] {4,5}, new[] {5,6}, new[] {6,7}, new[] {7,4},
            new[] {0,4}, new[] {1,5}, new[] {2,6}, new[] {3,7},
        };
        // Dashed pen so triggers read as "volume" rather than solid geometry.
        var pen = new Pen(new SolidColorBrush(col, selected ? 1.0 : 0.65), selected ? 1.6 : 1.0, dashStyle: new DashStyle(new double[] { 4, 3 }, 0));
        foreach (var e in edges)
        {
            var (a, b) = ProjectLine(corners[e[0]], corners[e[1]], vp, w, h);
            if (a is not null && b is not null) ctx.DrawLine(pen, a.Value, b.Value);
        }
    }

    /// <summary>
    /// Compose a world matrix from the entity's TRS. Rotation is Euler XYZ in degrees;
    /// we build R = Rx * Ry * Rz (Z-first since the GLB convention used elsewhere in
    /// MonoForge is Y-up, right-handed).
    /// </summary>
    private static Matrix4x4 BuildWorldMatrix(MapEntity ent)
    {
        var deg = MathF.PI / 180f;
        var rot = ent.RotationVec;
        var r =
            Matrix4x4.CreateRotationX(rot.X * deg) *
            Matrix4x4.CreateRotationY(rot.Y * deg) *
            Matrix4x4.CreateRotationZ(rot.Z * deg);
        var s = Matrix4x4.CreateScale(ent.ScaleVec);
        var t = Matrix4x4.CreateTranslation(ent.PositionVec);
        return s * r * t;
    }

    /// <summary>Lazy-load + cache GLB models. nulls in the cache mean "tried, failed".</summary>
    private static ModelData? GetCachedModel(string path)
    {
        if (_modelCache.TryGetValue(path, out var cached)) return cached;
        ModelData? model = null;
        try { model = ModelData.Load(path); }
        catch { /* ignored — show fallback wireframe box */ }
        _modelCache[path] = model;
        return model;
    }

    /// <summary>
    /// Walk every triangle and draw its three edges projected through our near-plane
    /// clipper. Heavy meshes get a stride so dense GLBs still render at interactive
    /// rates with Avalonia's drawing API (rough heuristic: keep ~1500 triangles drawn).
    /// </summary>
    private void DrawMeshWireframe(DrawingContext ctx, ModelData model, Matrix4x4 world, Matrix4x4 vp, double w, double h, Pen pen)
    {
        var verts = model.BindVertices;
        var idx = model.Indices;
        if (verts.Count == 0 || idx.Count < 3) return;

        var triCount = idx.Count / 3;
        var stride = 1;
        if (triCount > 1500) stride = (int)Math.Ceiling(triCount / 1500.0);

        // Pre-transform vertices once if the mesh isn't huge — cheaper than per-edge.
        Span<Vector3> wv = verts.Count <= 65536 ? new Vector3[verts.Count] : new Vector3[verts.Count];
        for (var i = 0; i < verts.Count; i++) wv[i] = Vector3.Transform(verts[i], world);

        for (var t = 0; t < triCount; t += stride)
        {
            var i0 = idx[t * 3];
            var i1 = idx[t * 3 + 1];
            var i2 = idx[t * 3 + 2];
            if (i0 >= wv.Length || i1 >= wv.Length || i2 >= wv.Length) continue;
            var p0 = wv[i0]; var p1 = wv[i1]; var p2 = wv[i2];

            var (a, b) = ProjectLine(p0, p1, vp, w, h);
            if (a is not null && b is not null) ctx.DrawLine(pen, a.Value, b.Value);
            (a, b) = ProjectLine(p1, p2, vp, w, h);
            if (a is not null && b is not null) ctx.DrawLine(pen, a.Value, b.Value);
            (a, b) = ProjectLine(p2, p0, vp, w, h);
            if (a is not null && b is not null) ctx.DrawLine(pen, a.Value, b.Value);
        }
    }

    private static void DrawAxisTriad(DrawingContext ctx, double w, double h)
    {
        var o = new Point(28, h - 28);
        var len = 18;
        ctx.DrawLine(new Pen(Avalonia.Media.Brush.Parse("#ff6b6b"), 2), o, new Point(o.X + len, o.Y));         // X red
        ctx.DrawLine(new Pen(Avalonia.Media.Brush.Parse("#5fffa3"), 2), o, new Point(o.X, o.Y - len));         // Y green
        ctx.DrawLine(new Pen(Avalonia.Media.Brush.Parse("#5fb0ff"), 2), o, new Point(o.X - len * 0.7, o.Y + len * 0.5)); // Z blue
    }
}
