using System.Windows;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>Modal dialog showing the descriptive fields of the patch under review.</summary>
public partial class PatchInfoWindow : Window
{
    public PatchInfoWindow(PatchReviewViewModel review)
    {
        InitializeComponent();
        DataContext = review ?? throw new ArgumentNullException(nameof(review));
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
