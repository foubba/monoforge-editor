using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Models;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

/// <summary>
/// Editor for .mfmap documents. V1 supports 3D scenes: place GLB models, sprites,
/// lights, cameras and triggers on a freeform ground plane; drag/drop GLBs from the
/// asset tree to add them; click in the viewport to select; edit transform in the
/// inspector strip on the right. UI/2D mode and terrain land in follow-up passes.
/// </summary>
public sealed class MapEditor : UserControl
{
    private readonly MapDocument _doc;
    private readonly string _filePath;
    private readonly MapViewport _viewport;
    private readonly ListBox _outline = new();
    private readonly StackPanel _inspector = new() { Spacing = 6, Margin = new Thickness(10) };
    private readonly TextBlock _status = new() { Foreground = Brush(TextDim), FontSize = 11, Padding = new Thickness(10, 4) };
    private Button[] _modeButtons = Array.Empty<Button>();
    private bool _dirty;
    // Undo/redo stacks hold JSON snapshots of the whole MapDocument. Snapshots are
    // taken before a mutation (so undoing returns to the pre-mutation state) and
    // after gizmo drags so each drag is one undo step rather than per-frame noise.
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private const int UndoCap = 100;

    public bool IsDirty => _dirty;
    public string FilePath => _filePath;
    public event Action<MapEditor>? DirtyChanged;

    public MapEditor(string filePath, MapDocument doc)
    {
        _filePath = filePath;
        _doc = doc;

        _viewport = new MapViewport(_doc);
        _viewport.SelectionChanged += _ => { RefreshInspector(); RefreshOutlineSelection(); };
        _viewport.EntitiesMutated += () => { MarkDirty(); _viewport.Invalidate(); RefreshOutline(); };
        // Each completed gizmo drag becomes one undo step — we snapshot BEFORE the drag
        // begins, but the snapshot push happens at drag-start through PushUndoBefore.
        _viewport.GizmoDragCompleted += () => RefreshInspector();

        // ── Toolbar (Add Entity / Save / Camera reset) ──
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8),
            Background = Brush(MenuBackground)
        };
        toolbar.Children.Add(MakeAddMenu());
        // Transform-mode triplet (Move / Rotate / Scale). Industry-standard W/E/R shortcuts.
        var moveBtn = MakeModeButton("Move  W", MapViewport.GizmoMode.Move);
        var rotBtn = MakeModeButton("Rotate  E", MapViewport.GizmoMode.Rotate);
        var scaleBtn = MakeModeButton("Scale  R", MapViewport.GizmoMode.Scale);
        toolbar.Children.Add(moveBtn);
        toolbar.Children.Add(rotBtn);
        toolbar.Children.Add(scaleBtn);
        _viewport.ModeChanged += _ => RefreshModeButtons();
        toolbar.Children.Add(MenuButton("Undo  ⌘Z", (_, _) => Undo()));
        toolbar.Children.Add(MenuButton("Redo  ⌘⇧Z", (_, _) => Redo()));
        // Snap-to-grid toggle for the Move gizmo. Step value comes from the dropdown.
        var snapCheck = new CheckBox
        {
            Content = "Snap",
            Foreground = Brush(TextSecondary),
            FontFamily = new FontFamily(UiFont),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        var snapCombo = new ComboBox
        {
            ItemsSource = new[] { "0.25", "0.5", "1", "2", "5" },
            SelectedIndex = 2, // default 1 unit
            Width = 70,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            FontSize = 11,
        };
        void ApplySnap()
        {
            var step = (snapCheck.IsChecked ?? false) && snapCombo.SelectedItem is string s && float.TryParse(s, out var v) ? v : 0f;
            _viewport.SnapStep = step;
        }
        snapCheck.IsCheckedChanged += (_, _) => ApplySnap();
        snapCombo.SelectionChanged += (_, _) => ApplySnap();
        toolbar.Children.Add(snapCheck);
        toolbar.Children.Add(snapCombo);
        toolbar.Children.Add(MenuButton("Frame  F", (_, _) => FrameSelection()));
        toolbar.Children.Add(MenuButton("Reset Camera", (_, _) => { ResetCamera(); }));
        toolbar.Children.Add(MenuButton("Save  ⌘S", async (_, _) => await SaveAsync()));
        toolbar.Children.Add(MenuButton("Export Runtime…", async (_, _) => await ExportRuntimeAsync()));
        toolbar.Children.Add(new TextBlock
        {
            Text = "Drag a .glb from the asset tree onto the viewport. Right-drag orbit, MMB / Alt-drag pan, wheel zoom.",
            Foreground = Brush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0)
        });
        _modeButtons = new[] { moveBtn, rotBtn, scaleBtn };
        RefreshModeButtons();

        // ── Outline (entity list) ──
        _outline.Background = Brush(PanelBackground);
        _outline.Foreground = Brush(TextSecondary);
        _outline.FontFamily = new FontFamily(UiFont);
        _outline.FontSize = 12;
        _outline.BorderThickness = new Thickness(0);
        _outline.SelectionChanged += (_, _) =>
        {
            if (_outline.SelectedItem is MapEntity ent) _viewport.SelectEntity(ent);
        };
        var outlineHeader = new TextBlock
        {
            Text = "Entities",
            Foreground = Brush(TextMuted),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Padding = new Thickness(12, 8, 10, 4)
        };
        var outlinePane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush(PanelBackground)
        };
        outlinePane.Children.Add(outlineHeader.At(row: 0));
        outlinePane.Children.Add(_outline.At(row: 1));

        // ── Inspector ──
        var inspectorHeader = new TextBlock
        {
            Text = "Properties",
            Foreground = Brush(TextMuted),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Padding = new Thickness(12, 8, 10, 4)
        };
        var inspectorPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush(PanelBackground)
        };
        inspectorPane.Children.Add(inspectorHeader.At(row: 0));
        inspectorPane.Children.Add(new ScrollViewer { Content = _inspector }.At(row: 1));

        // Right sidebar = outline on top, inspector below.
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
        sidebar.Children.Add(outlinePane.At(row: 0));
        sidebar.Children.Add(new Border { Background = Brush(BorderSubtle), Height = 4 }.At(row: 1));
        sidebar.Children.Add(inspectorPane.At(row: 2));

        // ── Root layout ──
        var center = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = Brush(EditorBackground)
        };
        center.Children.Add(toolbar.At(row: 0));
        center.Children.Add(_viewport.At(row: 1));
        center.Children.Add(_status.At(row: 2));

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,260"),
            Background = Brush(EditorBackground)
        };
        root.Children.Add(center.At(column: 0));
        root.Children.Add(sidebar.At(column: 1));

        Content = root;

        // Drag-drop GLBs into the viewport adds a Model3D entity at origin.
        DragDrop.SetAllowDrop(_viewport, true);
        _viewport.AddHandler(DragDrop.DropEvent, OnViewportDrop);
        _viewport.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);

        // Keyboard shortcuts.
        KeyDown += (_, e) =>
        {
            var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (meta && e.Key == Key.S) { _ = SaveAsync(); e.Handled = true; return; }
            if (meta && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Z) { Redo(); e.Handled = true; return; }
            if (meta && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
            if (meta && e.Key == Key.D && _viewport.SelectedSet.Count > 0)
            {
                PushUndo();
                // Snapshot to a list because DuplicateEntity mutates SelectedSet (and
                // we want each duplicate added to the new selection, not nested).
                var toDup = _viewport.SelectedSet.ToList();
                foreach (var ent in toDup) DuplicateEntity(ent);
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.Delete || e.Key == Key.Back) && _viewport.SelectedSet.Count > 0)
            {
                PushUndo();
                foreach (var ent in _viewport.SelectedSet.ToList()) RemoveEntity(ent);
                e.Handled = true;
                return;
            }
            // Industry-standard transform-mode shortcuts (Maya/Blender/Unity). Skip when
            // a text input has focus so the user can still type "w" / "e" / "r" in the
            // inspector fields.
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (!meta && focused is not TextBox)
            {
                switch (e.Key)
                {
                    case Key.W: _viewport.SetMode(MapViewport.GizmoMode.Move); e.Handled = true; return;
                    case Key.E: _viewport.SetMode(MapViewport.GizmoMode.Rotate); e.Handled = true; return;
                    case Key.R: _viewport.SetMode(MapViewport.GizmoMode.Scale); e.Handled = true; return;
                    case Key.F: FrameSelection(); e.Handled = true; return;
                }
            }
        };

        // Take an undo snapshot every time a gizmo drag is about to start.
        _viewport.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var props = e.GetCurrentPoint(_viewport).Properties;
            // Only on plain left-click — orbit/pan don't mutate the document.
            if (props.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                PushUndo();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        RefreshOutline();
        RefreshInspector();
        UpdateStatus();
    }

    // ── Add-entity menu ─────────────────────────────────────────────────────────

    /// <summary>One of the Move/Rotate/Scale buttons in the toolbar. Highlight reflects active mode.</summary>
    private Button MakeModeButton(string label, MapViewport.GizmoMode mode)
    {
        var btn = new Button
        {
            Content = label,
            Background = Brush(FilterBackground),
            BorderBrush = Brush(BorderColor),
            Foreground = Brush(TextSecondary),
            FontFamily = new FontFamily(UiFont),
            FontSize = 11,
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(3),
        };
        btn.Click += (_, _) => _viewport.SetMode(mode);
        btn.Tag = mode;
        return btn;
    }

    private void RefreshModeButtons()
    {
        foreach (var b in _modeButtons)
        {
            var active = b.Tag is MapViewport.GizmoMode m && m == _viewport.Mode;
            b.Background = Brush(active ? Accent : FilterBackground);
            b.Foreground = Brush(active ? TextPrimary : TextSecondary);
            b.BorderBrush = Brush(active ? AccentBorder : BorderColor);
        }
    }

    private Control MakeAddMenu()
    {
        var addBtn = new Button
        {
            Content = "+  Add",
            Background = Brush(Accent),
            BorderBrush = Brush(AccentBorder),
            Foreground = Brush(TextPrimary),
            FontFamily = new FontFamily(UiFont),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(4),
        };
        addBtn.Flyout = new MenuFlyout
        {
            Items =
            {
                AddMenuItem("Empty",   "Empty"),
                AddMenuItem("Model3D", "Model3D"),
                AddMenuItem("Sprite",  "Sprite"),
                AddMenuItem("Light",   "Light"),
                AddMenuItem("Camera",  "Camera"),
                AddMenuItem("Trigger", "Trigger"),
                AddMenuItem("Text",    "Text"),
            }
        };
        return addBtn;
    }

    private MenuItem AddMenuItem(string label, string type)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += (_, _) => AddEntity(type);
        return mi;
    }

    private MapEntity AddEntity(string type, string? modelPath = null)
    {
        PushUndo();
        var ent = new MapEntity { Type = type, Name = type };
        if (modelPath is not null)
        {
            ent.ModelPath = modelPath;
            ent.Name = Path.GetFileNameWithoutExtension(modelPath);
        }
        _doc.Entities.Add(ent);
        MarkDirty();
        RefreshOutline();
        _viewport.SelectEntity(ent);
        _viewport.Invalidate();
        return ent;
    }

    /// <summary>Clone the entity (deep-copy via JSON round-trip), nudge its X by 1 so the
    /// duplicate isn't z-fighting the original, and select it.</summary>
    private void DuplicateEntity(MapEntity ent)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ent);
        var copy = System.Text.Json.JsonSerializer.Deserialize<MapEntity>(json);
        if (copy is null) return;
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        copy.Name = ent.Name + " (copy)";
        copy.Position = new[] { ent.Position[0] + 1f, ent.Position[1], ent.Position[2] };
        _doc.Entities.Add(copy);
        MarkDirty();
        RefreshOutline();
        _viewport.SelectEntity(copy);
        _viewport.Invalidate();
    }

    private void RemoveEntity(MapEntity ent)
    {
        _doc.Entities.Remove(ent);
        MarkDirty();
        _viewport.SelectEntity(null);
        RefreshOutline();
        _viewport.Invalidate();
    }

    // ── Drag/drop ───────────────────────────────────────────────────────────────

    private void OnViewportDrop(object? sender, DragEventArgs e)
    {
        var data = e.Data;
        // Asset tree drops a "monoforge/asset-path" text payload; external Finder drops
        // come as file lists.
        string? path = data.GetText();
        if (string.IsNullOrEmpty(path) && data.Contains(DataFormats.Files))
        {
            var files = data.GetFiles();
            if (files is not null)
            {
                foreach (var f in files)
                {
                    if (f.Path.LocalPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.LocalPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                    {
                        path = f.Path.LocalPath;
                        break;
                    }
                }
            }
        }
        if (string.IsNullOrEmpty(path)) return;
        if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) return;
        AddEntity("Model3D", path);
    }

    // ── Outline / inspector ─────────────────────────────────────────────────────

    private void RefreshOutline()
    {
        _outline.ItemsSource = _doc.Entities.ToList();
        _outline.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MapEntity>((ent, _) => new TextBlock
        {
            Text = $"{TypeGlyph(ent.Type)}  {ent.Name}",
            Foreground = Brush(TextSecondary),
            FontFamily = new FontFamily(UiFont),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        }, supportsRecycling: true);
        UpdateStatus();
    }

    private void RefreshOutlineSelection()
    {
        if (_viewport.Selected is null) { _outline.SelectedIndex = -1; return; }
        var idx = _doc.Entities.IndexOf(_viewport.Selected);
        if (idx >= 0) _outline.SelectedIndex = idx;
    }

    private static string TypeGlyph(string type) => type switch
    {
        "Model3D" => "◆",
        "Sprite" => "▣",
        "Light" => "✦",
        "Camera" => "▷",
        "Trigger" => "○",
        "Text" => "T",
        _ => "·",
    };

    private void RefreshInspector()
    {
        _inspector.Children.Clear();
        var ent = _viewport.Selected;
        if (ent is null)
        {
            _inspector.Children.Add(new TextBlock
            {
                Text = "Click an entity in the viewport or outline to inspect it.",
                Foreground = Brush(TextDim),
                FontFamily = new FontFamily(UiFont),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        AddRow("Name", ent.Name, v => { ent.Name = v; MarkDirty(); RefreshOutline(); });
        AddRow("Type", ent.Type, v => { ent.Type = v; MarkDirty(); _viewport.Invalidate(); });
        AddSection("Transform");
        AddVec3("Position", ent.PositionVec, v => { ent.PositionVec = v; MarkDirty(); _viewport.Invalidate(); });
        AddVec3("Rotation°", ent.RotationVec, v => { ent.RotationVec = v; MarkDirty(); _viewport.Invalidate(); });
        AddVec3("Scale", ent.ScaleVec, v => { ent.ScaleVec = v; MarkDirty(); _viewport.Invalidate(); });

        if (ent.Type == "Model3D")
        {
            AddSection("Mesh");
            AddRowWithPicker("ModelPath", ent.ModelPath ?? "",
                v => { ent.ModelPath = v; MarkDirty(); _viewport.Invalidate(); },
                async () => await PickModelFileAsync());
        }
        else if (ent.Type == "Sprite")
        {
            AddSection("Sprite");
            AddRow("TexturePath", ent.TexturePath ?? "", v => { ent.TexturePath = v; MarkDirty(); });
            AddRow("Color", ent.Color, v => { ent.Color = v; MarkDirty(); });
        }
        else if (ent.Type == "Text")
        {
            AddSection("Text");
            AddRow("Content", ent.Text ?? "", v => { ent.Text = v; MarkDirty(); });
            AddRow("Color", ent.Color, v => { ent.Color = v; MarkDirty(); });
        }
        else if (ent.Type == "Light")
        {
            AddSection("Light");
            AddRow("LightType", ent.LightType, v => { ent.LightType = v; MarkDirty(); });
            AddRow("Color", ent.Color, v => { ent.Color = v; MarkDirty(); });
            AddRow("Intensity", ent.LightIntensity.ToString("0.##"),
                v => { if (float.TryParse(v, out var f)) { ent.LightIntensity = f; MarkDirty(); } });
        }
        else if (ent.Type == "Trigger")
        {
            AddSection("Trigger");
            AddRow("Shape", ent.TriggerShape, v => { ent.TriggerShape = v; MarkDirty(); });
            AddVec3("Size", new System.Numerics.Vector3(ent.TriggerSize[0], ent.TriggerSize[1], ent.TriggerSize[2]),
                v => { ent.TriggerSize = new[] { v.X, v.Y, v.Z }; MarkDirty(); _viewport.Invalidate(); });
        }

        var deleteBtn = new Button
        {
            Content = "Delete entity",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(BorderColor),
            Foreground = Brush("#ff8a8a"),
            FontSize = 11,
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0),
        };
        deleteBtn.Click += (_, _) => RemoveEntity(ent);
        _inspector.Children.Add(deleteBtn);
    }

    private void AddSection(string title)
    {
        _inspector.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(TextMuted),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 4)
        });
    }

    private void AddRow(string label, string value, Action<string> onChange)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*"),
            ColumnSpacing = 6
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily(UiFont),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        }.At(column: 0));
        var tb = new TextBox
        {
            Text = value,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            FontFamily = new FontFamily("Menlo"),
            FontSize = 11,
            Padding = new Thickness(6, 2),
            MinHeight = 22,
        };
        tb.LostFocus += (_, _) => { if (tb.Text != value) onChange(tb.Text ?? ""); };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) onChange(tb.Text ?? ""); };
        grid.Children.Add(tb.At(column: 1));
        _inspector.Children.Add(grid);
    }

    /// <summary>Like AddRow but with a "…" button that opens a file picker and writes
    /// the chosen path back into the field. Used for ModelPath / TexturePath inputs.</summary>
    private void AddRowWithPicker(string label, string value, Action<string> onChange, Func<Task<string?>> picker)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,Auto"),
            ColumnSpacing = 6
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily(UiFont),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        }.At(column: 0));
        var tb = new TextBox
        {
            Text = value,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            FontFamily = new FontFamily("Menlo"),
            FontSize = 11,
            Padding = new Thickness(6, 2),
            MinHeight = 22,
        };
        tb.LostFocus += (_, _) => { if (tb.Text != value) onChange(tb.Text ?? ""); };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) onChange(tb.Text ?? ""); };
        var pick = new Button
        {
            Content = "…",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(BorderColor),
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            Padding = new Thickness(8, 2),
            MinHeight = 22,
        };
        pick.Click += async (_, _) =>
        {
            var picked = await picker();
            if (!string.IsNullOrEmpty(picked))
            {
                tb.Text = picked;
                onChange(picked);
            }
        };
        grid.Children.Add(tb.At(column: 1));
        grid.Children.Add(pick.At(column: 2));
        _inspector.Children.Add(grid);
    }

    private async Task<string?> PickModelFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a model file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("3D model") { Patterns = new[] { "*.glb", "*.gltf" } }
            }
        });
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private void AddVec3(string label, System.Numerics.Vector3 v, Action<System.Numerics.Vector3> onChange)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,*,*"),
            ColumnSpacing = 4
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily(UiFont),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        }.At(column: 0));
        TextBox MakeNum(float val)
        {
            var tb = new TextBox
            {
                Text = val.ToString("0.##"),
                Background = Brush(InputBackground),
                Foreground = Brush(TextSecondary),
                BorderBrush = Brush(BorderColor),
                FontFamily = new FontFamily("Menlo"),
                FontSize = 11,
                Padding = new Thickness(4, 2),
                MinHeight = 22,
            };
            return tb;
        }
        var x = MakeNum(v.X);
        var y = MakeNum(v.Y);
        var z = MakeNum(v.Z);
        void Push()
        {
            float Parse(TextBox b) => float.TryParse(b.Text, out var f) ? f : 0;
            onChange(new System.Numerics.Vector3(Parse(x), Parse(y), Parse(z)));
        }
        x.LostFocus += (_, _) => Push();
        y.LostFocus += (_, _) => Push();
        z.LostFocus += (_, _) => Push();
        x.KeyDown += (_, e) => { if (e.Key == Key.Enter) Push(); };
        y.KeyDown += (_, e) => { if (e.Key == Key.Enter) Push(); };
        z.KeyDown += (_, e) => { if (e.Key == Key.Enter) Push(); };
        grid.Children.Add(x.At(column: 1));
        grid.Children.Add(y.At(column: 2));
        grid.Children.Add(z.At(column: 3));
        _inspector.Children.Add(grid);
    }

    // ── Save / dirty tracking ───────────────────────────────────────────────────

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        DirtyChanged?.Invoke(this);
        UpdateStatus();
    }

    public async Task<bool> SaveAsync()
    {
        try
        {
            MapJson.Save(_filePath, _doc);
            _dirty = false;
            DirtyChanged?.Invoke(this);
            UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            _status.Text = "Save failed: " + ex.Message;
            return false;
        }
    }

    /// <summary>Push a snapshot of the current document state onto the undo stack and
    /// clear the redo stack (since we just branched history).</summary>
    private void PushUndo()
    {
        _undo.Push(MapJson.Serialize(_doc));
        if (_undo.Count > UndoCap)
        {
            // Trim oldest by re-stacking. Cheap enough at this scale.
            var arr = _undo.ToArray();
            _undo.Clear();
            for (var i = UndoCap - 1; i >= 0; i--) _undo.Push(arr[i]);
        }
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(MapJson.Serialize(_doc));
        ApplySnapshot(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(MapJson.Serialize(_doc));
        ApplySnapshot(_redo.Pop());
    }

    /// <summary>Replace the live document fields from a JSON snapshot. We don't swap the
    /// document instance itself — the viewport / panels hold references to it.</summary>
    private void ApplySnapshot(string json)
    {
        var snap = MapJson.Deserialize(json);
        _doc.Name = snap.Name;
        _doc.Mode = snap.Mode;
        _doc.Camera = snap.Camera;
        _doc.Entities.Clear();
        foreach (var e in snap.Entities) _doc.Entities.Add(e);
        _viewport.SelectEntity(null);
        RefreshOutline();
        _viewport.Invalidate();
        MarkDirty();
    }

    private void FrameSelection()
    {
        if (_viewport.Selected is { } ent) _viewport.FrameOnEntity(ent);
    }

    /// <summary>
    /// Save a generated MapRuntime.cs the user can drop into their MonoGame project to
    /// load this map (and any other .mfmap that follows the same schema). They pick the
    /// output path and we infer the C# namespace from the filename's parent folder.
    /// </summary>
    private async Task ExportRuntimeAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export map runtime (.cs)",
            SuggestedFileName = "MapRuntime.cs",
            FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("C# source") { Patterns = new[] { "*.cs" } } }
        });
        if (file is null) return;
        var path = file.Path.LocalPath;
        // Best-effort namespace from the parent directory name; sanitize for invalid chars.
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "MyGame.Maps");
        var ns = new string((parent ?? "MyGame.Maps").Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray());
        if (ns.Length == 0 || char.IsDigit(ns[0])) ns = "MyGame." + ns;
        try
        {
            File.WriteAllText(path, Services.MapRuntimeExporter.GenerateRuntimeCs(ns));
            _status.Text = $"Exported runtime → {Path.GetFileName(path)} (namespace {ns})";
        }
        catch (Exception ex)
        {
            _status.Text = "Export failed: " + ex.Message;
        }
    }

    private void ResetCamera()
    {
        _doc.Camera = new MapCamera();
        // Recreate viewport state? Simpler: just trigger Invalidate; pos comes from camera.
        // For now, the viewport reads camera on construction; future iteration: expose reset on viewport.
        _viewport.Invalidate();
    }

    private void UpdateStatus()
    {
        var n = _doc.Entities.Count;
        var dirtyMark = _dirty ? "  ●" : "";
        _status.Text = $"{_doc.Name}    {_doc.Mode}    {n} entit{(n == 1 ? "y" : "ies")}{dirtyMark}";
    }
}
