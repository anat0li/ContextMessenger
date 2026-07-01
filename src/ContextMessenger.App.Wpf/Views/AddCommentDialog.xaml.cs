using System.Windows;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>Modal dialog to enter a reviewer comment anchored to a file position.</summary>
public partial class AddCommentDialog : Window
{
    public AddCommentDialog(string prompt, string? checkboxLabel = null, bool checkboxInitial = false)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        if (!string.IsNullOrWhiteSpace(checkboxLabel))
        {
            OptionCheckBox.Content = checkboxLabel;
            OptionCheckBox.IsChecked = checkboxInitial;
            OptionCheckBox.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => CommentBox.Focus();
    }

    public string CommentText => CommentBox.Text;

    public bool CheckBoxChecked => OptionCheckBox.IsChecked == true;

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommentBox.Text))
            return;

        DialogResult = true;
    }
}
