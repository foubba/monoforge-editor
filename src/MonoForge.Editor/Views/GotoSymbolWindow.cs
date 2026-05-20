using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

public sealed class GotoSymbolWindow : Window
{
    public sealed record Symbol(string Name, int LineNumber, string Kind);

    private readonly List<Symbol> _all;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly Action<int> _onPick;

    public GotoSymbolWindow(IEnumerable<Symbol> symbols, Action<int> onPick)
    {
        _all = symbols.ToList();
        _onPick = onPick;

        Title = "Goto Symbol";
        Width = 560; Height = 420;
        Background = Brush(MenuBackground);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.BorderOnly;

        _search.Watermark = "Type a symbol name…";
        _search.Background = Brush(InputBackground);
        _search.Foreground = Brush(TextSecondary);
        _search.BorderBrush = Brush(BorderColor);
        _search.FontFamily = new FontFamily(MonoFont);
        _search.FontSize = 13;
        _search.Padding = new Thickness(10, 6);
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

        _search.TextChanged += (_, _) => Rebuild(_search.Text ?? "");
        _search.KeyDown += OnKey;
        _list.DoubleTapped += (_, _) => Accept();

        Opened += (_, _) => _search.Focus();
        Rebuild("");
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Down) { _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1); e.Handled = true; }
        else if (e.Key == Key.Up) { _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; }
    }

    private void Accept()
    {
        if (_list.SelectedItem is SymbolDisplay s) { _onPick(s.Line); Close(); }
    }

    private void Rebuild(string query)
    {
        IEnumerable<Symbol> filtered = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        _list.ItemsSource = filtered.Select(s => new SymbolDisplay($"{s.Kind,-8} {s.Name}    :{s.LineNumber}", s.LineNumber)).ToList();
        if (_list.ItemCount > 0) _list.SelectedIndex = 0;
    }

    private sealed record SymbolDisplay(string Display, int Line)
    {
        public override string ToString() => Display;
    }

    public static List<Symbol> Extract(string text, string language)
    {
        var symbols = new List<Symbol>();
        var lines = text.Split('\n');
        Regex[] patterns = language switch
        {
            "csharp" or "cs" => new[]
            {
                new Regex(@"^\s*(?:public|private|internal|protected|static|sealed|abstract|partial|\s)*\s+(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled),
                new Regex(@"^\s*(?:public|private|internal|protected|static|virtual|override|async|sealed|\s)+\s+[A-Za-z_<>\[\],?\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled),
            },
            "python" or "py" => new[]
            {
                new Regex(@"^\s*def\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled),
                new Regex(@"^\s*class\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled),
            },
            "javascript" or "js" or "typescript" or "ts" => new[]
            {
                new Regex(@"^\s*(?:export\s+)?(?:async\s+)?function\s+([A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled),
                new Regex(@"^\s*(?:export\s+)?class\s+([A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled),
                new Regex(@"^\s*([A-Za-z_$][A-Za-z0-9_$]*)\s*[:=]\s*(?:async\s+)?(?:function|\()", RegexOptions.Compiled),
            },
            _ => new[]
            {
                new Regex(@"^\s*(?:def|function|class|fn|func|sub)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)
            }
        };

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var p in patterns)
            {
                var m = p.Match(lines[i]);
                if (m.Success && m.Groups[1].Success)
                {
                    var name = m.Groups[1].Value;
                    var kind = lines[i].Contains("class", StringComparison.Ordinal) ? "class"
                        : lines[i].Contains("interface", StringComparison.Ordinal) ? "iface"
                        : lines[i].Contains("struct", StringComparison.Ordinal) ? "struct"
                        : lines[i].Contains("enum", StringComparison.Ordinal) ? "enum"
                        : "fn";
                    symbols.Add(new Symbol(name, i + 1, kind));
                    break;
                }
            }
        }
        return symbols;
    }
}
