using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>Maps a patch file operation to a status text colour: create=green, replace=blue, delete=red.</summary>
public sealed class OperationToBrushConverter : IValueConverter
{
    private static readonly Brush Create = Frozen(Color.FromRgb(26, 127, 55));   // green
    private static readonly Brush Replace = Frozen(Color.FromRgb(9, 105, 218));  // blue
    private static readonly Brush Delete = Frozen(Color.FromRgb(207, 34, 46));   // red
    private static readonly Brush Default = Frozen(Color.FromRgb(36, 41, 47));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "create" => Create,
            "replace" => Replace,
            "delete" => Delete,
            _ => Default,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
