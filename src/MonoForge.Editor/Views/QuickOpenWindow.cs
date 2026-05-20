using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

public sealed class QuickOpenWindow : Window
{
    private readonly List<string> _all;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly Action<string> _onPick;

    public QuickOpenWindow(IEnumerable<string> projectFiles, Action<string> onPick)
    {
        _all = projectFiles.ToList();
        _onPick = onPick;

        Title = "Open File";
        Width = 640; Height = 420;
        Background = Brush(MenuBackground);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.BorderOnly;

        _search.Watermark = "Type to filter files…";
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

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Down)
        {
            _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
            e.Handled = true;
        }
    }

    private void Accept()
    {
        if (_list.SelectedItem is string path)
        {
            _onPick(path);
            Close();
        }
    }

    private void RebuildList(string query)
    {
        IEnumerable<string> ranked;
        if (string.IsNullOrWhiteSpace(query))
        {
            ranked = _all.Take(200);
        }
        else
        {
            ranked = _all
                .Select(p => (Path: p, Score: FuzzyScore(System.IO.Path.GetFileName(p), query)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path.Length)
                .Take(200)
                .Select(x => x.Path);
        }
        _list.ItemsSource = ranked.ToList();
        if (_list.ItemCount > 0) _list.SelectedIndex = 0;
    }

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
