namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// One build error projected for the Errors tab. <see cref="Path"/> keeps the raw diagnostic path
/// used to associate the error with a changed file; <see cref="Location"/> is the display form.
/// </summary>
public sealed record BuildErrorRow
{
    public required string Code { get; init; }

    public required string Path { get; init; }

    /// <summary>New-file line number reported by the compiler; used to jump the diff caret.</summary>
    public int? Line { get; init; }

    public required string Location { get; init; }

    public required string Message { get; init; }
}
