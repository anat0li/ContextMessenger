namespace ContextMessenger.App.Wpf.Services;

public interface IProtocolLogFormatter
{
    string FormatRequestBodies(IReadOnlyList<string> requestBodies);

    string FormatResponse(string responseBlock);
}
