using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpGLTF.Schema2;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

/// <summary>
/// 3D model viewer for .glb / .gltf. Software rasterizer with z-buffer, perspective-correct
/// texture sampling, Lambert + ambient lighting, skeletal animation playback.
/// </summary>
public sealed class Model3DViewer : UserControl
{
    private readonly string _path;
    private readonly ModelData? _model;
    private readonly Viewport3D? _viewport;
    private readonly TextBlock _stats = new() { Foreground = Brush(TextDim), FontSize = 11, FontFamily = new FontFamily("Menlo"), Padding = new Thickness(10, 6) };
    private readonly StackPanel _jointPanel = new() { Spacing = 4 };
    private readonly TextBlock _jointInfo = new() { Foreground = Brush(TextDivider), FontSize = 11, FontFamily = new FontFamily("Menlo"), TextWrapping = TextWrapping.Wrap };
    private readonly TimelineBar? _timeline;
    private bool _sidebarVisible = true;
    private Grid _layoutGrid = null!;

    public Model3DViewer(string filePath)
    {
        _path = filePath;
        try { _model = ModelData.Load(filePath); }
        catch (Exception ex)
        {
            Content = new TextBlock { Text = "GLB load failed: " + ex.Message, Foreground = Brush(TextDim), Padding = new Thickness(20) };
            return;
        }

        _viewport = new Viewport3D(_model);
        _timeline = new TimelineBar(_model, _viewport);
        _viewport.JointSelected += idx => UpdateJointInfo(idx);
        _viewport.PoseChanged += () => UpdateJointInfo(_viewport.SelectedJoint);
        UpdateJointInfo(-1);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        toolbar.Children.Add(MenuButton("Reset View", (_, _) => _viewport.ResetCamera()));
        toolbar.Children.Add(MenuButton("Frame", (_, _) => _viewport.FrameModel()));
        toolbar.Children.Add(ToggleBtn("Wireframe", () => _viewport.Wireframe, v => { _viewport.Wireframe = v; _viewport.RequestRedraw(); }));
        toolbar.Children.Add(ToggleBtn("Edges", () => _viewport.OverlayWires, v => { _viewport.OverlayWires = v; _viewport.RequestRedraw(); }));
        toolbar.Children.Add(ToggleBtn("Textures", () => _viewport.UseTextures, v => { _viewport.UseTextures = v; _viewport.RequestRedraw(); }));
        toolbar.Children.Add(ToggleBtn("Skeleton", () => _viewport.ShowSkeleton, v => { _viewport.ShowSkeleton = v; _viewport.RequestRedraw(); }));
        toolbar.Children.Add(ToggleBtn("Normals", () => _viewport.ShowNormals, v => { _viewport.ShowNormals = v; _viewport.RequestRedraw(); }));
        toolbar.Children.Add(ToggleBtn("Sidebar", () => _sidebarVisible, v =>
        {
            _sidebarVisible = v;
            _layoutGrid.ColumnDefinitions = _sidebarVisible
                ? new ColumnDefinitions("*,260")
                : new ColumnDefinitions("*,0");
        }));
        toolbar.Children.Add(MenuButton("AutoRig…", (_, _) =>
        {
            var center = (_model?.Min + _model?.Max) * 0.5f ?? new Vector3();
            new AutoRigDialog((center.X, center.Y, center.Z)).Show(this.FindAncestorOfType<Window>() ?? new Window());
        }));

        var sidebar = BuildSidebar();

        _layoutGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,260")
        };
        Grid.SetColumnSpan(toolbar, 2);
        _layoutGrid.Children.Add(toolbar.At(row: 0, column: 0));
        _layoutGrid.Children.Add(_viewport.At(row: 1, column: 0));
        _layoutGrid.Children.Add(sidebar.At(row: 1, column: 1));
        Grid.SetColumnSpan(_timeline, 2);
        _layoutGrid.Children.Add(_timeline.At(row: 2, column: 0));
        Grid.SetColumnSpan(_stats, 2);
        _layoutGrid.Children.Add(_stats.At(row: 3, column: 0));

        Content = _layoutGrid;
        UpdateStats();
    }

    private static Button ToggleBtn(string label, Func<bool> get, Action<bool> set)
    {
        var btn = new Button
        {
            Content = label,
            BorderBrush = Brush(BorderColor),
            FontSize = 12,
            Padding = new Thickness(10, 4)
        };
        void Refresh()
        {
            var active = get();
            btn.Background = Brush(active ? Accent : FilterBackground);
            btn.Foreground = Brush(active ? TextPrimary : TextMuted);
            btn.BorderBrush = Brush(active ? AccentBorder : BorderColor);
        }
        btn.Click += (_, _) => { set(!get()); Refresh(); };
        Refresh();
        return btn;
    }

    private void UpdateJointInfo(int idx)
    {
        _jointPanel.Children.Clear();
        if (_model is null || _viewport is null || idx < 0 || idx >= _model.JointCount)
        {
            _jointPanel.Children.Add(new TextBlock { Text = "Click a joint sphere to inspect.", Foreground = Brush(TextDim), FontSize = 11 });
            return;
        }

        var name = _model.JointNames[idx];
        var parent = _model.JointParents[idx];
        var parentName = parent < 0 ? "(root)" : _model.JointNames[parent];
        _jointPanel.Children.Add(new TextBlock { Text = $"#{idx}  {name}", Foreground = Brush(TextSecondary), FontSize = 12, FontWeight = FontWeight.SemiBold });
        _jointPanel.Children.Add(new TextBlock { Text = $"parent: {parentName}", Foreground = Brush(TextDim), FontSize = 11 });

        var (t, r, s) = _viewport.GetJointCurrentTRS(idx);

        BuildVec3Editor("Translation", t, v => Commit(idx, "translation", v, Quaternion.Identity));
        BuildEulerEditor("Rotation (Euler)", r, q => Commit(idx, "rotation", Vector3.Zero, q));
        BuildVec3Editor("Scale", s, v => Commit(idx, "scale", v, Quaternion.Identity));

        var remove = new Button
        {
            Content = "Delete key at current time",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(BorderColor),
            Foreground = Brush(TextDim),
            FontSize = 11,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 6, 0, 0)
        };
        remove.Click += (_, _) => DeleteKeysAtCurrentTime(idx);
        _jointPanel.Children.Add(remove);
    }

    private void Commit(int jointIdx, string property, Vector3 v, Quaternion q)
    {
        if (_viewport is null) return;
        _viewport.SetKeyAtCurrentTime(jointIdx, property, v, q);
        UpdateJointInfo(jointIdx);
    }

    private void DeleteKeysAtCurrentTime(int jointIdx)
    {
        if (_model is null || _viewport is null) return;
        if (_viewport.CurrentClip < 0) return;
        var clip = _model.Animations[_viewport.CurrentClip];
        foreach (var ch in clip.Channels.Where(c => c.JointIndex == jointIdx).ToList())
        {
            ch.RemoveKeyAt(_viewport.CurrentTime);
        }
        _viewport.RequestRedraw();
        UpdateJointInfo(jointIdx);
    }

    private void BuildVec3Editor(string label, Vector3 value, Action<Vector3> onChange)
    {
        _jointPanel.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        var x = NumericInput(value.X);
        var y = NumericInput(value.Y);
        var z = NumericInput(value.Z);
        void Push() { onChange(new Vector3(P(x), P(y), P(z))); }
        x.LostFocus += (_, _) => Push();
        y.LostFocus += (_, _) => Push();
        z.LostFocus += (_, _) => Push();
        x.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        y.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        z.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        grid.Children.Add(x.At(column: 0));
        grid.Children.Add(y.At(column: 1));
        grid.Children.Add(z.At(column: 2));
        _jointPanel.Children.Add(grid);
    }

    private void BuildEulerEditor(string label, Quaternion q, Action<Quaternion> onChange)
    {
        _jointPanel.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        var (rx, ry, rz) = QuatToEulerDeg(q);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        var x = NumericInput(rx);
        var y = NumericInput(ry);
        var z = NumericInput(rz);
        void Push() { onChange(EulerDegToQuat(P(x), P(y), P(z))); }
        x.LostFocus += (_, _) => Push();
        y.LostFocus += (_, _) => Push();
        z.LostFocus += (_, _) => Push();
        x.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        y.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        z.KeyUp += (_, e) => { if (e.Key == Key.Enter) Push(); };
        grid.Children.Add(x.At(column: 0));
        grid.Children.Add(y.At(column: 1));
        grid.Children.Add(z.At(column: 2));
        _jointPanel.Children.Add(grid);
    }

    private static TextBox NumericInput(float val) => new()
    {
        Text = val.ToString("0.###"),
        Background = Brush(InputBackground),
        Foreground = Brush(TextSecondary),
        BorderBrush = Brush(BorderColor),
        FontFamily = new FontFamily("Menlo"),
        FontSize = 11,
        Padding = new Thickness(6, 2)
    };

    private static float P(TextBox tb) => float.TryParse(tb.Text, out var v) ? v : 0;

    private static (float X, float Y, float Z) QuatToEulerDeg(Quaternion q)
    {
        // ZYX-ish Euler; good enough for editing
        var ysqr = q.Y * q.Y;
        var t0 = 2f * (q.W * q.X + q.Y * q.Z);
        var t1 = 1f - 2f * (q.X * q.X + ysqr);
        var roll = MathF.Atan2(t0, t1);
        var t2 = Math.Clamp(2f * (q.W * q.Y - q.Z * q.X), -1f, 1f);
        var pitch = MathF.Asin(t2);
        var t3 = 2f * (q.W * q.Z + q.X * q.Y);
        var t4 = 1f - 2f * (ysqr + q.Z * q.Z);
        var yaw = MathF.Atan2(t3, t4);
        const float toDeg = 180f / MathF.PI;
        return (roll * toDeg, pitch * toDeg, yaw * toDeg);
    }

    private static Quaternion EulerDegToQuat(float xDeg, float yDeg, float zDeg)
    {
        const float toRad = MathF.PI / 180f;
        return Quaternion.CreateFromYawPitchRoll(yDeg * toRad, xDeg * toRad, zDeg * toRad);
    }

    private Control BuildSidebar()
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        if (_model is null) return panel;

        panel.Children.Add(Text("Selected Joint", TextPrimary, FontWeight.Bold));
        panel.Children.Add(new Border
        {
            Background = Brush(InputBackground),
            Padding = new Thickness(8),
            Child = _jointPanel
        });

        panel.Children.Add(Text("Hierarchy", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 0));
        foreach (var node in _model.RootNodes) AppendNode(panel, node, 0);

        if (_model.Animations.Count > 0)
        {
            panel.Children.Add(Text("Animations", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 0));
            foreach (var a in _model.Animations)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {a.Name}   {a.Duration:0.##}s",
                    Foreground = Brush(TextSecondary),
                    FontSize = 11,
                    FontFamily = new FontFamily("Menlo")
                });
            }
        }

        if (_model.Materials.Count > 0)
        {
            panel.Children.Add(Text("Materials", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 0));
            foreach (var m in _model.Materials)
            {
                var line = m.HasTexture ? $"  ▤ {m.Name}" : $"  ○ {m.Name}";
                panel.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = Brush(TextSecondary),
                    FontSize = 11,
                    FontFamily = new FontFamily("Menlo")
                });
            }
        }

        return new ScrollViewer { Content = panel, Background = Brush(PanelBackground) };
    }

    private void AppendNode(StackPanel host, NodeInfo node, int depth)
    {
        host.Children.Add(new TextBlock
        {
            Text = new string(' ', depth * 2) + (node.IsJoint ? "● " : "▸ ") + node.Name,
            Foreground = Brush(node.IsJoint ? "#ffd166" : TextSecondary),
            FontSize = 11,
            FontFamily = new FontFamily("Menlo")
        });
        foreach (var c in node.Children) AppendNode(host, c, depth + 1);
    }

    private void UpdateStats()
    {
        if (_model is null) { _stats.Text = ""; return; }
        _stats.Text =
            $"{System.IO.Path.GetFileName(_path)}   |   " +
            $"verts: {_model.VertexCount}   tris: {_model.TriangleCount}   meshes: {_model.MeshCount}   " +
            $"materials: {_model.Materials.Count}   joints: {_model.JointCount}   animations: {_model.Animations.Count}   " +
            $"|  drag orbit · MMB/Alt pan · wheel zoom";
    }
}

internal sealed class MaterialInfo
{
    public string Name { get; init; } = "";
    public Bitmap? BaseColorTexture { get; init; }
    public Vector4 BaseColorFactor { get; init; } = new(1, 1, 1, 1);
    public float Metallic { get; init; }
    public float Roughness { get; init; } = 1f;
    public bool HasTexture => BaseColorTexture is not null;
}

internal sealed class JointInfluence
{
    // Up to 4 joints per vertex (glTF standard)
    public int J0, J1, J2, J3;
    public float W0, W1, W2, W3;
}

internal sealed class AnimChannel
{
    public int JointIndex;
    public string Property = "translation"; // translation | rotation | scale
    public float[] Times = Array.Empty<float>();
    public Vector3[] Vec3Values = Array.Empty<Vector3>();
    public Quaternion[] QuatValues = Array.Empty<Quaternion>();

    public bool IsRotation => Property == "rotation";

    /// <summary>Insert or replace a vector key at the given time. Keys are kept sorted.</summary>
    public void SetVec3Key(float time, Vector3 value)
    {
        const float eps = 1e-4f;
        var times = Times;
        var values = Vec3Values;
        for (var i = 0; i < times.Length; i++)
        {
            if (Math.Abs(times[i] - time) < eps) { values[i] = value; return; }
            if (times[i] > time)
            {
                var t = new float[times.Length + 1];
                var v = new Vector3[values.Length + 1];
                Array.Copy(times, 0, t, 0, i);
                Array.Copy(values, 0, v, 0, i);
                t[i] = time; v[i] = value;
                Array.Copy(times, i, t, i + 1, times.Length - i);
                Array.Copy(values, i, v, i + 1, values.Length - i);
                Times = t; Vec3Values = v;
                return;
            }
        }
        var newT = new float[times.Length + 1];
        var newV = new Vector3[values.Length + 1];
        Array.Copy(times, newT, times.Length);
        Array.Copy(values, newV, values.Length);
        newT[^1] = time; newV[^1] = value;
        Times = newT; Vec3Values = newV;
    }

    public void SetQuatKey(float time, Quaternion value)
    {
        const float eps = 1e-4f;
        var times = Times;
        var values = QuatValues;
        for (var i = 0; i < times.Length; i++)
        {
            if (Math.Abs(times[i] - time) < eps) { values[i] = value; return; }
            if (times[i] > time)
            {
                var t = new float[times.Length + 1];
                var v = new Quaternion[values.Length + 1];
                Array.Copy(times, 0, t, 0, i);
                Array.Copy(values, 0, v, 0, i);
                t[i] = time; v[i] = value;
                Array.Copy(times, i, t, i + 1, times.Length - i);
                Array.Copy(values, i, v, i + 1, values.Length - i);
                Times = t; QuatValues = v;
                return;
            }
        }
        var newT = new float[times.Length + 1];
        var newV = new Quaternion[values.Length + 1];
        Array.Copy(times, newT, times.Length);
        Array.Copy(values, newV, values.Length);
        newT[^1] = time; newV[^1] = value;
        Times = newT; QuatValues = newV;
    }

    public void RemoveKeyAt(float time)
    {
        const float eps = 1e-4f;
        for (var i = 0; i < Times.Length; i++)
        {
            if (Math.Abs(Times[i] - time) < eps)
            {
                var t = new float[Times.Length - 1];
                Array.Copy(Times, 0, t, 0, i);
                Array.Copy(Times, i + 1, t, i, Times.Length - i - 1);
                Times = t;
                if (IsRotation)
                {
                    var v = new Quaternion[QuatValues.Length - 1];
                    Array.Copy(QuatValues, 0, v, 0, i);
                    Array.Copy(QuatValues, i + 1, v, i, QuatValues.Length - i - 1);
                    QuatValues = v;
                }
                else
                {
                    var v = new Vector3[Vec3Values.Length - 1];
                    Array.Copy(Vec3Values, 0, v, 0, i);
                    Array.Copy(Vec3Values, i + 1, v, i, Vec3Values.Length - i - 1);
                    Vec3Values = v;
                }
                return;
            }
        }
    }
}

internal sealed class AnimClip
{
    public string Name = "";
    public float Duration;
    public List<AnimChannel> Channels = new();
}

internal sealed class NodeInfo
{
    public string Name { get; }
    public bool IsJoint { get; }
    public List<NodeInfo> Children { get; } = new();
    public NodeInfo(string name, bool joint) { Name = name; IsJoint = joint; }
}

/// <summary>
/// Snapshot of the GLB suitable for software rasterization and animation playback.
/// </summary>
internal sealed class ModelData
{
    public List<Vector3> BindVertices { get; } = new();
    public List<Vector3> BindNormals { get; } = new();
    public List<Vector2> UVs { get; } = new();
    public List<int> Indices { get; } = new();
    public List<int> TriMaterial { get; } = new();
    public List<JointInfluence?> Influences { get; } = new(); // per-vertex
    public List<MaterialInfo> Materials { get; } = new();
    public List<NodeInfo> RootNodes { get; } = new();
    public List<Vector3> JointRestPositions { get; } = new();
    public List<int> JointParents { get; } = new(); // index into JointRestPositions or -1
    public List<Matrix4x4> InverseBindMatrices { get; } = new();
    public List<Vector3> JointRestT { get; } = new();
    public List<Quaternion> JointRestR { get; } = new();
    public List<Vector3> JointRestS { get; } = new();
    public List<string> JointNames { get; } = new();
    public List<bool> VertexIsSkinned { get; } = new();
    public List<AnimClip> Animations { get; } = new();

    public int VertexCount => BindVertices.Count;
    public int TriangleCount => Indices.Count / 3;
    public int MeshCount;
    public int JointCount => JointRestPositions.Count;

    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }

    public static ModelData Load(string path)
    {
        var model = ModelRoot.Load(path);
        var data = new ModelData();
        var scene = model.DefaultScene ?? model.LogicalScenes[0];

        // Materials with base color texture if present.
        foreach (var m in model.LogicalMaterials)
        {
            Bitmap? tex = null;
            Vector4 factor = new(1, 1, 1, 1);
            var ch = m.FindChannel("BaseColor");
            if (ch is not null)
            {
                factor = ch.Value.Color;
                var img = ch.Value.Texture?.PrimaryImage;
                if (img is not null)
                {
                    try
                    {
                        using var ms = new MemoryStream(img.Content.Content.ToArray());
                        tex = new Bitmap(ms);
                    }
                    catch { tex = null; }
                }
            }
            float metallic = 0, roughness = 1;
            var mrCh = m.FindChannel("MetallicRoughness");
            if (mrCh is not null)
            {
                foreach (var p in mrCh.Value.Parameters)
                {
                    if (p.Name == "MetallicFactor" && p.Value is float mf) metallic = mf;
                    else if (p.Name == "RoughnessFactor" && p.Value is float rf) roughness = rf;
                }
            }
            data.Materials.Add(new MaterialInfo
            {
                Name = string.IsNullOrEmpty(m.Name) ? $"<mat #{m.LogicalIndex}>" : m.Name,
                BaseColorTexture = tex,
                BaseColorFactor = factor,
                Metallic = metallic,
                Roughness = roughness
            });
        }

        // Map: skin joint node → joint index inside model-flat joint list (concat of skins).
        // For simplicity we assume one skin (the common case for character models).
        var primarySkin = model.LogicalSkins.FirstOrDefault();
        var jointNodeToIndex = new Dictionary<Node, int>();
        if (primarySkin is not null)
        {
            for (var j = 0; j < primarySkin.JointsCount; j++)
            {
                var (jointNode, ibm) = primarySkin.GetJoint(j);
                jointNodeToIndex[jointNode] = j;
                var restPos = Vector3.Transform(Vector3.Zero, jointNode.WorldMatrix);
                data.JointRestPositions.Add(restPos);
                data.InverseBindMatrices.Add(ibm);
                // Local rest TRS = the bind-pose transform relative to the parent joint
                var local = jointNode.LocalTransform;
                data.JointRestT.Add(local.Translation);
                data.JointRestR.Add(local.Rotation);
                data.JointRestS.Add(local.Scale);
                data.JointNames.Add(string.IsNullOrEmpty(jointNode.Name) ? $"joint_{j}" : jointNode.Name);
            }
            // Parent indices within the skin (-1 if parent is not part of skin)
            for (var j = 0; j < primarySkin.JointsCount; j++)
            {
                var (jointNode, _) = primarySkin.GetJoint(j);
                var parent = jointNode.VisualParent;
                data.JointParents.Add(parent is not null && jointNodeToIndex.TryGetValue(parent, out var pi) ? pi : -1);
            }
        }

        // Hierarchy with joint flag
        var jointSet = new HashSet<int>();
        foreach (var skin in model.LogicalSkins)
            for (var j = 0; j < skin.JointsCount; j++)
                jointSet.Add(skin.GetJoint(j).Joint.LogicalIndex);
        foreach (var n in scene.VisualChildren)
            data.RootNodes.Add(BuildNodeInfo(n, jointSet));

        // Meshes — bake the mesh-node world transform ONLY for unskinned primitives.
        // For skinned primitives we leave vertices in their authoring (model) space so the
        // LBS step (which multiplies by jointWorld * inverseBindMatrix) produces the right pose.
        foreach (var node in scene.VisualChildren.SelectMany(Walk))
        {
            if (node.Mesh is null) continue;
            data.MeshCount++;
            var worldMat = node.WorldMatrix;
            var isSkinned = node.Skin is not null;
            foreach (var prim in node.Mesh.Primitives)
            {
                var posAcc = prim.GetVertexAccessor("POSITION");
                if (posAcc is null) continue;
                var positions = posAcc.AsVector3Array();
                var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var uvs = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                var jointsAcc = prim.GetVertexAccessor("JOINTS_0");
                var weightsAcc = prim.GetVertexAccessor("WEIGHTS_0");
                System.Collections.Generic.IList<Vector4>? joints = null;
                System.Collections.Generic.IList<Vector4>? weights = null;
                try { joints = jointsAcc?.AsVector4Array(); } catch { joints = null; }
                try { weights = weightsAcc?.AsVector4Array(); } catch { weights = null; }
                var indices = prim.GetIndices();

                var baseVtx = data.BindVertices.Count;
                for (var i = 0; i < positions.Count; i++)
                {
                    Vector3 v, n;
                    if (isSkinned)
                    {
                        // Leave in model space; LBS does all transforms via bone matrices.
                        v = positions[i];
                        n = normals is not null ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
                    }
                    else
                    {
                        v = Vector3.Transform(positions[i], worldMat);
                        n = normals is not null
                            ? Vector3.Normalize(Vector3.TransformNormal(normals[i], worldMat))
                            : Vector3.UnitY;
                    }
                    data.BindVertices.Add(v);
                    data.BindNormals.Add(n);
                    data.UVs.Add(uvs is not null ? uvs[i] : Vector2.Zero);
                    data.VertexIsSkinned.Add(isSkinned && joints is not null && weights is not null);

                    if (joints is not null && weights is not null)
                    {
                        var jj = joints[i];
                        var ww = weights[i];
                        data.Influences.Add(new JointInfluence
                        {
                            J0 = (int)jj.X, J1 = (int)jj.Y, J2 = (int)jj.Z, J3 = (int)jj.W,
                            W0 = ww.X, W1 = ww.Y, W2 = ww.Z, W3 = ww.W
                        });
                    }
                    else
                    {
                        data.Influences.Add(null);
                    }
                }

                if (indices is not null)
                {
                    var matIdx = prim.Material?.LogicalIndex ?? 0;
                    for (var i = 0; i + 2 < indices.Count; i += 3)
                    {
                        data.Indices.Add(baseVtx + (int)indices[i]);
                        data.Indices.Add(baseVtx + (int)indices[i + 1]);
                        data.Indices.Add(baseVtx + (int)indices[i + 2]);
                        data.TriMaterial.Add(matIdx);
                    }
                }
            }
        }

        // Animations: only channels targeting joints of the primary skin
        foreach (var anim in model.LogicalAnimations)
        {
            var clip = new AnimClip
            {
                Name = string.IsNullOrEmpty(anim.Name) ? $"<anim #{anim.LogicalIndex}>" : anim.Name,
                Duration = anim.Duration
            };
            foreach (var ch in anim.Channels)
            {
                var node = ch.TargetNode;
                if (!jointNodeToIndex.TryGetValue(node, out var jointIdx)) continue;
                var sampler = ch.GetTranslationSampler() ?? (object?)null;
                var prop = ch.TargetNodePath.ToString().ToLowerInvariant();
                var c = new AnimChannel { JointIndex = jointIdx, Property = prop };
                try
                {
                    if (prop == "translation")
                    {
                        var s = ch.GetTranslationSampler();
                        var kf = s.GetLinearKeys().ToList();
                        c.Times = kf.Select(k => k.Key).ToArray();
                        c.Vec3Values = kf.Select(k => k.Value).ToArray();
                    }
                    else if (prop == "rotation")
                    {
                        var s = ch.GetRotationSampler();
                        var kf = s.GetLinearKeys().ToList();
                        c.Times = kf.Select(k => k.Key).ToArray();
                        c.QuatValues = kf.Select(k => k.Value).ToArray();
                    }
                    else if (prop == "scale")
                    {
                        var s = ch.GetScaleSampler();
                        var kf = s.GetLinearKeys().ToList();
                        c.Times = kf.Select(k => k.Key).ToArray();
                        c.Vec3Values = kf.Select(k => k.Value).ToArray();
                    }
                    else continue;
                }
                catch { continue; }
                if (c.Times.Length > 0) clip.Channels.Add(c);
            }
            if (clip.Channels.Count > 0) data.Animations.Add(clip);
        }

        // BBox
        if (data.BindVertices.Count > 0)
        {
            var min = data.BindVertices[0]; var max = min;
            foreach (var v in data.BindVertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            data.Min = min; data.Max = max;
        }

        return data;

        static IEnumerable<Node> Walk(Node n)
        {
            yield return n;
            foreach (var c in n.VisualChildren) foreach (var x in Walk(c)) yield return x;
        }

        static NodeInfo BuildNodeInfo(Node n, HashSet<int> joints)
        {
            var info = new NodeInfo(string.IsNullOrEmpty(n.Name) ? $"<node #{n.LogicalIndex}>" : n.Name, joints.Contains(n.LogicalIndex));
            foreach (var c in n.VisualChildren) info.Children.Add(BuildNodeInfo(c, joints));
            return info;
        }
    }
}

/// <summary>
/// Bottom timeline: play/pause/reverse, scrubber, primary + secondary clip blend, speed.
/// </summary>
internal sealed class TimelineBar : Border
{
    private readonly ModelData _model;
    private readonly Viewport3D _viewport;
    private readonly ComboBox _clipCombo = new();
    private readonly ComboBox _clip2Combo = new();
    private readonly Slider _scrub = new() { Minimum = 0, Maximum = 1, Value = 0 };
    private readonly Slider _blendSlider = new() { Minimum = 0, Maximum = 1, Value = 0, Width = 100 };
    private readonly Slider _speedSlider = new() { Minimum = 0.1, Maximum = 3, Value = 1, Width = 90 };
    private readonly TextBlock _timeLabel = new() { Foreground = Brush(TextDim), FontFamily = new FontFamily("Menlo"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 110, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _speedLabel = new() { Foreground = Brush(TextDim), FontFamily = new FontFamily("Menlo"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 36 };
    private readonly Button _playBtn;
    private readonly Button _recordBtn;
    private DispatcherTimer? _timer;
    private DateTime _lastTick;
    private bool _scrubbing;

    public TimelineBar(ModelData model, Viewport3D viewport)
    {
        _model = model;
        _viewport = viewport;
        Background = Brush(MenuBackground);
        BorderBrush = Brush(BorderSubtle);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(8, 6);

        var clipNames = model.Animations.Select(a => a.Name).ToList();
        _clipCombo.ItemsSource = clipNames;
        _clipCombo.MinWidth = 160;
        _clipCombo.FontSize = 12;
        if (clipNames.Count > 0) _clipCombo.SelectedIndex = 0;
        _clipCombo.SelectionChanged += (_, _) =>
        {
            _viewport.CurrentClip = _clipCombo.SelectedIndex;
            _scrub.Maximum = Math.Max(0.01, _viewport.CurrentClipDuration);
            _scrub.Value = 0;
            _viewport.CurrentTime = 0;
            _viewport.RequestRedraw();
        };

        var clip2Names = new[] { "(none)" }.Concat(clipNames).ToList();
        _clip2Combo.ItemsSource = clip2Names;
        _clip2Combo.MinWidth = 140;
        _clip2Combo.FontSize = 12;
        _clip2Combo.SelectedIndex = 0;
        _clip2Combo.SelectionChanged += (_, _) =>
        {
            _viewport.SecondClip = _clip2Combo.SelectedIndex - 1;
            _viewport.SecondTime = 0;
            _viewport.RequestRedraw();
        };

        if (clipNames.Count > 0)
        {
            _viewport.CurrentClip = 0;
            _scrub.Maximum = Math.Max(0.01, _viewport.CurrentClipDuration);
        }

        _viewport.ClipsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshClipList();
            if (_viewport.CurrentClip >= 0)
            {
                _clipCombo.SelectedIndex = _viewport.CurrentClip;
                _scrub.Maximum = Math.Max(0.01, _viewport.CurrentClipDuration);
            }
        });

        _playBtn = new Button
        {
            Content = "▶",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 13,
            Padding = new Thickness(10, 4),
            Width = 38
        };
        _playBtn.Click += (_, _) => TogglePlay();

        _recordBtn = new Button
        {
            Content = "● Rec",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 12,
            Padding = new Thickness(10, 4)
        };
        _recordBtn.Click += (_, _) =>
        {
            _viewport.RecordMode = !_viewport.RecordMode;
            _recordBtn.Background = Brush(_viewport.RecordMode ? "#a13030" : FilterBackground);
            _recordBtn.Foreground = Brush(_viewport.RecordMode ? "#ffffff" : TextMuted);
        };

        var newClip = new Button
        {
            Content = "+ New",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        };
        newClip.Click += (_, _) =>
        {
            _viewport.AddEmptyClip("new_clip_" + (_model.Animations.Count + 1), 1.0f);
            RefreshClipList();
            _clipCombo.SelectedIndex = _model.Animations.Count - 1;
        };

        var saveClip = new Button
        {
            Content = "Save…",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        };
        saveClip.Click += async (_, _) => await SaveClipAsync();

        var loadClip = new Button
        {
            Content = "Load…",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        };
        loadClip.Click += async (_, _) => await LoadClipAsync();

        _scrub.SmallChange = 0.01;
        _scrub.AddHandler(InputElement.PointerPressedEvent, (_, _) => _scrubbing = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scrub.AddHandler(InputElement.PointerReleasedEvent, (_, _) => _scrubbing = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scrub.ValueChanged += (_, _) =>
        {
            if (_scrubbing || !IsPlaying)
            {
                _viewport.CurrentTime = (float)_scrub.Value;
                _viewport.RequestRedraw();
                _viewport.NotifyPoseChanged();
                UpdateLabel();
            }
        };

        _blendSlider.ValueChanged += (_, _) => { _viewport.BlendWeight = (float)_blendSlider.Value; _viewport.RequestRedraw(); };
        _speedSlider.ValueChanged += (_, _) =>
        {
            _viewport.PlaySpeed = (float)_speedSlider.Value;
            _speedLabel.Text = _speedSlider.Value.ToString("0.0×");
        };
        _speedLabel.Text = "1.0×";

        var loopChk = new CheckBox { Content = "Loop", IsChecked = true, Foreground = Brush(TextSecondary), VerticalAlignment = VerticalAlignment.Center };
        loopChk.IsCheckedChanged += (_, _) => _viewport.Loop = loopChk.IsChecked == true;

        var revChk = new CheckBox { Content = "Rev", IsChecked = false, Foreground = Brush(TextSecondary), VerticalAlignment = VerticalAlignment.Center };
        revChk.IsCheckedChanged += (_, _) => _viewport.Reverse = revChk.IsChecked == true;

        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        top.Children.Add(_playBtn.At(column: 0));
        top.Children.Add(new TextBlock { Text = "Clip", Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.At(column: 1));
        top.Children.Add(_clipCombo.At(column: 2));
        top.Children.Add(_scrub.At(column: 3));
        top.Children.Add(_timeLabel.At(column: 4));
        top.Children.Add(new TextBlock { Text = "+", Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) }.At(column: 5));
        top.Children.Add(_clip2Combo.At(column: 6));
        top.Children.Add(new TextBlock { Text = "w", Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.At(column: 7));
        top.Children.Add(_blendSlider.At(column: 8));
        top.Children.Add(new TextBlock { Text = "speed", Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }.At(column: 9));
        top.Children.Add(_speedSlider.At(column: 10));
        top.Children.Add(_speedLabel.At(column: 11));
        var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        checks.Children.Add(loopChk);
        checks.Children.Add(revChk);
        top.Children.Add(checks.At(column: 12));

        var authoring = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        authoring.Children.Add(_recordBtn);
        authoring.Children.Add(newClip);
        authoring.Children.Add(saveClip);
        authoring.Children.Add(loadClip);
        authoring.Children.Add(new TextBlock
        {
            Text = "Tip: enable Rec, scrub timeline, edit joint TRS in sidebar → key inserted at current time.",
            Foreground = Brush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(top);
        stack.Children.Add(authoring);
        Child = stack;

        UpdateLabel();
    }

    private bool IsPlaying => _timer?.IsEnabled == true;

    private void TogglePlay()
    {
        if (_model.Animations.Count == 0) return;
        if (IsPlaying) { _timer!.Stop(); _playBtn.Content = "▶"; return; }
        _lastTick = DateTime.UtcNow;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        _playBtn.Content = "❚❚";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (float)(now - _lastTick).TotalSeconds * _viewport.PlaySpeed * (_viewport.Reverse ? -1 : 1);
        _lastTick = now;
        var dur = _viewport.CurrentClipDuration;
        if (dur <= 0) return;

        var t = _viewport.CurrentTime + dt;
        if (_viewport.Loop)
        {
            t = ((t % dur) + dur) % dur;
        }
        else
        {
            if (t > dur) { t = dur; _timer!.Stop(); _playBtn.Content = "▶"; }
            else if (t < 0) { t = 0; _timer!.Stop(); _playBtn.Content = "▶"; }
        }
        _viewport.CurrentTime = t;

        // Advance the second clip independently (same speed, looped)
        if (_viewport.SecondClip >= 0)
        {
            var dur2 = _viewport.SecondClipDuration;
            if (dur2 > 0)
            {
                var t2 = _viewport.SecondTime + dt;
                t2 = ((t2 % dur2) + dur2) % dur2;
                _viewport.SecondTime = t2;
            }
        }

        _scrub.Value = t;
        UpdateLabel();
        _viewport.RequestRedraw();
    }

    private void UpdateLabel()
    {
        _timeLabel.Text = $"{_viewport.CurrentTime:0.00} / {_viewport.CurrentClipDuration:0.00}s";
    }

    private void RefreshClipList()
    {
        var keep = _clipCombo.SelectedIndex;
        _clipCombo.ItemsSource = _model.Animations.Select(a => a.Name).ToList();
        _clipCombo.SelectedIndex = Math.Min(Math.Max(0, keep), _model.Animations.Count - 1);

        var clip2Names = new[] { "(none)" }.Concat(_model.Animations.Select(a => a.Name)).ToList();
        _clip2Combo.ItemsSource = clip2Names;
        _clip2Combo.SelectedIndex = 0;
    }

    private async Task SaveClipAsync()
    {
        if (_viewport.CurrentClip < 0 || _viewport.CurrentClip >= _model.Animations.Count) return;
        var clip = _model.Animations[_viewport.CurrentClip];
        var win = TopLevel.GetTopLevel(this) as Window;
        var file = win is null ? null : await win.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            SuggestedFileName = clip.Name + ".clip.json",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Clip JSON") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        File.WriteAllText(file.Path.LocalPath, MonoForge.Editor.Services.ClipJson.Serialize(clip, _model));
    }

    private async Task LoadClipAsync()
    {
        var win = TopLevel.GetTopLevel(this) as Window;
        if (win is null) return;
        var files = await win.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Clip JSON") { Patterns = ["*.json"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            var json = File.ReadAllText(f.Path.LocalPath);
            var clip = MonoForge.Editor.Services.ClipJson.Deserialize(json, _model);
            _model.Animations.Add(clip);
            RefreshClipList();
            _clipCombo.SelectedIndex = _model.Animations.Count - 1;
        }
        catch { /* ignored */ }
    }
}

/// <summary>
/// Software-rasterized 3D viewport with z-buffer + perspective-correct texture sampling +
/// skeletal animation. Renders into a WriteableBitmap each frame.
/// </summary>
internal sealed class Viewport3D : Border
{
    private readonly ModelData _model;
    private double _yaw = 0.5, _pitch = -0.4, _distance = 4.0;
    private Vector3 _target;
    private Point _lastPointer;
    private bool _orbiting, _panning;
    private readonly Avalonia.Controls.Image _image = new();
    private WriteableBitmap? _frame;
    private int[]? _zBuffer;
    private int _frameW, _frameH;
    private bool _redrawQueued;

    public bool Wireframe { get; set; }
    public bool OverlayWires { get; set; }
    public bool UseTextures { get; set; } = true;
    public bool ShowSkeleton { get; set; } = true;
    public bool ShowNormals { get; set; }
    public int CurrentClip { get; set; } = -1;
    public float CurrentTime { get; set; }
    public bool Loop { get; set; } = true;
    public float CurrentClipDuration => (CurrentClip >= 0 && CurrentClip < _model.Animations.Count) ? _model.Animations[CurrentClip].Duration : 0;

    // Optional second clip for blending
    public int SecondClip { get; set; } = -1;
    public float SecondTime { get; set; }
    public float BlendWeight { get; set; } // 0 = full first, 1 = full second
    public float PlaySpeed { get; set; } = 1.0f;
    public bool Reverse { get; set; }
    public float SecondClipDuration => (SecondClip >= 0 && SecondClip < _model.Animations.Count) ? _model.Animations[SecondClip].Duration : 0;

    public int SelectedJoint { get; private set; } = -1;
    public event Action<int>? JointSelected;
    public event Action? PoseChanged;
    public event Action? ClipsChanged;
    public bool RecordMode { get; set; }
    public void NotifyPoseChanged() => PoseChanged?.Invoke();
    private Matrix4x4[]? _lastJointWorld;
    private Matrix4x4 _lastViewProj;
    private int _lastRenderW, _lastRenderH;

    public Viewport3D(ModelData model)
    {
        _model = model;
        Background = Avalonia.Media.Brush.Parse("#0e0f12");
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        Child = _image;
        _image.Stretch = Stretch.None;
        FrameModel();
        EffectiveViewportChanged += (_, _) => RequestRedraw();
        LayoutUpdated += (_, _) => RequestRedraw();
    }

    public void ResetCamera() { _yaw = 0.5; _pitch = -0.4; _target = Vector3.Zero; FrameModel(); }

    public void FrameModel()
    {
        if (_model.VertexCount == 0) return;
        var center = (_model.Min + _model.Max) * 0.5f;
        var size = (_model.Max - _model.Min).Length();
        _target = center;
        _distance = Math.Max(2.0, size * 1.6);
        RequestRedraw();
    }

    public void RequestRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawQueued = false;
            Render();
        }, DispatcherPriority.Background);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastPointer = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))) _panning = true;
        else if (props.IsRightButtonPressed) _panning = true;
        else
        {
            // Try to pick a joint first (only when skeleton is visible)
            if (ShowSkeleton && TryPickJoint(_lastPointer))
            {
                e.Pointer.Capture(this);
                return;
            }
            _orbiting = true;
        }
        e.Pointer.Capture(this);
    }

    private bool TryPickJoint(Point screen)
    {
        if (_lastJointWorld is null || _lastJointWorld.Length == 0) return false;
        var bounds = Bounds;
        var sx = screen.X / bounds.Width * _lastRenderW;
        var sy = screen.Y / bounds.Height * _lastRenderH;
        var threshold = 10.0;
        var bestIdx = -1;
        var bestDist = threshold;
        for (var i = 0; i < _lastJointWorld.Length; i++)
        {
            var pos = Vector3.Transform(Vector3.Zero, _lastJointWorld[i]);
            if (!Project(pos, _lastViewProj, _lastRenderW, _lastRenderH, out var p)) continue;
            var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        if (bestIdx >= 0)
        {
            SelectedJoint = bestIdx;
            JointSelected?.Invoke(bestIdx);
            RequestRedraw();
            return true;
        }
        // Click on empty area = clear selection
        if (SelectedJoint >= 0)
        {
            SelectedJoint = -1;
            JointSelected?.Invoke(-1);
            RequestRedraw();
        }
        return false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X;
        var dy = p.Y - _lastPointer.Y;
        _lastPointer = p;
        if (_orbiting)
        {
            _yaw -= dx * 0.01;
            _pitch -= dy * 0.01;
            _pitch = Math.Clamp(_pitch, -Math.PI / 2 + 0.05, Math.PI / 2 - 0.05);
            RequestRedraw();
        }
        else if (_panning)
        {
            var (right, up, _) = CameraBasis();
            var scale = _distance * 0.002;
            _target -= right * (float)(dx * scale);
            _target += up * (float)(dy * scale);
            RequestRedraw();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _orbiting = false; _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _distance *= e.Delta.Y > 0 ? 0.9 : 1.1;
        _distance = Math.Clamp(_distance, 0.1, 50000);
        RequestRedraw();
    }

    private (Vector3 right, Vector3 up, Vector3 forward) CameraBasis()
    {
        var f = new Vector3(
            (float)(Math.Cos(_pitch) * Math.Sin(_yaw)),
            (float)Math.Sin(_pitch),
            (float)(Math.Cos(_pitch) * Math.Cos(_yaw)));
        f = Vector3.Normalize(f);
        var r = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, f));
        var u = Vector3.Cross(f, r);
        return (r, u, f);
    }

    private void Render()
    {
        var bounds = Bounds;
        var w = Math.Max(1, (int)bounds.Width);
        var h = Math.Max(1, (int)bounds.Height);
        // Downscale for performance — render at 0.75x and let Image stretch.
        var renderW = Math.Max(1, (int)(w * 0.85));
        var renderH = Math.Max(1, (int)(h * 0.85));
        if (_frame is null || _frameW != renderW || _frameH != renderH)
        {
            _frame = new WriteableBitmap(new PixelSize(renderW, renderH), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _zBuffer = new int[renderW * renderH];
            _frameW = renderW;
            _frameH = renderH;
            _image.Source = _frame;
            _image.Width = w;
            _image.Height = h;
        }

        var (right, up, forward) = CameraBasis();
        var eye = _target + forward * (float)_distance;
        var view = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView((float)(Math.PI / 3.5), (float)renderW / renderH, 0.05f, 10000f);
        var vp = view * proj;

        // Compute current pose (joint world matrices)
        var jointWorld = ComputeJointPose();
        _lastJointWorld = jointWorld;
        _lastViewProj = vp;
        _lastRenderW = renderW;
        _lastRenderH = renderH;

        // Animated vertex positions and normals (LBS)
        var verts = _model.BindVertices;
        var norms = _model.BindNormals;
        Vector3[] animVerts;
        Vector3[] animNorms;
        if (jointWorld is not null && _model.Influences.Count == verts.Count)
        {
            // Precompute skin matrices: skinMatrix[j] = jointWorld[j] * inverseBindMatrix[j]
            var skinMat = new Matrix4x4[jointWorld.Length];
            for (var j = 0; j < jointWorld.Length; j++)
            {
                skinMat[j] = _model.InverseBindMatrices[j] * jointWorld[j];
            }

            animVerts = new Vector3[verts.Count];
            animNorms = new Vector3[verts.Count];
            for (var i = 0; i < verts.Count; i++)
            {
                var infl = _model.Influences[i];
                if (infl is null || !_model.VertexIsSkinned[i])
                {
                    animVerts[i] = verts[i];
                    animNorms[i] = norms[i];
                    continue;
                }
                var v = verts[i];
                var n = norms[i];
                var totalW = infl.W0 + infl.W1 + infl.W2 + infl.W3;
                if (totalW <= 0.0001f) { animVerts[i] = v; animNorms[i] = n; continue; }

                Matrix4x4 acc = default;
                AccBone(ref acc, skinMat, infl.J0, infl.W0);
                AccBone(ref acc, skinMat, infl.J1, infl.W1);
                AccBone(ref acc, skinMat, infl.J2, infl.W2);
                AccBone(ref acc, skinMat, infl.J3, infl.W3);
                animVerts[i] = Vector3.Transform(v, acc);
                animNorms[i] = Vector3.Normalize(Vector3.TransformNormal(n, acc));
            }
        }
        else
        {
            animVerts = verts.ToArray();
            animNorms = norms.ToArray();
        }

        // Render frame
        unsafe
        {
            using var fb = _frame!.Lock();
            var ptr = (uint*)fb.Address;
            var stride = fb.RowBytes / 4;

            // Clear: sky gradient (cool top, dark floor) + z-buffer reset
            for (var y = 0; y < renderH; y++)
            {
                var t = y / (float)renderH;
                var rcol = (byte)(20 + 12 * (1 - t));
                var gcol = (byte)(28 + 14 * (1 - t));
                var bcol = (byte)(36 + 22 * (1 - t));
                var col = (uint)(0xFF << 24 | rcol << 16 | gcol << 8 | bcol);
                for (var x = 0; x < renderW; x++) ptr[y * stride + x] = col;
            }
            Array.Fill(_zBuffer!, int.MaxValue);

            // Project all anim verts to screen + camera-space z (for z-buffer)
            var projected = new (float X, float Y, float InvZ, float Z)[animVerts.Length];
            for (var i = 0; i < animVerts.Length; i++)
            {
                var v = new Vector4(animVerts[i], 1f);
                var p = Vector4.Transform(v, vp);
                if (p.W <= 0.001f) { projected[i] = (0, 0, 0, float.MaxValue); continue; }
                var px = (p.X / p.W + 1) * 0.5f * renderW;
                var py = (1 - (p.Y / p.W + 1) * 0.5f) * renderH;
                projected[i] = (px, py, 1f / p.W, p.Z / p.W);
            }

            var light = Vector3.Normalize(new Vector3(0.35f, 0.75f, 0.55f));
            var light2 = Vector3.Normalize(new Vector3(-0.4f, 0.3f, -0.6f));

            // Rasterize triangles
            for (var t = 0; t < _model.TriangleCount; t++)
            {
                var i0 = _model.Indices[t * 3];
                var i1 = _model.Indices[t * 3 + 1];
                var i2 = _model.Indices[t * 3 + 2];
                var a = projected[i0]; var b = projected[i1]; var c = projected[i2];
                if (a.Z >= float.MaxValue || b.Z >= float.MaxValue || c.Z >= float.MaxValue) continue;

                // Backface cull via screen-space cross
                var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (cross <= 0) continue;

                var matIdx = _model.TriMaterial[t] % _model.Materials.Count;
                var mat = _model.Materials.Count > 0 ? _model.Materials[matIdx] : null;
                var tex = (UseTextures && mat?.BaseColorTexture is not null) ? mat.BaseColorTexture : null;
                var baseR = mat?.BaseColorFactor.X ?? 0.7f;
                var baseG = mat?.BaseColorFactor.Y ?? 0.7f;
                var baseB = mat?.BaseColorFactor.Z ?? 0.7f;
                // Fallback color palette if material has no base color factor distinct
                if (mat is null)
                {
                    var palette = new[] { (0.6f, 0.62f, 0.69f), (0.66f, 0.48f, 0.32f), (0.32f, 0.66f, 0.46f), (0.32f, 0.46f, 0.66f) };
                    var p = palette[matIdx % palette.Length];
                    baseR = p.Item1; baseG = p.Item2; baseB = p.Item3;
                }

                // Face normal in world space (average vertex normals)
                var n0 = animNorms[i0]; var n1 = animNorms[i1]; var n2 = animNorms[i2];
                var fnormal = Vector3.Normalize(n0 + n1 + n2);

                var l1 = Math.Max(0f, Vector3.Dot(fnormal, light));
                var l2 = Math.Max(0f, Vector3.Dot(fnormal, light2)) * 0.4f;
                var ambient = 0.25f;
                var diffuse = ambient + l1 + l2;
                if (diffuse > 1.4f) diffuse = 1.4f;

                // Blinn-Phong specular (per-triangle)
                float specular = 0f;
                if (mat is not null && mat.Roughness < 0.95f)
                {
                    var rough = Math.Clamp(mat.Roughness, 0.04f, 1f);
                    var shininess = MathF.Max(2f, (1f - rough) * (1f - rough) * 256f);
                    // View direction approximated as forward of camera; here we approximate with light reflection
                    var halfDir = Vector3.Normalize(light + Vector3.UnitY);
                    var spec = MathF.Pow(MathF.Max(0f, Vector3.Dot(fnormal, halfDir)), shininess);
                    specular = spec * (0.4f + 0.6f * mat.Metallic);
                }

                var uv0 = _model.UVs[i0]; var uv1 = _model.UVs[i1]; var uv2 = _model.UVs[i2];

                if (!Wireframe)
                {
                    RasterizeTriangle(
                        ptr, stride, renderW, renderH,
                        a, b, c,
                        uv0, uv1, uv2,
                        tex, baseR, baseG, baseB, diffuse, specular);
                }

                if (Wireframe || OverlayWires)
                {
                    var wireColor = Wireframe ? 0xFFAEB8C2 : 0x66FFFFFFu;
                    DrawLine(ptr, stride, renderW, renderH, (int)a.X, (int)a.Y, (int)b.X, (int)b.Y, wireColor);
                    DrawLine(ptr, stride, renderW, renderH, (int)b.X, (int)b.Y, (int)c.X, (int)c.Y, wireColor);
                    DrawLine(ptr, stride, renderW, renderH, (int)c.X, (int)c.Y, (int)a.X, (int)a.Y, wireColor);
                }
            }

            // Skeleton overlay
            if (ShowSkeleton && jointWorld is not null)
            {
                for (var j = 0; j < _model.JointParents.Count; j++)
                {
                    var parentIdx = _model.JointParents[j];
                    if (parentIdx < 0) continue;
                    var a = Vector3.Transform(Vector3.Zero, jointWorld[parentIdx]);
                    var b = Vector3.Transform(Vector3.Zero, jointWorld[j]);
                    if (Project(a, vp, renderW, renderH, out var pa) && Project(b, vp, renderW, renderH, out var pb))
                    {
                        DrawLine(ptr, stride, renderW, renderH, pa.X, pa.Y, pb.X, pb.Y, 0xFFFFD166);
                    }
                }
                // Joint pips
                for (var j = 0; j < (jointWorld?.Length ?? 0); j++)
                {
                    var p = Vector3.Transform(Vector3.Zero, jointWorld![j]);
                    if (Project(p, vp, renderW, renderH, out var sp))
                    {
                        if (j == SelectedJoint)
                            FillRect(ptr, stride, renderW, renderH, sp.X - 4, sp.Y - 4, 8, 8, 0xFFFF6B6B);
                        else
                            FillRect(ptr, stride, renderW, renderH, sp.X - 2, sp.Y - 2, 4, 4, 0xFFFFD166);
                    }
                }
            }

            // Ground grid (after meshes so it's visible above empty space, but z-buffer keeps it behind)
            DrawGroundGrid(ptr, stride, renderW, renderH, vp);

            // Transform gizmo at the selected joint (if any)
            if (SelectedJoint >= 0 && jointWorld is not null && SelectedJoint < jointWorld.Length)
            {
                DrawGizmo(ptr, stride, renderW, renderH, vp, jointWorld[SelectedJoint]);
            }
        }

        _image.InvalidateVisual();
    }

    /// <summary>
    /// Returns the current TRS of a joint accounting for the active clip's samples;
    /// falls back to rest if no clip or no channels for the joint.
    /// </summary>
    public (Vector3 T, Quaternion R, Vector3 S) GetJointCurrentTRS(int idx)
    {
        if (idx < 0 || idx >= _model.JointCount) return (Vector3.Zero, Quaternion.Identity, Vector3.One);
        var t = _model.JointRestT[idx];
        var r = _model.JointRestR[idx];
        var s = _model.JointRestS[idx];
        if (CurrentClip >= 0 && CurrentClip < _model.Animations.Count)
        {
            var clip = _model.Animations[CurrentClip];
            foreach (var ch in clip.Channels.Where(c => c.JointIndex == idx))
            {
                if (ch.Property == "translation") t = SampleVec3(ch, CurrentTime);
                else if (ch.Property == "rotation") r = SampleQuat(ch, CurrentTime);
                else if (ch.Property == "scale") s = SampleVec3(ch, CurrentTime);
            }
        }
        return (t, r, s);
    }

    /// <summary>Inserts or updates a key for the given joint property at the current time.
    /// If there is no active clip, one is created automatically.</summary>
    public void SetKeyAtCurrentTime(int jointIdx, string property, Vector3 vec3Value, Quaternion quatValue)
    {
        if (CurrentClip < 0 || CurrentClip >= _model.Animations.Count)
        {
            // Auto-create a scratch clip so the user can start authoring.
            _model.Animations.Add(new AnimClip { Name = "untitled", Duration = Math.Max(1f, CurrentTime + 0.01f) });
            CurrentClip = _model.Animations.Count - 1;
            ClipsChanged?.Invoke();
        }
        var clip = _model.Animations[CurrentClip];
        var ch = clip.Channels.FirstOrDefault(c => c.JointIndex == jointIdx && c.Property == property);
        if (ch is null)
        {
            ch = new AnimChannel { JointIndex = jointIdx, Property = property };
            clip.Channels.Add(ch);
        }
        if (property == "rotation") ch.SetQuatKey(CurrentTime, quatValue);
        else ch.SetVec3Key(CurrentTime, vec3Value);

        // Extend clip duration if needed
        if (CurrentTime > clip.Duration) clip.Duration = CurrentTime;
        PoseChanged?.Invoke();
        RequestRedraw();
    }

    public void AddEmptyClip(string name, float duration)
    {
        _model.Animations.Add(new AnimClip { Name = name, Duration = duration });
        ClipsChanged?.Invoke();
        PoseChanged?.Invoke();
    }

    private Matrix4x4[]? ComputeJointPose()
    {
        if (_model.JointCount == 0) return null;

        // Start from each joint's bind-pose local TRS (the "rest" pose).
        var local = new (Vector3 T, Quaternion R, Vector3 S)[_model.JointCount];
        for (var i = 0; i < _model.JointCount; i++)
        {
            local[i] = (_model.JointRestT[i], _model.JointRestR[i], _model.JointRestS[i]);
        }

        // Sample primary clip
        if (CurrentClip >= 0 && CurrentClip < _model.Animations.Count)
        {
            var clip = _model.Animations[CurrentClip];
            foreach (var ch in clip.Channels)
            {
                var jp = local[ch.JointIndex];
                if (ch.Property == "translation") jp.T = SampleVec3(ch, CurrentTime);
                else if (ch.Property == "rotation") jp.R = SampleQuat(ch, CurrentTime);
                else if (ch.Property == "scale") jp.S = SampleVec3(ch, CurrentTime);
                local[ch.JointIndex] = jp;
            }
        }

        // Blend in second clip with BlendWeight
        if (SecondClip >= 0 && SecondClip < _model.Animations.Count && BlendWeight > 0)
        {
            var weight = Math.Clamp(BlendWeight, 0, 1);
            var clip = _model.Animations[SecondClip];
            foreach (var ch in clip.Channels)
            {
                var jp = local[ch.JointIndex];
                if (ch.Property == "translation")
                    jp.T = Vector3.Lerp(jp.T, SampleVec3(ch, SecondTime), weight);
                else if (ch.Property == "rotation")
                    jp.R = Quaternion.Slerp(jp.R, SampleQuat(ch, SecondTime), weight);
                else if (ch.Property == "scale")
                    jp.S = Vector3.Lerp(jp.S, SampleVec3(ch, SecondTime), weight);
                local[ch.JointIndex] = jp;
            }
        }

        // Compose locals → world by walking the joint hierarchy (parents come first because
        // SharpGLTF orders skin.GetJoint by depth in practice; if a parent index is greater
        // than child, we fall back to recursive resolution).
        var world = new Matrix4x4[_model.JointCount];
        var resolved = new bool[_model.JointCount];

        Matrix4x4 Resolve(int i)
        {
            if (resolved[i]) return world[i];
            var (t, r, s) = local[i];
            var lm = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
            var parent = _model.JointParents[i];
            world[i] = parent < 0 ? lm : lm * Resolve(parent);
            resolved[i] = true;
            return world[i];
        }
        for (var i = 0; i < _model.JointCount; i++) Resolve(i);
        return world;
    }

    private static Vector3 SampleVec3(AnimChannel ch, float t)
    {
        if (ch.Vec3Values.Length == 0) return Vector3.Zero;
        if (ch.Vec3Values.Length == 1) return ch.Vec3Values[0];
        var times = ch.Times;
        if (t <= times[0]) return ch.Vec3Values[0];
        if (t >= times[^1]) return ch.Vec3Values[^1];
        for (var i = 0; i < times.Length - 1; i++)
        {
            if (t >= times[i] && t <= times[i + 1])
            {
                var u = (t - times[i]) / (times[i + 1] - times[i]);
                return Vector3.Lerp(ch.Vec3Values[i], ch.Vec3Values[i + 1], u);
            }
        }
        return ch.Vec3Values[^1];
    }

    private static Quaternion SampleQuat(AnimChannel ch, float t)
    {
        if (ch.QuatValues.Length == 0) return Quaternion.Identity;
        if (ch.QuatValues.Length == 1) return ch.QuatValues[0];
        var times = ch.Times;
        if (t <= times[0]) return ch.QuatValues[0];
        if (t >= times[^1]) return ch.QuatValues[^1];
        for (var i = 0; i < times.Length - 1; i++)
        {
            if (t >= times[i] && t <= times[i + 1])
            {
                var u = (t - times[i]) / (times[i + 1] - times[i]);
                return Quaternion.Normalize(Quaternion.Slerp(ch.QuatValues[i], ch.QuatValues[i + 1], u));
            }
        }
        return ch.QuatValues[^1];
    }

    private static void AccBone(ref Matrix4x4 acc, Matrix4x4[] joints, int idx, float w)
    {
        if (w <= 0 || idx < 0 || idx >= joints.Length) return;
        var m = joints[idx];
        acc.M11 += m.M11 * w; acc.M12 += m.M12 * w; acc.M13 += m.M13 * w; acc.M14 += m.M14 * w;
        acc.M21 += m.M21 * w; acc.M22 += m.M22 * w; acc.M23 += m.M23 * w; acc.M24 += m.M24 * w;
        acc.M31 += m.M31 * w; acc.M32 += m.M32 * w; acc.M33 += m.M33 * w; acc.M34 += m.M34 * w;
        acc.M41 += m.M41 * w; acc.M42 += m.M42 * w; acc.M43 += m.M43 * w; acc.M44 += m.M44 * w;
    }

    // ---------- Rasterizer ----------

    private unsafe void RasterizeTriangle(uint* ptr, int stride, int w, int h,
        (float X, float Y, float InvZ, float Z) a,
        (float X, float Y, float InvZ, float Z) b,
        (float X, float Y, float InvZ, float Z) c,
        Vector2 uv0, Vector2 uv1, Vector2 uv2,
        Bitmap? tex, float baseR, float baseG, float baseB, float diffuse, float specular = 0f)
    {
        // Texture pixels (read once)
        int texW = 0, texH = 0;
        uint[]? texPixels = null;
        if (tex is not null)
        {
            texW = tex.PixelSize.Width;
            texH = tex.PixelSize.Height;
            texPixels = GetTexturePixels(tex);
        }

        var minX = (int)Math.Floor(Math.Max(0, Math.Min(Math.Min(a.X, b.X), c.X)));
        var maxX = (int)Math.Ceiling(Math.Min(w - 1, Math.Max(Math.Max(a.X, b.X), c.X)));
        var minY = (int)Math.Floor(Math.Max(0, Math.Min(Math.Min(a.Y, b.Y), c.Y)));
        var maxY = (int)Math.Ceiling(Math.Min(h - 1, Math.Max(Math.Max(a.Y, b.Y), c.Y)));

        var areaDouble = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(areaDouble) < 0.0001f) return;
        var invArea = 1f / areaDouble;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * invArea;
                var w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * invArea;
                var w2 = 1f - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;

                var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                var zKey = (int)(z * 1000000);
                var zi = y * w + x;
                if (zKey >= _zBuffer![zi]) continue;
                _zBuffer[zi] = zKey;

                float r, g, bb;
                if (texPixels is not null)
                {
                    // Perspective-correct UV
                    var oneOverZ = w0 * a.InvZ + w1 * b.InvZ + w2 * c.InvZ;
                    var u = (w0 * uv0.X * a.InvZ + w1 * uv1.X * b.InvZ + w2 * uv2.X * c.InvZ) / oneOverZ;
                    var vv = (w0 * uv0.Y * a.InvZ + w1 * uv1.Y * b.InvZ + w2 * uv2.Y * c.InvZ) / oneOverZ;
                    u = u - (float)Math.Floor(u);
                    vv = vv - (float)Math.Floor(vv);
                    var tx = Math.Clamp((int)(u * texW), 0, texW - 1);
                    var ty = Math.Clamp((int)(vv * texH), 0, texH - 1);
                    var sample = texPixels[ty * texW + tx];
                    r = ((sample >> 16) & 0xFF) / 255f * baseR;
                    g = ((sample >> 8) & 0xFF) / 255f * baseG;
                    bb = (sample & 0xFF) / 255f * baseB;
                }
                else
                {
                    r = baseR; g = baseG; bb = baseB;
                }

                r = r * diffuse + specular;
                g = g * diffuse + specular;
                bb = bb * diffuse + specular;
                var br = (byte)Math.Clamp(r * 255f, 0, 255);
                var bg = (byte)Math.Clamp(g * 255f, 0, 255);
                var bb2 = (byte)Math.Clamp(bb * 255f, 0, 255);
                ptr[zi] = (uint)(0xFF << 24 | (br << 16) | (bg << 8) | bb2);
            }
        }
    }

    private static readonly Dictionary<Bitmap, uint[]> _texturePixelCache = new();
    private static uint[] GetTexturePixels(Bitmap bmp)
    {
        if (_texturePixelCache.TryGetValue(bmp, out var cached)) return cached;
        var w = bmp.PixelSize.Width;
        var h = bmp.PixelSize.Height;
        var pixels = new uint[w * h];
        unsafe
        {
            fixed (uint* dst = pixels)
            {
                bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)dst, w * h * 4, w * 4);
            }
        }
        _texturePixelCache[bmp] = pixels;
        return pixels;
    }

    private static unsafe void DrawLine(uint* ptr, int stride, int w, int h, int x0, int y0, int x1, int y1, uint color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h) ptr[y0 * stride + x0] = color;
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static unsafe void FillRect(uint* ptr, int stride, int w, int h, int x, int y, int rw, int rh, uint color)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(h, y + rh); yy++)
            for (var xx = Math.Max(0, x); xx < Math.Min(w, x + rw); xx++)
                ptr[yy * stride + xx] = color;
    }

    private static bool Project(Vector3 world, Matrix4x4 vp, int w, int h, out (int X, int Y) screen)
    {
        var p = Vector4.Transform(new Vector4(world, 1), vp);
        if (p.W <= 0.001f) { screen = (0, 0); return false; }
        screen = ((int)((p.X / p.W + 1) * 0.5f * w), (int)((1 - (p.Y / p.W + 1) * 0.5f) * h));
        return true;
    }

    private static unsafe void DrawGizmo(uint* ptr, int stride, int w, int h, Matrix4x4 vp, Matrix4x4 world)
    {
        // Take the joint's world matrix axes and draw axes scaled to a fixed visual size.
        var origin = Vector3.Transform(Vector3.Zero, world);
        // Extract rotation only (no scale, no translation). For simplicity use world matrix basis vectors.
        var xAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, world));
        var yAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, world));
        var zAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        var size = 0.4f;
        if (!Project(origin, vp, w, h, out var o)) return;
        if (Project(origin + xAxis * size, vp, w, h, out var px)) { DrawLine(ptr, stride, w, h, o.X, o.Y, px.X, px.Y, 0xFFFF4040); FillRect(ptr, stride, w, h, px.X - 3, px.Y - 3, 6, 6, 0xFFFF4040); }
        if (Project(origin + yAxis * size, vp, w, h, out var py)) { DrawLine(ptr, stride, w, h, o.X, o.Y, py.X, py.Y, 0xFF40FF40); FillRect(ptr, stride, w, h, py.X - 3, py.Y - 3, 6, 6, 0xFF40FF40); }
        if (Project(origin + zAxis * size, vp, w, h, out var pz)) { DrawLine(ptr, stride, w, h, o.X, o.Y, pz.X, pz.Y, 0xFF4080FF); FillRect(ptr, stride, w, h, pz.X - 3, pz.Y - 3, 6, 6, 0xFF4080FF); }
        FillRect(ptr, stride, w, h, o.X - 3, o.Y - 3, 6, 6, 0xFFFFFFFF);
    }

    private static unsafe void DrawGroundGrid(uint* ptr, int stride, int w, int h, Matrix4x4 vp)
    {
        for (var i = -10; i <= 10; i++)
        {
            var color = i == 0 ? 0xFF4A5260u : 0xFF252A33u;
            if (Project(new Vector3(i, 0, -10), vp, w, h, out var a) && Project(new Vector3(i, 0, 10), vp, w, h, out var b))
                DrawLine(ptr, stride, w, h, a.X, a.Y, b.X, b.Y, color);
            if (Project(new Vector3(-10, 0, i), vp, w, h, out var c) && Project(new Vector3(10, 0, i), vp, w, h, out var d))
                DrawLine(ptr, stride, w, h, c.X, c.Y, d.X, d.Y, color);
        }
    }
}
