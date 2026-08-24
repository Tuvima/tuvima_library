using System.Globalization;
using System.Text;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.View;

public sealed class ViewScopeStore(
    IProfileRepository profiles,
    IViewProfileRepository policies,
    IViewPersonalSpaceRepository spaces) : IViewScopeStore
{
    public async Task<ViewScopeStoreEntry?> FindProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        if (await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false) is null) return null;
        return new ViewScopeStoreEntry(
            await policies.GetPolicyAsync(profileId, ct).ConfigureAwait(false),
            await spaces.GetByOwnerAsync(profileId, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ViewScopeStoreEntry>> GetProfilesAsync(CancellationToken ct = default)
    {
        var result = new List<ViewScopeStoreEntry>();
        foreach (var profile in await profiles.GetAllAsync(ct).ConfigureAwait(false))
        {
            result.Add(new ViewScopeStoreEntry(
                await policies.GetPolicyAsync(profile.Id, ct).ConfigureAwait(false),
                await spaces.GetByOwnerAsync(profile.Id, ct).ConfigureAwait(false)));
        }
        return result;
    }
}

public sealed class ViewResourceStore(
    ILocalAssetRepository assets,
    IViewGalleryRepository galleries,
    IViewPersonalSpaceRepository spaces) : IViewResourceStore
{
    public async Task<ViewResourceDescriptor?> FindAsync(
        ViewResourceKind kind,
        Guid resourceId,
        Guid requestingProfileId,
        CancellationToken ct = default)
    {
        if (kind == ViewResourceKind.Gallery)
        {
            var gallery = await galleries.GetAsync(resourceId, ct).ConfigureAwait(false);
            if (gallery is null) return null;
            var space = await spaces.GetByOwnerAsync(gallery.OwnerProfileId, ct).ConfigureAwait(false);
            var shares = await galleries.GetSharesAsync(resourceId, ct).ConfigureAwait(false);
            return new ViewResourceDescriptor(
                kind,
                resourceId,
                gallery.OwnerProfileId,
                space?.LibraryId,
                shares.Select(share => share.ProfileId).ToHashSet());
        }

        var item = assets.Find(resourceId, ct);
        if (item is null) return null;
        var explicitProfiles = await GetExplicitAssetRecipientsAsync(item.Id, requestingProfileId, ct)
            .ConfigureAwait(false);
        return new ViewResourceDescriptor(
            kind,
            item.Id,
            item.OwnerProfileId,
            item.LibraryId,
            explicitProfiles);
    }

    private async Task<IReadOnlySet<Guid>> GetExplicitAssetRecipientsAsync(
        Guid itemId,
        Guid requestingProfileId,
        CancellationToken ct)
    {
        var shared = await galleries.GetSharedWithAsync(requestingProfileId, ct).ConfigureAwait(false);
        foreach (var gallery in shared.Where(candidate => candidate.Kind == ViewGalleryKind.Manual))
        {
            int? position = null;
            Guid? cursorId = null;
            do
            {
                var page = await galleries.GetItemsAsync(gallery.Id, position, cursorId, 200, ct)
                    .ConfigureAwait(false);
                if (page.Items.Any(item => item.ItemId == itemId))
                    return new HashSet<Guid> { requestingProfileId };
                position = page.NextPosition;
                cursorId = page.NextItemId;
                if (!page.HasMore) break;
            } while (true);
        }
        return new HashSet<Guid>();
    }
}

public sealed class ViewAssetQueryBackend(ILocalAssetRepository assets) : IViewAssetQueryBackend
{
    public Task<ViewAssetTimelinePageDto> QueryAsync(
        ViewAssetQueryPlan plan,
        CancellationToken ct = default)
    {
        var cursor = ViewTimelineCursorCodec.Decode(plan.Cursor);
        var page = assets.QueryTimeline(new LocalAssetTimelineQuery(
            plan.Scope.LibraryIds,
            plan.Limit,
            cursor?.EffectiveAt,
            cursor?.ItemId,
            plan.Search,
            plan.MediaKinds,
            plan.FavoritesOnly,
            plan.IncludeHidden,
            plan.GalleryId,
            plan.Lifecycle), ct);
        return Task.FromResult(new ViewAssetTimelinePageDto(
            page.Items,
            ViewTimelineCursorCodec.Encode(page.NextCursor),
            page.HasMore));
    }
}

public static class ViewTimelineCursorCodec
{
    public static string? Encode(LocalAssetTimelineCursor? cursor)
    {
        if (cursor is null) return null;
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{cursor.EffectiveAt.UtcTicks}:{cursor.ItemId:N}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static LocalAssetTimelineCursor? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var separator = decoded.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0
                || !long.TryParse(decoded[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(decoded[(separator + 1)..], "N", out var itemId))
                throw new FormatException();
            return new LocalAssetTimelineCursor(new DateTimeOffset(ticks, TimeSpan.Zero), itemId);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("The View timeline cursor is invalid.", nameof(value));
        }
    }
}
