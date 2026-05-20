using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MonoForge.Editor.Views;

internal static class ControlExtensions
{
    public static T At<T>(this T control, int row = 0, int column = 0) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    public static T WithMargin<T>(this T control, double left, double top, double right, double bottom) where T : Control
    {
        control.Margin = new Thickness(left, top, right, bottom);
        return control;
    }

    public static Button OnClick(this Button button, EventHandler<RoutedEventArgs> click)
    {
        button.Click += click;
        return button;
    }
}
