namespace ContextMessenger.Core.FileSystem;

public sealed class PathOutsideSandboxException : InvalidOperationException
{
    public PathOutsideSandboxException(string path)
        : base($"Path '{path}' is outside the configured sandbox root.")
    {
        Path = path;
    }

    public string Path { get; }
}
