namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Status of the Pass 2 (Universe Lookup) deferred enrichment queue.</summary>
public sealed record Pass2StatusDto(int PendingCount, bool TwoPassEnabled);

/// <summary>Result of triggering Pass 2 processing.</summary>
public sealed record Pass2TriggerResultDto(int PendingCount, string Message);
