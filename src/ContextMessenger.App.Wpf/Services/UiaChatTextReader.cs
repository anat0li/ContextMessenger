using System.Windows.Automation;

namespace ContextMessenger.App.Wpf.Services;

internal static class UiaChatTextReader
{
    public static IReadOnlyList<ChatTextReadResult> ReadCandidates(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var results = new List<ChatTextReadResult>();

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObj))
        {
            var text = ((TextPattern)textPatternObj).DocumentRange.GetText(-1);
            AddIfUseful(results, "TextPattern", text);
        }

        return results;
    }

    private static void AddIfUseful(List<ChatTextReadResult> results, string source, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (results.Any(r => string.Equals(r.Text, text, StringComparison.Ordinal)))
            return;

        results.Add(new ChatTextReadResult(source, text));
    }
}
