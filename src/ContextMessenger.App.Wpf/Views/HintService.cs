using System.Windows;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>
/// Attached property carrying a long descriptive hint for a control. MainWindow wires
/// class handlers that surface the hint of the hovered button in the status bar.
/// </summary>
public static class HintService
{
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached(
            "Description",
            typeof(string),
            typeof(HintService),
            new PropertyMetadata(null));

    public static void SetDescription(DependencyObject element, string? value) =>
        element.SetValue(DescriptionProperty, value);

    public static string? GetDescription(DependencyObject element) =>
        (string?)element.GetValue(DescriptionProperty);
}
