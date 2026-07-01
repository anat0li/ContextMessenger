namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// One build warning projected for the Warnings tab. <see cref="Path"/> keeps the raw diagnostic
/// path used to associate the warning with a changed file; <see cref="Location"/> is the display form.
/// </summary>
public sealed record BuildWarningRow
{
    public required string Code { get; init; }

    public required string Path { get; init; }

    /// <summary>New-file line number reported by the compiler; used to jump the diff caret.</summary>
    public int? Line { get; init; }

    public required string Location { get; init; }

    public required string Message { get; init; }
}
