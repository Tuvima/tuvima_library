using MediaEngine.Domain;
using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Adapters;

public sealed partial class ReconciliationAdapter
{
    private async Task AppendAwardFamilyClaimsAsync(List<ProviderClaim> claims, CancellationToken ct)
    {
        var exactFields = new[]
        {
            (Exact: MetadataFieldConstants.AwardReceived, Family: MetadataFieldConstants.AwardFamily),
            (Exact: MetadataFieldConstants.AwardNominated, Family: MetadataFieldConstants.NominationFamily),
        };

        foreach (var (exact, family) in exactFields)
        {
            var qids = claims
                .Where(claim => string.Equals(claim.Key, exact + MetadataFieldConstants.CompanionQidSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(claim => claim.Value.Split("::", 2, StringSplitOptions.TrimEntries)[0])
                .Where(qid => qid.Length > 1 && qid[0] == 'Q' && qid[1..].All(char.IsDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (qids.Length == 0)
                continue;

            var hierarchy = await ExtendAsync(qids, ["P361"], ct).ConfigureAwait(false);
            foreach (var qid in qids)
            {
                if (!hierarchy.TryGetValue(qid, out var properties)
                    || !properties.TryGetValue("P361", out var parents))
                    continue;

                foreach (var parent in parents.Where(parent => parent.Value?.EntityId is not null))
                {
                    var familyQid = parent.Value!.EntityId!;
                    var label = parent.Value.EntityLabel ?? parent.Value.RawValue;
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    if (!claims.Any(claim => string.Equals(claim.Key, family, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(claim.Value, label, StringComparison.OrdinalIgnoreCase)))
                        claims.Add(new ProviderClaim(family, label, ClaimConfidence.WikidataProperty));

                    var packedQid = $"{familyQid}::{label}";
                    if (!claims.Any(claim => string.Equals(claim.Key, family + MetadataFieldConstants.CompanionQidSuffix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(claim.Value, packedQid, StringComparison.OrdinalIgnoreCase)))
                        claims.Add(new ProviderClaim(family + MetadataFieldConstants.CompanionQidSuffix, packedQid, ClaimConfidence.EntityQidReference));
                }
            }
        }
    }
}
