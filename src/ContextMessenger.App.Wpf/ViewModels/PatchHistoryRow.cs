namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// One row in the History list. Only the last row of an interaction carries the held response,
/// which the view reveals when that row is expanded.
/// </summary>
public sealed record PatchHistoryRow
{
    public required string Direction { get; init; }

    public required string Summary { get; init; }

    public int Revision { get; init; }

    public string HeldResponse { get; init; } = "";

    public bool HasHeldResponse => HeldResponse.Length > 0;

    public string Header => $"{Direction} · {Summary} (rev {Revision})";
}
