using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.App.Wpf.Views;

namespace ContextMessenger.App.Wpf;

public partial class MainWindow : Window
{
    private const double TailTolerance = 24;
    private static readonly Regex EntryHeader = new(
        @"^\S.*\[(?<kind>Info|Request|Response|Warning|Error|Automation)\]\s+",
        RegexOptions.Compiled);

    static MainWindow()
    {
        // Surface each toolbar button's long hint in the status bar while hovered.
        EventManager.RegisterClassHandler(typeof(ButtonBase), Mouse.MouseEnterEvent,
            new MouseEventHandler(OnHintEnter), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), Mouse.MouseLeaveEvent,
            new MouseEventHandler(OnHintLeave), handledEventsToo: true);
    }

    public MainWindow()
    {
        InitializeComponent();
    }

    private static void OnHintEnter(object sender, MouseEventArgs e)
    {
        if (sender is DependencyObject element &&
            HintService.GetDescription(element) is { Length: > 0 } description &&
            Window.GetWindow(element) is { DataContext: MainViewModel vm })
        {
            vm.StatusHint = description;
        }
    }

    private static void OnHintLeave(object sender, MouseEventArgs e)
    {
        if (sender is DependencyObject element &&
            Window.GetWindow(element) is { DataContext: MainViewModel vm })
        {
            vm.StatusHint = "";
        }
    }

    private void InfoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { PatchReview: { } review })
            return;

        new PatchInfoWindow(review) { Owner = this }.ShowDialog();
    }

    private void ReviewTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is TreeView { DataContext: PatchReviewViewModel review })
            review.SelectNode(e.NewValue as PatchTreeNode);
    }

    private void DiffView_OnAddCommentRequested(object? sender, int line)
    {
        if (DataContext is not MainViewModel { PatchReview: { SelectedFile: { } file } review })
            return;

        var dialog = new AddCommentDialog($"Comment on {file.Path} (line {line})", "Open issue") { Owner = this };
        if (dialog.ShowDialog() == true)
            review.AddComment(line, dialog.CommentText, dialog.CheckBoxChecked);
    }

    private void RespondComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ReviewComment comment } ||
            DataContext is not MainViewModel { PatchReview: { } review })
            return;

        var dialog = new AddCommentDialog(
            $"Respond to comment {comment.Id} ({comment.Location})",
            comment.OpenIssue ? "Open issue" : null,
            comment.OpenIssue) { Owner = this };
        if (dialog.ShowDialog() == true)
            review.RespondToComment(comment, dialog.CommentText, comment.OpenIssue && !dialog.CheckBoxChecked);
    }

    private void LogRichTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox richTextBox)
            return;

        richTextBox.Tag = true;
        AttachLogSource(richTextBox);
        RenderInitialLog(richTextBox);
        richTextBox.Dispatcher.BeginInvoke(richTextBox.ScrollToEnd, DispatcherPriority.Background);
    }

    private void LogRichTextBox_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is RichTextBox richTextBox)
            DetachLogSource(richTextBox);
    }

    private void LogRichTextBox_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not RichTextBox richTextBox)
            return;

        if (e.OldValue is ProcessingLoopViewModel oldLoop)
            Unsubscribe(oldLoop);

        // Selecting a log tab should land on the latest entries, not the top of a freshly rebuilt
        // document. Reset the auto-follow flag so RenderInitialLog scrolls to the end.
        richTextBox.Tag = true;
        AttachLogSource(richTextBox);
        RenderInitialLog(richTextBox);
    }

    private void LogRichTextBox_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not RichTextBox richTextBox)
            return;

        richTextBox.Tag = IsNearBottom(e);
    }

    private static void AttachLogSource(RichTextBox richTextBox)
    {
        if (richTextBox.DataContext is not ProcessingLoopViewModel loop)
            return;

        Unsubscribe(loop);
        loop.Logs.CollectionChanged += OnLogsCollectionChanged;
    }

    private static void DetachLogSource(RichTextBox richTextBox)
    {
        if (richTextBox.DataContext is ProcessingLoopViewModel loop)
            Unsubscribe(loop);
    }

    private static void Unsubscribe(ProcessingLoopViewModel loop)
    {
        loop.Logs.CollectionChanged -= OnLogsCollectionChanged;
    }

    private static void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var richTextBox in FindVisualChildren<RichTextBox>(Application.Current.MainWindow))
        {
            if (richTextBox.DataContext is ProcessingLoopViewModel loop &&
                ReferenceEquals(loop.Logs, sender))
            {
                ApplyLogChange(richTextBox, loop, e);
            }
        }
    }

    private static void ApplyLogChange(RichTextBox richTextBox, ProcessingLoopViewModel loop, NotifyCollectionChangedEventArgs e)
    {
        var shouldScrollToEnd = richTextBox.Tag is not false;

        if (e.Action == NotifyCollectionChangedAction.Replace &&
            e.NewItems?.Count == 1 &&
            e.NewStartingIndex >= 0)
        {
            ReplaceParagraph(richTextBox.Document, e.NewStartingIndex, loop.FormatForDisplay((LogEntry)e.NewItems[0]!));
        }
        else if (e.Action == NotifyCollectionChangedAction.Add &&
                 e.NewItems is not null)
        {
            foreach (LogEntry entry in e.NewItems)
                AddParagraph(richTextBox.Document, loop.FormatForDisplay(entry), entry.Kind);
        }
        else
        {
            RenderInitialLog(richTextBox);
            return;
        }

        if (shouldScrollToEnd)
            richTextBox.Dispatcher.BeginInvoke(richTextBox.ScrollToEnd, DispatcherPriority.Background);
    }

    private static void RenderInitialLog(RichTextBox richTextBox)
    {
        if (richTextBox.DataContext is not ProcessingLoopViewModel loop)
            return;

        var shouldScrollToEnd = richTextBox.Tag is not false;
        var document = CreateDocument(richTextBox);

        if (!string.IsNullOrEmpty(loop.LoadedLogText))
            AddLoadedHistory(document, loop.LoadedLogText);

        foreach (var entry in loop.Logs)
            AddParagraph(document, loop.FormatForDisplay(entry), entry.Kind);

        richTextBox.Document = document;

        if (shouldScrollToEnd)
            richTextBox.Dispatcher.BeginInvoke(richTextBox.ScrollToEnd, DispatcherPriority.Background);
    }

    private static FlowDocument CreateDocument(RichTextBox richTextBox) => new()
    {
        PagePadding = new Thickness(0),
        FontFamily = richTextBox.FontFamily,
        FontSize = richTextBox.FontSize,
        LineHeight = double.NaN,
    };

    private static void ReplaceParagraph(FlowDocument document, int structuredLogIndex, string text)
    {
        var paragraph = GetStructuredParagraph(document, structuredLogIndex);
        if (paragraph is null)
            return;

        paragraph.Inlines.Clear();
        paragraph.Inlines.Add(new Run(text));
    }

    private static Paragraph? GetStructuredParagraph(FlowDocument document, int structuredLogIndex)
    {
        var index = 0;
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.Tag is not LogEntryKind)
                continue;

            if (index == structuredLogIndex)
                return paragraph;

            index++;
        }

        return null;
    }

    private static void AddParagraph(FlowDocument document, string text, LogEntryKind kind)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Tag = kind,
            Margin = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Background = BackgroundFor(kind),
            Foreground = ForegroundFor(kind),
        };

        document.Blocks.Add(paragraph);
    }

    private static void AddLoadedHistory(FlowDocument document, string text)
    {
        var current = new StringBuilder();
        var currentKind = LogEntryKind.Info;

        foreach (var line in text.Split([Environment.NewLine], StringSplitOptions.None))
        {
            var match = EntryHeader.Match(line);
            if (match.Success && current.Length > 0)
            {
                AddLoadedParagraph(document, current.ToString(), currentKind);
                current.Clear();
            }

            if (match.Success && Enum.TryParse(match.Groups["kind"].Value, out LogEntryKind parsedKind))
                currentKind = parsedKind;

            if (current.Length > 0)
                current.AppendLine();
            current.Append(line);
        }

        if (current.Length > 0)
            AddLoadedParagraph(document, current.ToString(), currentKind);
    }

    private static void AddLoadedParagraph(FlowDocument document, string text, LogEntryKind kind)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Background = BackgroundFor(kind),
            Foreground = ForegroundFor(kind),
        };

        document.Blocks.Add(paragraph);
    }

    private static Brush BackgroundFor(LogEntryKind kind) => kind switch
    {
        LogEntryKind.Request => new SolidColorBrush(Color.FromRgb(238, 246, 255)),
        LogEntryKind.Response => new SolidColorBrush(Color.FromRgb(239, 250, 241)),
        LogEntryKind.Warning => new SolidColorBrush(Color.FromRgb(255, 248, 197)),
        LogEntryKind.Error => new SolidColorBrush(Color.FromRgb(255, 235, 233)),
        LogEntryKind.Automation => new SolidColorBrush(Color.FromRgb(246, 248, 250)),
        _ => Brushes.Transparent,
    };

    private static Brush ForegroundFor(LogEntryKind kind) => kind switch
    {
        LogEntryKind.Warning => new SolidColorBrush(Color.FromRgb(154, 103, 0)),
        LogEntryKind.Error => new SolidColorBrush(Color.FromRgb(130, 30, 30)),
        LogEntryKind.Automation => new SolidColorBrush(Color.FromRgb(87, 96, 106)),
        _ => Brushes.Black,
    };

    private static bool IsNearBottom(ScrollChangedEventArgs e) =>
        e.ExtentHeight <= e.ViewportHeight ||
        e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - TailTolerance;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
            yield break;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
