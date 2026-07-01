using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ContextMessenger.App.Wpf.ViewModels;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace ContextMessenger.App.Wpf.Views;

/// <summary>
/// Read-only diff viewer over a flat list of <see cref="DiffLine"/>s. Renders the diff with
/// AvalonEdit so source syntax highlighting can follow the selected file extension, while custom
/// line backgrounds preserve added/removed diff coloring.
/// </summary>
public partial class DiffView : UserControl
{
    private static readonly Brush AddedBackground = Frozen(Color.FromRgb(230, 255, 237));
    private static readonly Brush RemovedBackground = Frozen(Color.FromRgb(255, 235, 233));
    private static readonly Brush DarkRedForeground = Frozen(Color.FromRgb(128, 0, 0));

    private readonly Dictionary<int, int> _newLineToDocumentLine = new();
    private readonly Dictionary<int, int> _documentLineToNewLine = new();
    private readonly DiffLineBackgroundRenderer _backgroundRenderer = new();
    private readonly BrightRedForegroundColorizer _brightRedForegroundColorizer = new(DarkRedForeground);
    private int? _firstChangeDocumentLine;
    private int? _contextMenuLine;

    public DiffView()
    {
        InitializeComponent();
        Editor.TextArea.TextView.BackgroundRenderers.Add(_backgroundRenderer);
        Editor.TextArea.TextView.LineTransformers.Add(_brightRedForegroundColorizer);
    }

    /// <summary>Raised when the reviewer chooses "Add Comment"; carries the new-file line clicked.</summary>
    public event EventHandler<int>? AddCommentRequested;

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines),
        typeof(IReadOnlyList<DiffLine>),
        typeof(DiffView),
        new PropertyMetadata(null, OnLinesChanged));

    public IReadOnlyList<DiffLine>? Lines
    {
        get => (IReadOnlyList<DiffLine>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath),
        typeof(string),
        typeof(DiffView),
        new PropertyMetadata("", OnSourcePathChanged));

    public string SourcePath
    {
        get => (string)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public static readonly DependencyProperty TargetLineProperty = DependencyProperty.Register(
        nameof(TargetLine),
        typeof(int?),
        typeof(DiffView),
        new PropertyMetadata(null, OnTargetLineChanged));

    public int? TargetLine
    {
        get => (int?)GetValue(TargetLineProperty);
        set => SetValue(TargetLineProperty, value);
    }

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DiffView)d).Rebuild();

    private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DiffView)d).ApplySyntaxHighlighting();

    private static void OnTargetLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DiffView)d).JumpToTarget();

    private void Rebuild()
    {
        _newLineToDocumentLine.Clear();
        _documentLineToNewLine.Clear();
        _backgroundRenderer.LineKinds.Clear();
        _firstChangeDocumentLine = null;

        var lines = Lines ?? [];
        var documentLines = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var documentLine = i + 1;
            var diffLine = lines[i];
            documentLines[i] = diffLine.Text;

            if (diffLine.NewLineNumber is int newLine)
            {
                _newLineToDocumentLine[newLine] = documentLine;
                _documentLineToNewLine[documentLine] = newLine;
            }

            if (diffLine.Kind is DiffLineKind.Added or DiffLineKind.Removed)
            {
                _backgroundRenderer.LineKinds[documentLine] = diffLine.Kind;
                _firstChangeDocumentLine ??= documentLine;
            }
        }

        Editor.Document = new TextDocument(string.Join(Environment.NewLine, documentLines));
        ApplySyntaxHighlighting();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

        if (TargetLine.HasValue)
            JumpToTarget();
        else
            CenterOn(_firstChangeDocumentLine);
    }

    private void ApplySyntaxHighlighting()
    {
        var extension = Path.GetExtension(SourcePath ?? "");
        Editor.SyntaxHighlighting = string.IsNullOrEmpty(extension)
            ? null
            : HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    private void Editor_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var point = Mouse.GetPosition(Editor);
        var position = Editor.GetPositionFromPoint(point);
        var documentLine = position?.Line;
        _contextMenuLine = documentLine is int line && _documentLineToNewLine.TryGetValue(line, out var newLine)
            ? newLine
            : null;
        AddCommentMenuItem.IsEnabled = _contextMenuLine.HasValue;
    }

    private void AddComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLine is int line)
            AddCommentRequested?.Invoke(this, line);
    }

    private void JumpToTarget()
    {
        if (TargetLine is not int line || !_newLineToDocumentLine.TryGetValue(line, out var documentLine))
            return;

        SelectLine(documentLine);
        CenterOn(documentLine);
    }

    private void SelectLine(int documentLine)
    {
        if (Editor.Document is null ||
            documentLine < 1 ||
            documentLine > Editor.Document.LineCount)
        {
            return;
        }

        var line = Editor.Document.GetLineByNumber(documentLine);
        Editor.Select(line.Offset, line.Length);
        Editor.TextArea.Caret.Line = documentLine;
        Editor.TextArea.Caret.Column = 1;
    }

    private void CenterOn(int? documentLine)
    {
        if (documentLine is null)
            return;

        Editor.Dispatcher.BeginInvoke(
            () => Editor.ScrollToLine(documentLine.Value),
            DispatcherPriority.Background);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed class DiffLineBackgroundRenderer : IBackgroundRenderer
    {
        public Dictionary<int, DiffLineKind> LineKinds { get; } = [];

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document is null || LineKinds.Count == 0)
                return;

            textView.EnsureVisualLines();
            foreach (var line in textView.VisualLines)
            {
                var documentLine = line.FirstDocumentLine.LineNumber;
                if (!LineKinds.TryGetValue(documentLine, out var kind))
                    continue;

                var brush = kind == DiffLineKind.Added ? AddedBackground : RemovedBackground;
                var rectangle = new Rect(
                    0,
                    line.VisualTop - textView.ScrollOffset.Y,
                    textView.ActualWidth,
                    line.Height);
                drawingContext.DrawRectangle(brush, null, rectangle);
            }
        }
    }

    private sealed class BrightRedForegroundColorizer : DocumentColorizingTransformer
    {
        private readonly Brush _replacement;

        public BrightRedForegroundColorizer(Brush replacement) => _replacement = replacement;

        protected override void ColorizeLine(DocumentLine line)
        {
            ChangeLinePart(
                line.Offset,
                line.EndOffset,
                element =>
                {
                    if (IsBrightRed(element.TextRunProperties.ForegroundBrush))
                        element.TextRunProperties.SetForegroundBrush(_replacement);
                });
        }

        private static bool IsBrightRed(Brush? brush) =>
            brush is SolidColorBrush { Color: var color } &&
            color.R >= 180 &&
            color.G <= 80 &&
            color.B <= 80;
    }
}
