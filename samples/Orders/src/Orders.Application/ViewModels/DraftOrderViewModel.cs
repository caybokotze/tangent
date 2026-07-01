namespace Orders.Application.ViewModels;

/// <summary>VIOLATION: a mutable view model.</summary>
public sealed record DraftOrderViewModel
{
    // VIOLATION (TNG0107): ViewModel declares `properties { forbid public set }`.
    public Guid Id { get; set; }

    public string Notes { get; init; } = string.Empty;
}
