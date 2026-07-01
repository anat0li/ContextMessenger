namespace ContextMessenger.App.Wpf.Services;

public interface ITargetAutomationAdapter
{
    Task<bool> SubmitResponseAsync(string text, CancellationToken cancellationToken);
}
