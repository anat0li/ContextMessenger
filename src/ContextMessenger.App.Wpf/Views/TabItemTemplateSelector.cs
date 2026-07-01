using System.Windows;
using System.Windows.Controls;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>
/// Picks a tab header/content template based on the tab item's type: the patch
/// <see cref="PatchReviewViewModel"/> review tab versus a normal
/// <see cref="ProcessingLoopViewModel"/> log tab. Two instances are used — one for the tab
/// strip header, one for the tab body — each supplied with the matching template pair.
/// </summary>
public sealed class TabItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LoopTemplate { get; set; }

    public DataTemplate? ReviewTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is PatchReviewViewModel ? ReviewTemplate : LoopTemplate;
}
