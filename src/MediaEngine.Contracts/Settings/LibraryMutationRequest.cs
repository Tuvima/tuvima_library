namespace MediaEngine.Contracts.Settings;

/// <summary>A compare-and-swap edit scoped to one library or the View root.</summary>
public sealed class LibraryMutationRequest
{
    public LibraryFolderDto? Library { get; init; }
    public LibraryFolderDto? ExpectedLibrary { get; init; }
    public ViewStorageDto? ViewStorage { get; init; }
    public ViewStorageDto? ExpectedViewStorage { get; init; }
}

public sealed record ViewStorageSummaryDto(int Sources, int Items);
