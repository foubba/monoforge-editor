using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

/// <summary>
/// Defines one action that can be invoked from the command palette.
/// Category is shown as a left-side label, Shortcut as a right-side hint.
/// </summary>
public sealed record EditorCommand(string Name, string Category, string Shortcut, Action Execute);

/// <summary>
/// VS Code-style command palette (⌘⇧P). Fuzzy-matches the user's query against the
/// command name + category and executes the chosen command on Enter / double-click.
/// </summary>
public sealed class CommandPaletteWindow : Window
{
    private readonly List<EditorCommand> _all;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();

    public CommandPaletteWindow(IEnumerable<EditorCommand> commands)
    {
        _all = commands.ToList();

        Title = "Command Palette";
        Width = 720; Height = 460;
        Background = Brush(MenuBackground);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.BorderOnly;

        _search.Watermark = "Type a command…";
        _search.Background = Brush(InputBackground);
        _search.Foreground = Brush(TextSecondary);
        _search.BorderBrush = Brush(BorderColor);
        _search.FontFamily = new FontFamily(MonoFont);
        _search.FontSize = 14;
        _search.Padding = new Thickness(12, 8);
        _search.Margin = new Thickness(12, 12, 12, 6);

        _list.Background = Brush(MenuBackground);
        _list.Foreground = Brush(TextSecondary);
        _list.FontFamily = new FontFamily(MonoFont);
        _list.FontSize = 12;
        _list.Margin = new Thickness(12, 0, 12, 12);
        _list.BorderThickness = new Thickness(0);
        _list.ItemTemplate = new FuncDataTemplate<EditorCommand>((cmd, _) => BuildRow(cmd), supportsRecycling: true);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(_search.At(row: 0));
        root.Children.Add(_list.At(row: 1));
        Content = root;

        _search.TextChanged += (_, _) => RebuildList(_search.Text ?? "");
        _search.KeyDown += OnSearchKey;
        _list.DoubleTapped += (_, _) => Accept();

        Opened += (_, _) => _search.Focus();
        RebuildList("");
    }

    private static Control BuildRow(EditorCommand cmd)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,Auto"),
            ColumnSpacing = 10,
            Height = 24
        };
        var cat = new TextBlock
        {
            Text = cmd.Category,
            Foreground = Brush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = cmd.Name,
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var sc = new TextBlock
        {
            Text = cmd.Shortcut,
            Foreground = Brush(TextMuted),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        grid.Children.Add(cat.At(column: 0));
        grid.Children.Add(name.At(column: 1));
        grid.Children.Add(sc.At(column: 2));
        return grid;
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Down)
        {
            _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
            _list.ScrollIntoView(_list.SelectedIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
            _list.ScrollIntoView(_list.SelectedIndex);
            e.Handled = true;
        }
    }

    private void Accept()
    {
        if (_list.SelectedItem is EditorCommand cmd)
        {
            Close();
            try { cmd.Execute(); } catch { /* swallow to keep the editor alive */ }
        }
    }

    private void RebuildList(string query)
    {
        IEnumerable<EditorCommand> ranked;
        if (string.IsNullOrWhiteSpace(query))
        {
            ranked = _all;
        }
        else
        {
            ranked = _all
                .Select(c => (Cmd: c, Score: FuzzyScore(c.Name + " " + c.Category, query)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Cmd.Name.Length)
                .Select(x => x.Cmd);
        }
        _list.ItemsSource = ranked.ToList();
        if (_list.ItemCount > 0) _list.SelectedIndex = 0;
    }

    /// <summary>Subsequence fuzzy match with bonus for streaks (matches QuickOpen's algo).</summary>
    private static int FuzzyScore(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 1;
        var ti = 0; var qi = 0; var score = 0; var streak = 0;
        text = text.ToLowerInvariant();
        query = query.ToLowerInvariant();
        while (ti < text.Length && qi < query.Length)
        {
            if (text[ti] == query[qi])
            {
                score += 1 + streak;
                streak++;
                qi++;
            }
            else
            {
                streak = 0;
            }
            ti++;
        }
        return qi == query.Length ? score : 0;
    }
}
