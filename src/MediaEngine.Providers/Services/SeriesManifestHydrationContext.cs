using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Services;

public sealed record SeriesManifestHydrationContext(
    Guid AssetId,
    Guid? WorkId,
    string ResolvedWorkQid,
    MediaType MediaType,
    string? Title,
    string? SeriesHint,
    Guid? IngestionRunId,
    WorkLineage? Lineage,
    IReadOnlyList<ProviderClaim> FullClaims);
