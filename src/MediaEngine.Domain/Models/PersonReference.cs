namespace MediaEngine.Domain.Models;

/// <summary>
/// A lightweight reference to a person extracted from ingested file metadata,
/// passed to <see cref="Contracts.IRecursiveIdentityService.EnrichAsync"/> so
/// the service can create or locate the corresponding <see cref="Entities.Person"/>
/// record and enqueue a Wikidata enrichment request if needed.
///
/// Spec: Phase 9 – Recursive Person Enrichment § Input Shape.
/// </summary>
/// <param name="Role">
/// The role this person plays in the associated media asset.
/// Valid values: <c>"Author"</c>, <c>"Narrator"</c>, <c>"Director"</c>.
/// </param>
/// <param name="Name">
/// The person's display name as extracted from the file's metadata.
/// Example: <c>"Ursula K. Le Guin"</c>.
/// </param>
public sealed record PersonReference(string Role, string Name);
