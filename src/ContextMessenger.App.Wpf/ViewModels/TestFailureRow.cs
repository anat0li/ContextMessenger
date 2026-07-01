namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// One failed test projected for the Tests tab. <see cref="Path"/> keeps the raw runner path used
/// to associate the failure with a changed file (jump only works for in-patch test sources);
/// <see cref="HasLocation"/> indicates whether a location is even available.
/// </summary>
public sealed record TestFailureRow
{
    /// <summary>Test name/identifier, or a short fallback when the runner reports none.</summary>
    public required string Name { get; init; }

    public required string Path { get; init; }

    public int? Line { get; init; }

    /// <summary>Display location ("path (line)"), empty when the runner reported no source.</summary>
    public required string Location { get; init; }

    public required string Message { get; init; }

    /// <summary>True when the test's source file is part of the patch, so the jump is available.</summary>
    public bool CanJump { get; init; }

    public bool HasLocation => Location.Length > 0;
}
