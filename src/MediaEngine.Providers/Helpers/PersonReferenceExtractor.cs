using MediaEngine.Domain;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Helpers;

/// <summary>
/// Extracts person references from metadata claims and canonical values.
/// Static helper with no dependencies — used by pipeline workers and enrichment workers.
/// </summary>
public static class PersonReferenceExtractor
{
    /// <summary>
    /// Extracts person references directly from raw <see cref="ProviderClaim"/> lists.
    /// Returns only references with a confirmed Wikidata QID.
    /// Handles collective pseudonyms and constituent members.
    /// </summary>
    public static IReadOnlyList<PersonReference> FromRawClaims(
        IReadOnlyList<ProviderClaim> rawClaims,
        MediaType mediaType = MediaType.Unknown)
    {
        var byKey = AccumulateByKey(rawClaims);
        var performerRole = ResolvePerformerRole(mediaType);

        var refs = new List<PersonReference>();
        AddPersonRefsFromLists(refs, "Author",       byKey, MetadataFieldConstants.Author,   "author_qid");
        AddPersonRefsFromLists(refs, "Narrator",     byKey, MetadataFieldConstants.Narrator, "narrator_qid");
        AddPersonRefsFromLists(refs, performerRole,  byKey, "performer",                     "performer_qid");
        AddPersonRefsFromLists(refs, performerRole,  byKey, MetadataFieldConstants.Artist,   "artist_qid");
        AddPersonRefsFromLists(refs, "Director",     byKey, "director",                      "director_qid");
        AddPersonRefsFromLists(refs, "Screenwriter", byKey, "screenwriter",                  "screenwriter_qid");
        AddPersonRefsFromLists(refs, "Composer",     byKey, "composer",                      "composer_qid");
        AddPersonRefsFromLists(refs, "Actor",        byKey, "cast_member",                   "cast_member_qid");

        // Mark author refs as collective pseudonyms when the adapter flagged it.
        if (byKey.TryGetValue("author_is_collective_pseudonym", out var pseudoFlags)
            && pseudoFlags.Any(f => string.Equals(f, "true", StringComparison.OrdinalIgnoreCase)))
        {
            for (int i = 0; i < refs.Count; i++)
            {
                if (string.Equals(refs[i].Role, "Author", StringComparison.OrdinalIgnoreCase))
                    refs[i] = refs[i] with { IsCollectivePseudonym = true };
            }
        }

        // Collective pseudonym constituent members.
        if (byKey.TryGetValue("collective_members_qid", out var collectiveValues))
        {
            foreach (var segment in collectiveValues)
            {
                var colonIndex = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var qid   = segment[..colonIndex].Trim();
                    var label = segment[(colonIndex + 2)..].Trim();
                    if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(qid))
                    {
                        var alreadyPresent = refs.Any(r =>
                            string.Equals(r.WikidataQid, qid, StringComparison.OrdinalIgnoreCase)
                            || (r.IsCollectivePseudonym && string.Equals(r.Name, label, StringComparison.OrdinalIgnoreCase)));
                        if (!alreadyPresent)
                            refs.Add(new PersonReference("Author", label, qid));
                    }
                }
            }
        }

        // QID-first: only emit references with a confirmed Wikidata QID.
        return refs
            .Where(r => !string.IsNullOrEmpty(r.WikidataQid))
            .GroupBy(r => r.WikidataQid!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Extracts person references that have a name but NO Wikidata QID.
    /// These are candidates for standalone person reconciliation.
    /// </summary>
    public static IReadOnlyList<PersonReference> FromRawClaimsUnlinked(
        IReadOnlyList<ProviderClaim> rawClaims,
        MediaType mediaType = MediaType.Unknown)
    {
        var byKey = AccumulateByKey(rawClaims);
        var performerRole = ResolvePerformerRole(mediaType);

        var refs = new List<PersonReference>();
        AddPersonRefsFromLists(refs, "Author",       byKey, MetadataFieldConstants.Author,   "author_qid");
        AddPersonRefsFromLists(refs, "Narrator",     byKey, MetadataFieldConstants.Narrator, "narrator_qid");
        AddPersonRefsFromLists(refs, performerRole,  byKey, "performer",                     "performer_qid");
        AddPersonRefsFromLists(refs, performerRole,  byKey, MetadataFieldConstants.Artist,   "artist_qid");
        AddPersonRefsFromLists(refs, "Director",     byKey, "director",                      "director_qid");
        AddPersonRefsFromLists(refs, "Screenwriter", byKey, "screenwriter",                  "screenwriter_qid");
        AddPersonRefsFromLists(refs, "Composer",     byKey, "composer",                      "composer_qid");
        AddPersonRefsFromLists(refs, "Actor",        byKey, "cast_member",                   "cast_member_qid");

        return refs
            .Where(r => string.IsNullOrEmpty(r.WikidataQid) && !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => $"{r.Role}::{r.Name}".ToUpperInvariant())
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Extracts person references from canonical values (post-scoring).
    /// When a <c>*_qid</c> companion canonical is present, each QID::Label pair
    /// is parsed and the Wikidata QID is forwarded to the PersonReference.
    /// </summary>
    public static IReadOnlyList<PersonReference> FromCanonicals(
        IReadOnlyList<CanonicalValue> canonicals,
        MediaType mediaType = MediaType.Unknown)
    {
        var performerRole = ResolvePerformerRole(mediaType);

        var refs = new List<PersonReference>();
        AddPersonRefsFromCanonicals(refs, "Author",       canonicals, MetadataFieldConstants.Author,   "author_qid");
        AddPersonRefsFromCanonicals(refs, "Narrator",     canonicals, MetadataFieldConstants.Narrator, "narrator_qid");
        AddPersonRefsFromCanonicals(refs, performerRole,  canonicals, "performer",                     "performer_qid");
        AddPersonRefsFromCanonicals(refs, performerRole,  canonicals, MetadataFieldConstants.Artist,   "artist_qid");
        AddPersonRefsFromCanonicals(refs, "Director",     canonicals, "director",                      "director_qid");
        AddPersonRefsFromCanonicals(refs, "Screenwriter", canonicals, "screenwriter",                  "screenwriter_qid");
        AddPersonRefsFromCanonicals(refs, "Composer",     canonicals, "composer",                      "composer_qid");
        AddPersonRefsFromCanonicals(refs, "Actor",        canonicals, "cast_member",                   "cast_member_qid");

        // Collective pseudonym constituent members from canonical values.
        var collectiveValue = canonicals.FirstOrDefault(c => c.Key == "collective_members_qid")?.Value;
        if (!string.IsNullOrEmpty(collectiveValue))
        {
            foreach (var segment in collectiveValue.Split("|||",
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colonIndex = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var qid   = segment[..colonIndex].Trim();
                    var label = segment[(colonIndex + 2)..].Trim();
                    if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(qid))
                    {
                        if (!refs.Any(r => string.Equals(r.WikidataQid, qid, StringComparison.OrdinalIgnoreCase)))
                            refs.Add(new PersonReference("Author", label, qid));
                    }
                }
            }
        }

        return refs;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string ResolvePerformerRole(MediaType mediaType) => mediaType switch
    {
        MediaType.Music      => "Performer",
        MediaType.Audiobooks => "Narrator",
        _                    => "Actor",
    };

    private static Dictionary<string, List<string>> AccumulateByKey(IReadOnlyList<ProviderClaim> claims)
    {
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in claims)
        {
            if (!byKey.TryGetValue(c.Key, out var list))
            {
                list = [];
                byKey[c.Key] = list;
            }
            list.Add(c.Value);
        }
        return byKey;
    }

    private static void AddPersonRefsFromLists(
        List<PersonReference> refs,
        string role,
        Dictionary<string, List<string>> byKey,
        string nameKey,
        string qidKey)
    {
        if (!byKey.TryGetValue(nameKey, out var names))
            return;

        byKey.TryGetValue(qidKey, out var qids);

        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string? qid = null;
            if (qids is not null && i < qids.Count)
            {
                var segment = qids[i];
                var colonIdx = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIdx > 0)
                    qid = segment[..colonIdx].Trim();
                else if (!string.IsNullOrWhiteSpace(segment))
                    qid = segment.Trim();
            }

            refs.Add(new PersonReference(role, name, string.IsNullOrEmpty(qid) ? null : qid));
        }
    }

    private static void AddPersonRefsFromCanonicals(
        List<PersonReference> refs,
        string role,
        IReadOnlyList<CanonicalValue> canonicals,
        string nameKey,
        string qidKey)
    {
        var nameCanonical = canonicals.FirstOrDefault(c =>
            string.Equals(c.Key, nameKey, StringComparison.OrdinalIgnoreCase));
        if (nameCanonical is null || string.IsNullOrWhiteSpace(nameCanonical.Value))
            return;

        var qidCanonical = canonicals.FirstOrDefault(c =>
            string.Equals(c.Key, qidKey, StringComparison.OrdinalIgnoreCase));

        // Split multi-valued canonicals (joined with |||).
        var names = nameCanonical.Value.Split("|||",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var qidParts = qidCanonical?.Value?.Split("|||",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        for (int i = 0; i < names.Length; i++)
        {
            string? qid = null;
            if (i < qidParts.Length)
            {
                var segment = qidParts[i];
                var colonIdx = segment.IndexOf("::", StringComparison.Ordinal);
                if (colonIdx > 0)
                    qid = segment[..colonIdx].Trim();
                else if (!string.IsNullOrWhiteSpace(segment))
                    qid = segment.Trim();
            }

            refs.Add(new PersonReference(role, names[i], string.IsNullOrEmpty(qid) ? null : qid));
        }
    }
}
