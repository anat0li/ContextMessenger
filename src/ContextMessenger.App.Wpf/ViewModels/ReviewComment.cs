using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// A reviewer comment anchored to a file position, shown in the Comments tab and delivered to the
/// model on Send. It is a thread: the reviewer's message starts it, the model answers by
/// <see cref="Id"/>, and the reviewer can respond again. <see cref="Pending"/> is true while there
/// is a reviewer message not yet delivered.
/// </summary>
public sealed partial class ReviewComment : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private CommentAnchorStatus _anchorStatus = CommentAnchorStatus.Current;

    public string AnchorText { get; set; } = "";

    public IReadOnlyList<string> BeforeContext { get; set; } = [];

    public IReadOnlyList<string> AfterContext { get; set; } = [];

    public ObservableCollection<CommentMessage> Messages { get; } = new();

    /// <summary>True while the reviewer has a message that has not been sent to the model yet.</summary>
    [ObservableProperty]
    private bool _pending;

    /// <summary>True when this thread is an unresolved issue that blocks accepting the patch.</summary>
    [ObservableProperty]
    private bool _openIssue;

    /// <summary>The original comment text, used as the thread header.</summary>
    public string FirstText => Messages.Count > 0 ? Messages[0].Text : "";

    /// <summary>The latest reviewer message, delivered to the model on Send.</summary>
    public string LatestReviewerText
    {
        get
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].Author == CommentAuthor.Reviewer)
                    return Messages[i].Text;
            }

            return "";
        }
    }

    public bool HasAnchor => !string.IsNullOrWhiteSpace(Path) && Line > 0;

    public string Location => HasAnchor ? $"{Path} ({Line})" : "General";

    public bool HasAnchorWarning => AnchorStatus is not CommentAnchorStatus.Current;

    public string AnchorStatusText => AnchorStatus switch
    {
        CommentAnchorStatus.Moved => "Anchor moved after patch changes.",
        CommentAnchorStatus.Changed => "Anchor content changed; verify before sending.",
        CommentAnchorStatus.Missing => "Anchor file is no longer available.",
        CommentAnchorStatus.Deleted => "Anchor file was deleted by the patch.",
        _ => "",
    };

    partial void OnPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasAnchor));
        OnPropertyChanged(nameof(Location));
    }

    partial void OnLineChanged(int value)
    {
        OnPropertyChanged(nameof(HasAnchor));
        OnPropertyChanged(nameof(Location));
    }

    partial void OnAnchorStatusChanged(CommentAnchorStatus value)
    {
        OnPropertyChanged(nameof(HasAnchorWarning));
        OnPropertyChanged(nameof(AnchorStatusText));
    }
}

public enum CommentAnchorStatus
{
    Current,
    Moved,
    Changed,
    Missing,
    Deleted,
}
