using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

/// <summary>
/// A pending Pass 2 (Universe Lookup) enrichment request stored in the
/// <c>deferred_enrichment_queue</c> database table.
///
/// Created by the hydration pipeline after Pass 1 completes. Processed by
/// <c>DeferredEnrichmentService</c> when the system is idle, on a nightly
/// schedule, or when manually triggered.
///
/// Spec: §3.24 — Two-Pass Enrichment Architecture.
/// </summary>
public sealed class DeferredEnrichmentRequest
{
    /// <summary>Unique identifier for this deferred request.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The domain entity to enrich. Points to <c>media_assets.id</c>.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// The Wikidata QID resolved during Pass 1 (nullable).
    /// When present, Pass 2 skips QID resolution and goes straight to
    /// SPARQL deep hydration.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>The media type of the asset being enriched.</summary>
    public MediaType MediaType { get; set; }

    /// <summary>
    /// Serialised JSON dictionary of contextual hints from Pass 1.
    /// Preserved so Pass 2 can rebuild a <see cref="HarvestRequest"/>
    /// with the same lookup context.
    /// </summary>
    public string? HintsJson { get; set; }

    /// <summary>When this request was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Lifecycle status: <c>"Pending"</c> or <c>"Processed"</c>.
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>When this request was processed (nullable).</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
}
