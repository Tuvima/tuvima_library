using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Components.MediaEditor;

public partial class SharedMediaEditorShell
{
    private static readonly (string Id, string Label)[] TabDefinitions =
    [
        ("details", "Details"),
        ("universe", "Universe"),
        ("artwork", "Artwork"),
        ("options", "Options"),
        ("sorting", "Sorting"),
        ("file", "File"),
    ];

    private static readonly string[] ReclassifyMediaTypes =
    [
        "Books",
        "Audiobooks",
        "Movies",
        "TV",
        "Music",
        "Comics",
    ];

    private static readonly ArtworkSlotDefinition PosterCoverArtworkSlot =
        new("CoverArt", "Poster / Cover", "Primary art used on cards and detail pages.", Icons.Material.Outlined.Photo, "portrait", "cover", true, "Best for posters and front-cover artwork.", "Primary");

    private static readonly ArtworkSlotDefinition BookCoverArtworkSlot =
        new("CoverArt", "Cover", "Primary front-cover art used on cards and detail pages.", Icons.Material.Outlined.MenuBook, "portrait", "cover", true, "Best for book, comic, and audiobook covers.", "Primary");

    private static readonly ArtworkSlotDefinition AlbumArtArtworkSlot =
        new("CoverArt", "Album Art", "Primary album art used across music views.", Icons.Material.Outlined.Album, "square", "cover", true, "Best for album covers and primary music art.", "Primary");

    private static readonly ArtworkSlotDefinition SquareArtArtworkSlot =
        new("SquareArt", "Square Art", "A dedicated square crop for tiles, shelves, and compact layouts.", Icons.Material.Outlined.CropSquare, "square", "cover", true, "Best for square variants that should not be auto-cropped from the primary cover.", "Square");

    private static readonly ArtworkSlotDefinition BackgroundArtworkSlot =
        new("Background", "Background", "A cinematic wide image for backgrounds and immersive layouts.", Icons.Material.Outlined.Panorama, "background", "cover", true, "Best for scenic or full-bleed background art.", "Wide");

    private static readonly ArtworkSlotDefinition BannerArtworkSlot =
        new("Banner", "Banner", "A wide promotional strip for shelves and collection headers.", Icons.Material.Outlined.PanoramaWideAngle, "banner", "cover", true, "Best for landscape banners and shelf headers.", "Strip");

    private static readonly ArtworkSlotDefinition LogoArtworkSlot =
        new("Logo", "Logo", "Title treatment or transparent branding art.", Icons.Material.Outlined.BrandingWatermark, "logo", "contain", true, "Best for transparent logos or wordmarks.", "Logo");

    [Inject] protected IEngineApiClient ApiClient { get; set; } = null!;
    [Inject] protected UIOrchestratorService Orchestrator { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter] public MediaEditorLaunchRequest Request { get; set; } = new();

    private RegistryItemDetailViewModel? _detail;
    private List<CanonicalFieldViewModel> _canonicalValues = [];
    private List<ClaimHistoryDto> _claims = [];
    private List<RegistryItemHistoryDto> _history = [];
    private ArtworkEditorDto? _artwork;
    private MediaEditorSchema _schema = MediaEditorSchemaCatalog.Resolve(null);
    private readonly Dictionary<string, string> _editedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _selectedSuggestedFieldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBrowserFile> _pendingArtworkFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingArtworkPreviewUrls = new(StringComparer.OrdinalIgnoreCase);
    private ItemCanonicalSearchResponseDto? _canonicalSearchResponse;
    private string _activeTab = "details";
    private string _canonicalTargetGroup = "";
    private string _canonicalSearchQuery = "";
    private string? _selectedCandidateId;
    private string? _selectedArtworkAssetType;
    private string? _focusedArtworkVariantKey;
    private string _selectedMediaType = "Books";
    private bool _loading = true;
    private bool _saving;
    private bool _reclassifying;
    private bool _searchingCanonical;
    private bool _confirmDiscard;
    private string? _dragTargetArtworkType;
    private string _reviewSummary = "Review the item identity.";

    protected IReadOnlyList<(string Id, string Label)> Tabs => TabDefinitions;
    protected IReadOnlyList<ArtworkSlotDefinition> ArtworkSlots => ResolveArtworkSlots(_detail?.MediaType ?? Request.MediaType ?? _selectedMediaType);
    protected bool IsSingleItem => Request.EntityIds.Count == 1;
    protected bool IsBatchMode => Request.Mode == SharedMediaEditorMode.Batch || Request.EntityIds.Count > 1;
    protected Guid CurrentEntityId => Request.EntityIds[0];
    protected bool IsDirty => _editedValues.Count > 0 || _pendingArtworkFiles.Count > 0;
    protected bool HasGeneratedHeroArtwork => !string.IsNullOrWhiteSpace(GetArtworkPreviewUrl("Hero"));
    protected string ArtworkTabExplanation => GetArtworkTabExplanation();
    protected ArtworkSlotDefinition? SelectedArtworkSlot =>
        ArtworkSlots.FirstOrDefault(slot => string.Equals(slot.AssetType, _selectedArtworkAssetType, StringComparison.OrdinalIgnoreCase))
        ?? ArtworkSlots.FirstOrDefault();

    protected string HeaderKicker =>
        Request.Mode switch
        {
            SharedMediaEditorMode.Review => "Review",
            SharedMediaEditorMode.Batch => $"{Request.EntityIds.Count} items",
            _ => _schema.MediaType,
        };

    protected string HeaderTitle =>
        Request.HeaderTitle
        ?? _detail?.Title
        ?? (IsBatchMode ? $"Edit {Request.EntityIds.Count} Items" : "Edit Item");

    protected string? HeaderSubtitle =>
        Request.HeaderSubtitle
        ?? (IsSingleItem ? BuildHeaderSubtitle() : string.Join(" | ", Request.PreviewItems.Take(3).Select(x => x.Title)));

    protected string? CurrentCoverUrl => GetArtworkPreviewUrl("CoverArt") ?? Request.CoverUrl;

    protected override async Task OnInitializedAsync()
    {
        _activeTab = string.IsNullOrWhiteSpace(Request.InitialTab) ? "details" : Request.InitialTab;
        _schema = MediaEditorSchemaCatalog.Resolve(Request.MediaType);

        if (IsSingleItem)
            await LoadSingleItemAsync();
        else
            await LoadBatchAsync();
    }

    private async Task LoadSingleItemAsync()
    {
        _loading = true;
        StateHasChanged();

        try
        {
            var detailTask = ApiClient.GetRegistryItemDetailAsync(CurrentEntityId);
            var canonicalTask = Orchestrator.GetCanonicalValuesAsync(CurrentEntityId);
            var claimsTask = Orchestrator.GetClaimHistoryAsync(CurrentEntityId);
            var historyTask = ApiClient.GetItemHistoryAsync(CurrentEntityId);
            var artworkTask = ApiClient.GetArtworkAsync(CurrentEntityId);

            await Task.WhenAll(detailTask, canonicalTask, claimsTask, historyTask, artworkTask);

            _detail = detailTask.Result;
            _canonicalValues = canonicalTask.Result;
            _claims = claimsTask.Result;
            _history = historyTask.Result;
            _artwork = artworkTask.Result ?? new ArtworkEditorDto { EntityId = CurrentEntityId };
            _schema = MediaEditorSchemaCatalog.Resolve(_detail?.MediaType ?? Request.MediaType);
            _selectedMediaType = _detail?.MediaType ?? Request.MediaType ?? "Books";
            _pendingArtworkFiles.Clear();
            _pendingArtworkPreviewUrls.Clear();
            _dragTargetArtworkType = null;
            NormalizeArtworkSelection();

            if (Request.Mode == SharedMediaEditorMode.Review)
            {
                var target = ReviewTargetResolver.Resolve(_detail?.MediaType ?? Request.MediaType, Request.ReviewTrigger ?? _detail?.ReviewTrigger);
                _activeTab = string.IsNullOrWhiteSpace(Request.InitialTab) ? target.InitialTab : Request.InitialTab!;
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup) ? target.CanonicalTargetGroup : Request.InitialCanonicalTargetGroup!;
                _reviewSummary = target.Summary;
            }
            else
            {
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup) ? _schema.DefaultTargetGroup : Request.InitialCanonicalTargetGroup!;
            }

            _canonicalSearchQuery = BuildSuggestedSearchQuery();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private Task LoadBatchAsync()
    {
        var mediaTypes = Request.PreviewItems.Select(x => x.MediaType ?? Request.MediaType ?? "Books");
        _schema = MediaEditorSchemaCatalog.Resolve(mediaTypes.FirstOrDefault());
        _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup) ? _schema.DefaultTargetGroup : Request.InitialCanonicalTargetGroup!;
        _loading = false;
        return Task.CompletedTask;
    }

    protected bool IsTabDisabled(string tabId) => IsBatchMode && tabId is "universe" or "artwork" or "file";

    protected bool CanReclassifyMediaType =>
        IsSingleItem && (Request.Mode == SharedMediaEditorMode.Review || !string.IsNullOrWhiteSpace(_detail?.MediaType));

    protected IEnumerable<MediaEditorFieldGroup> GetGroupsForTab(string tabId)
    {
        if (!IsBatchMode)
            return _schema.Groups.Where(group => group.TabId == tabId);

        var batchFields = MediaEditorSchemaCatalog.ResolveBatchFields(Request.PreviewItems.Select(x => x.MediaType ?? Request.MediaType ?? "Books"));

        return tabId switch
        {
            "details" =>
            [
                new MediaEditorFieldGroup
                {
                    Id = "batch_details",
                    Label = "Shared Fields",
                    TabId = "details",
                    Fields = batchFields
                        .Where(field => !field.Key.StartsWith("sort_", StringComparison.OrdinalIgnoreCase)
                                        && field.Key is not ("description" or "comment" or "rating"))
                        .ToList(),
                },
            ],
            "options" =>
            [
                new MediaEditorFieldGroup
                {
                    Id = "batch_options",
                    Label = "Batch Options",
                    TabId = "options",
                    Fields = batchFields.Where(field => field.Key is "description" or "comment" or "rating").ToList(),
                },
            ],
            "sorting" =>
            [
                new MediaEditorFieldGroup
                {
                    Id = "batch_sorting",
                    Label = "Batch Sorting",
                    TabId = "sorting",
                    Fields = batchFields.Where(field => field.Key.StartsWith("sort_", StringComparison.OrdinalIgnoreCase)).ToList(),
                },
            ],
            _ => [],
        };
    }

    protected string GetPlaceholder(MediaEditorFieldDefinition field) =>
        IsBatchMode ? "Leave unchanged" : (field.Placeholder ?? field.Label);

    protected string GetEditableValue(string key)
    {
        if (_editedValues.TryGetValue(key, out var edited))
            return edited;

        if (IsBatchMode)
            return string.Empty;

        var values = MediaEditorSchemaCatalog.BuildValueMap(_detail, _canonicalValues);
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    protected void OnFieldInput(string key, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var baseline = IsBatchMode ? string.Empty : GetBaselineValue(key);

        if (string.Equals(normalized, baseline, StringComparison.Ordinal))
        {
            _editedValues.Remove(key);
        }
        else if (string.IsNullOrWhiteSpace(normalized))
        {
            _editedValues.Remove(key);
        }
        else
        {
            _editedValues[key] = normalized;
        }
    }

    protected async Task SaveAsync()
    {
        if (!IsDirty)
        {
            MudDialog.Cancel();
            return;
        }

        _saving = true;
        StateHasChanged();

        try
        {
            if (IsBatchMode)
            {
                if (_editedValues.Count == 0)
                {
                    MudDialog.Cancel();
                    return;
                }

                var result = await ApiClient.BatchEditAsync(Request.EntityIds, new Dictionary<string, string>(_editedValues, StringComparer.OrdinalIgnoreCase));
                if (result is null)
                {
                    Snackbar.Add("Batch edit failed.", Severity.Error);
                    return;
                }

                Snackbar.Add($"Updated {result.UpdatedCount} item(s).", Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
                return;
            }

            var savedAnything = false;

            if (_editedValues.Count > 0)
            {
                var saved = await ApiClient.SaveItemPreferencesAsync(CurrentEntityId, new Dictionary<string, string>(_editedValues, StringComparer.OrdinalIgnoreCase));
                if (!saved)
                {
                    Snackbar.Add("Preference save failed.", Severity.Error);
                    return;
                }

                savedAnything = true;
            }

            foreach (var pendingArtwork in _pendingArtworkFiles.ToList())
            {
                await using var stream = pendingArtwork.Value.OpenReadStream(10 * 1024 * 1024);
                var uploaded = await ApiClient.UploadArtworkVariantAsync(
                    CurrentEntityId,
                    pendingArtwork.Key,
                    stream,
                    pendingArtwork.Value.Name);

                if (!uploaded)
                {
                    var label = ArtworkSlots.FirstOrDefault(slot =>
                        string.Equals(slot.AssetType, pendingArtwork.Key, StringComparison.OrdinalIgnoreCase))?.Label
                        ?? pendingArtwork.Key;
                    Snackbar.Add($"{label} upload failed.", Severity.Error);
                    return;
                }

                savedAnything = true;
            }

            if (!savedAnything)
            {
                MudDialog.Cancel();
                return;
            }

            Snackbar.Add("Changes saved.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        finally
        {
            _saving = false;
        }
    }

    protected void HandleClose()
    {
        if (IsDirty)
        {
            _confirmDiscard = true;
            return;
        }

        MudDialog.Cancel();
    }

    protected void DiscardAndClose() => MudDialog.Cancel();

    protected async Task ReclassifyMediaTypeAsync()
    {
        if (!CanReclassifyMediaType || string.IsNullOrWhiteSpace(_selectedMediaType))
            return;

        _reclassifying = true;
        StateHasChanged();

        try
        {
            var ok = await Orchestrator.ReclassifyMediaTypeAsync(CurrentEntityId, _selectedMediaType);
            if (!ok)
            {
                Snackbar.Add("Media type change failed.", Severity.Error);
                return;
            }

            _canonicalSearchResponse = null;
            _editedValues.Clear();
            _selectedSuggestedFieldKeys.Clear();
            _selectedCandidateId = null;
            await LoadSingleItemAsync();
            Snackbar.Add($"Media type updated to {_selectedMediaType}.", Severity.Success);
        }
        finally
        {
            _reclassifying = false;
            StateHasChanged();
        }
    }

    protected async Task HandleArtworkSelectedAsync(string assetType, InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            ClearArtworkSelection(assetType);
            return;
        }

        _pendingArtworkFiles[assetType] = file;
        await using var stream = file.OpenReadStream(10 * 1024 * 1024);
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/png" : file.ContentType;
        _pendingArtworkPreviewUrls[assetType] = $"data:{contentType};base64,{Convert.ToBase64String(ms.ToArray())}";
        _dragTargetArtworkType = null;
        _selectedArtworkAssetType = assetType;
        NormalizeArtworkSelection();
    }

    protected void ClearArtworkSelection(string assetType)
    {
        _pendingArtworkFiles.Remove(assetType);
        _pendingArtworkPreviewUrls.Remove(assetType);
        if (string.Equals(_dragTargetArtworkType, assetType, StringComparison.OrdinalIgnoreCase))
            _dragTargetArtworkType = null;
        NormalizeArtworkSelection();
    }

    protected bool HasPendingArtwork(string assetType) =>
        _pendingArtworkFiles.ContainsKey(assetType);

    protected string? GetPendingArtworkFileName(string assetType) =>
        _pendingArtworkFiles.TryGetValue(assetType, out var file) ? file.Name : null;

    protected string? GetArtworkPreviewUrl(string assetType)
    {
        if (string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase))
            return _detail?.HeroUrl;

        if (_pendingArtworkPreviewUrls.TryGetValue(assetType, out var pendingPreview))
            return pendingPreview;

        return GetPreferredArtworkVariant(assetType)?.ImageUrl;
    }

    protected int GetArtworkAssetCount(string assetType) =>
        string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(_detail?.HeroUrl) ? 0 : 1)
            : GetArtworkVariants(assetType).Count;

    protected string GetArtworkSourceLabel(string assetType)
    {
        if (string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(_detail?.HeroUrl) ? "No generated hero banner yet." : "Generated from preferred artwork.";

        if (HasPendingArtwork(assetType))
            return $"Pending upload: {GetPendingArtworkFileName(assetType)}";

        var variant = GetPreferredArtworkVariant(assetType);
        if (variant is null)
            return GetArtworkEmptyStateLabel(assetType);

        return variant.Origin switch
        {
            "Uploaded" => "Preferred uploaded artwork.",
            "Provider" when !string.IsNullOrWhiteSpace(variant.ProviderName) => $"Preferred provider artwork from {variant.ProviderName}.",
            "Provider" => "Preferred provider artwork.",
            _ => "Preferred stored artwork.",
        };
    }

    protected ArtworkStateBadge GetArtworkStateBadge(string assetType)
    {
        if (HasPendingArtwork(assetType))
            return new("Pending", "pending");

        var variant = GetPreferredArtworkVariant(assetType);
        if (variant is null)
            return new("Missing", "missing");

        return variant.Origin switch
        {
            "Uploaded" => new("Uploaded", "uploaded"),
            "Provider" => new(string.IsNullOrWhiteSpace(variant.ProviderName) ? "Provider" : variant.ProviderName, "provider"),
            _ => new("Stored", "stored"),
        };
    }

    protected string GetArtworkActionLabel(string assetType) => "Add Variant";

    protected string GetArtworkAcceptedTypes(string assetType) =>
        string.Equals(assetType, "Logo", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/png,image/jpeg";

    protected void SetArtworkDragTarget(string assetType) => _dragTargetArtworkType = assetType;

    protected void ClearArtworkDragTarget(string assetType)
    {
        if (string.Equals(_dragTargetArtworkType, assetType, StringComparison.OrdinalIgnoreCase))
            _dragTargetArtworkType = null;
    }

    protected bool IsArtworkDragTarget(string assetType) =>
        string.Equals(_dragTargetArtworkType, assetType, StringComparison.OrdinalIgnoreCase);

    protected IReadOnlyList<ArtworkVariantDto> GetArtworkVariants(string assetType) =>
        _artwork?.Slots.FirstOrDefault(slot =>
            string.Equals(slot.AssetType, assetType, StringComparison.OrdinalIgnoreCase))?.Variants
        ?? [];

    protected ArtworkVariantDto? GetPreferredArtworkVariant(string assetType) =>
        GetArtworkVariants(assetType)
            .OrderByDescending(variant => variant.IsPreferred)
            .ThenByDescending(variant => variant.CreatedAt)
            .FirstOrDefault();

    protected IReadOnlyList<ArtworkVariantDisplayItem> GetArtworkGalleryItems(string assetType)
    {
        var items = new List<ArtworkVariantDisplayItem>();

        if (_pendingArtworkPreviewUrls.TryGetValue(assetType, out var pendingPreview))
        {
            items.Add(new ArtworkVariantDisplayItem(
                BuildPendingArtworkKey(assetType),
                Guid.Empty,
                assetType,
                pendingPreview,
                IsPreferred: false,
                IsPending: true,
                CanDelete: false,
                Origin: "Pending",
                ProviderName: null,
                CreatedAt: null));
        }

        items.AddRange(GetArtworkVariants(assetType).Select(variant => new ArtworkVariantDisplayItem(
            BuildVariantKey(variant.Id),
            variant.Id,
            assetType,
            variant.ImageUrl,
            variant.IsPreferred,
            IsPending: false,
            CanDelete: variant.CanDelete,
            Origin: variant.Origin,
            ProviderName: variant.ProviderName,
            CreatedAt: variant.CreatedAt)));

        return items;
    }

    protected ArtworkVariantDisplayItem? GetLeadArtworkVariant(string assetType)
    {
        var items = GetArtworkGalleryItems(assetType);
        if (items.Count == 0)
            return null;

        return items.FirstOrDefault(item => string.Equals(item.Key, _focusedArtworkVariantKey, StringComparison.Ordinal))
               ?? items.FirstOrDefault(item => item.IsPending)
               ?? items.FirstOrDefault(item => item.IsPreferred)
               ?? items.FirstOrDefault();
    }

    protected void SelectArtworkSlot(string assetType)
    {
        _selectedArtworkAssetType = assetType;
        _focusedArtworkVariantKey = GetArtworkGalleryItems(assetType).FirstOrDefault(item => item.IsPending)?.Key
                                    ?? GetArtworkGalleryItems(assetType).FirstOrDefault(item => item.IsPreferred)?.Key
                                    ?? GetArtworkGalleryItems(assetType).FirstOrDefault()?.Key;
    }

    protected void FocusArtworkVariant(string variantKey) => _focusedArtworkVariantKey = variantKey;

    protected bool IsFocusedArtworkVariant(string variantKey) =>
        string.Equals(_focusedArtworkVariantKey, variantKey, StringComparison.Ordinal);

    protected async Task SetPreferredArtworkVariantAsync(Guid variantId)
    {
        if (variantId == Guid.Empty)
            return;

        var ok = await ApiClient.SetPreferredArtworkAsync(variantId);
        if (!ok)
        {
            Snackbar.Add("Could not change the preferred artwork.", Severity.Error);
            return;
        }

        await RefreshArtworkStateAsync();
    }

    protected async Task DeleteArtworkVariantAsync(Guid variantId)
    {
        if (variantId == Guid.Empty)
            return;

        var ok = await ApiClient.DeleteArtworkAsync(variantId);
        if (!ok)
        {
            Snackbar.Add("Could not delete the artwork variant.", Severity.Error);
            return;
        }

        await RefreshArtworkStateAsync();
    }

    protected void HandleCanonicalQueryInput(ChangeEventArgs args) =>
        _canonicalSearchQuery = args.Value?.ToString() ?? string.Empty;

    protected void SetCanonicalTargetGroup(string targetGroup)
    {
        _canonicalTargetGroup = targetGroup;
        _canonicalSearchQuery = BuildSuggestedSearchQuery();
        _canonicalSearchResponse = null;
        _selectedCandidateId = null;
        _selectedSuggestedFieldKeys.Clear();
    }

    protected async Task SearchCanonicalAsync()
    {
        if (!IsSingleItem)
            return;

        _searchingCanonical = true;
        StateHasChanged();

        try
        {
            var response = await ApiClient.SearchItemCanonicalAsync(
                CurrentEntityId,
                new ItemCanonicalSearchRequestDto
                {
                    MediaType = _detail?.MediaType ?? Request.MediaType,
                    TargetKind = GetCanonicalTargetKind(_canonicalTargetGroup),
                    TargetFieldGroup = _canonicalTargetGroup,
                    DraftFields = BuildDraftFields(),
                    QueryOverride = string.IsNullOrWhiteSpace(_canonicalSearchQuery) ? null : _canonicalSearchQuery.Trim(),
                });

            _canonicalSearchResponse = response;
            _selectedCandidateId = null;
            _selectedSuggestedFieldKeys.Clear();

            if (response is null)
            {
                Snackbar.Add("Canonical search failed.", Severity.Error);
                return;
            }

            foreach (var candidate in response.LinkedCandidates)
            {
                _selectedSuggestedFieldKeys[GetCandidateId(candidate)] = candidate.SuggestedFields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var candidate in response.RetailCandidates)
            {
                _selectedSuggestedFieldKeys[GetCandidateId(candidate)] = candidate.SuggestedFields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _searchingCanonical = false;
        }
    }

    protected string GetCanonicalSearchSubtitle()
    {
        var label = GetCanonicalTargetLabel(_canonicalTargetGroup);
        return Request.Mode == SharedMediaEditorMode.Review
            ? $"Resolve the blocking {label.ToLowerInvariant()} identity."
            : $"Find the canonical {label.ToLowerInvariant()} and apply only the fields you want.";
    }

    protected string GetCanonicalTargetLabel(string targetGroup) =>
        _schema.QuickSearchTargets.FirstOrDefault(target => string.Equals(target.Key, targetGroup, StringComparison.OrdinalIgnoreCase)).Label
        ?? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(targetGroup.Replace('_', ' '));

    protected string BuildRetailCandidateSubtitle(RetailCandidateDto candidate)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(candidate.Author))
            parts.Add(candidate.Author);
        else if (!string.IsNullOrWhiteSpace(candidate.Director))
            parts.Add(candidate.Director);

        if (!string.IsNullOrWhiteSpace(candidate.Year))
            parts.Add(candidate.Year);

        if (!string.IsNullOrWhiteSpace(candidate.ProviderName))
            parts.Add(candidate.ProviderName);

        return parts.Count > 0 ? string.Join(" | ", parts) : "Provider candidate";
    }

    protected void KeepCurrentCanonical()
    {
        _canonicalSearchResponse = null;
        _selectedCandidateId = null;
        Snackbar.Add("Kept the current canonical value.", Severity.Info);
    }

    protected async Task SaveAsPreferenceOnlyAsync()
    {
        if (_editedValues.Count == 0)
        {
            Snackbar.Add("There are no preference changes to save.", Severity.Info);
            return;
        }

        await SaveAsync();
    }

    protected async Task ApplyUnlinkedCanonicalAsync()
    {
        if (_canonicalSearchResponse is null)
            return;

        var fields = _canonicalSearchResponse.UnlinkedFields.Count > 0
            ? _canonicalSearchResponse.UnlinkedFields
            : BuildDraftFields()
                .Where(pair => MediaEditorSchemaCatalog.GetStrongFieldKeys(_canonicalTargetGroup).Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        if (fields.Count == 0)
        {
            Snackbar.Add("There are no canonical fields to apply.", Severity.Warning);
            return;
        }

        await ApplyCanonicalAsync(
            linkState: "text_only",
            providerName: null,
            providerItemId: null,
            requiredFields: fields,
            suggestedFields: [],
            acceptedSuggestedKeys: [],
            bridgeIds: [],
            qidFields: []);
    }

    protected void SelectCandidate(RetailCandidateDto candidate) => _selectedCandidateId = GetCandidateId(candidate);
    protected void SelectCandidate(UniverseCandidateDto candidate) => _selectedCandidateId = GetCandidateId(candidate);

    protected bool IsCandidateSelected(string candidateId) =>
        string.Equals(_selectedCandidateId, candidateId, StringComparison.Ordinal);

    protected bool IsSuggestedFieldSelected(string candidateId, string key) =>
        _selectedSuggestedFieldKeys.TryGetValue(candidateId, out var selected) && selected.Contains(key);

    protected void ToggleSuggestedField(string candidateId, string key, object? value)
    {
        var isChecked = value as bool? ?? string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value?.ToString(), "on", StringComparison.OrdinalIgnoreCase);

        if (!_selectedSuggestedFieldKeys.TryGetValue(candidateId, out var selected))
        {
            selected = [];
            _selectedSuggestedFieldKeys[candidateId] = selected;
        }

        if (isChecked)
            selected.Add(key);
        else
            selected.Remove(key);
    }

    protected async Task ApplyRetailCandidateAsync(RetailCandidateDto candidate)
    {
        if (!candidate.IsApplicable)
            return;

        await ApplyCanonicalAsync(
            candidate.LinkState,
            candidate.ProviderName,
            candidate.ProviderItemId,
            candidate.RequiredFields,
            candidate.SuggestedFields,
            GetAcceptedSuggestedKeys(GetCandidateId(candidate)),
            candidate.BridgeIds,
            candidate.QidFields);
    }

    protected async Task ApplyLinkedCandidateAsync(UniverseCandidateDto candidate)
    {
        if (!candidate.IsApplicable)
            return;

        await ApplyCanonicalAsync(
            candidate.LinkState,
            providerName: null,
            providerItemId: null,
            requiredFields: candidate.RequiredFields,
            suggestedFields: candidate.SuggestedFields,
            acceptedSuggestedKeys: GetAcceptedSuggestedKeys(GetCandidateId(candidate)),
            bridgeIds: candidate.BridgeIds,
            qidFields: candidate.QidFields);
    }

    private async Task ApplyCanonicalAsync(
        string linkState,
        string? providerName,
        string? providerItemId,
        Dictionary<string, string> requiredFields,
        Dictionary<string, string> suggestedFields,
        List<string> acceptedSuggestedKeys,
        Dictionary<string, string> bridgeIds,
        Dictionary<string, string> qidFields)
    {
        var response = await ApiClient.ApplyItemCanonicalAsync(
            CurrentEntityId,
            new ItemCanonicalApplyRequestDto
            {
                TargetKind = GetCanonicalTargetKind(_canonicalTargetGroup),
                TargetFieldGroup = _canonicalTargetGroup,
                LinkState = linkState,
                ProviderName = providerName,
                ProviderItemId = providerItemId,
                RequiredFields = requiredFields,
                SuggestedFields = suggestedFields,
                AcceptedSuggestedKeys = acceptedSuggestedKeys,
                BridgeIds = bridgeIds,
                QidFields = qidFields,
            });

        if (response is null)
        {
            Snackbar.Add("Canonical apply failed.", Severity.Error);
            return;
        }

        Snackbar.Add(response.Message, Severity.Success);
        MudDialog.Close(DialogResult.Ok(true));
    }

    private List<string> GetAcceptedSuggestedKeys(string candidateId) =>
        _selectedSuggestedFieldKeys.TryGetValue(candidateId, out var selected)
            ? selected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    private string GetCandidateId(RetailCandidateDto candidate) =>
        $"retail:{candidate.CandidateId}:{candidate.ProviderName}:{candidate.ProviderItemId}";

    private string GetCandidateId(UniverseCandidateDto candidate) =>
        $"linked:{candidate.CandidateId}:{candidate.Qid}";

    private string BuildHeaderSubtitle()
    {
        if (_detail is null)
            return string.Empty;

        var parts = new List<string>();

        var creator = _detail.MediaType switch
        {
            "Music" => GetBaselineValue("artist"),
            "Movies" => _detail.Director,
            "TV" => GetBaselineValue("show_name"),
            "Audiobooks" => _detail.Narrator ?? _detail.Author,
            _ => _detail.Author,
        };

        if (!string.IsNullOrWhiteSpace(creator))
            parts.Add(creator);

        if (!string.IsNullOrWhiteSpace(GetBaselineValue("album")) && _detail.MediaType == "Music")
            parts.Add(GetBaselineValue("album"));

        if (!string.IsNullOrWhiteSpace(_detail.Year))
            parts.Add(_detail.Year);

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string BuildSuggestedSearchQuery()
    {
        var values = BuildDraftFields();
        string[] parts = _canonicalTargetGroup switch
        {
            "album" => new[] { GetValue(values, "artist"), GetValue(values, "album") },
            "artist" => new[] { GetValue(values, "artist") },
            "track" => new[] { GetValue(values, "artist"), GetValue(values, "title") },
            "movie_identity" => new[] { GetValue(values, "title"), GetValue(values, "year") },
            "show_episode" => new[] { GetValue(values, "show_name"), SeasonEpisodeLabel(values) },
            "show" => new[] { GetValue(values, "show_name") },
            "book_identity" => new[] { GetValue(values, "title"), GetValue(values, "author") },
            "audiobook_identity" => new[] { GetValue(values, "title"), GetValue(values, "author"), GetValue(values, "narrator") },
            "narrator" => new[] { GetValue(values, "narrator") },
            "series" => new[] { GetValue(values, "series") },
            "issue" => new[] { GetValue(values, "series"), GetValue(values, "series_position"), GetValue(values, "title") },
            _ => new[] { GetValue(values, "title") },
        };

        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private Dictionary<string, string> BuildDraftFields()
    {
        var merged = MediaEditorSchemaCatalog.BuildValueMap(_detail, _canonicalValues)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var edit in _editedValues)
            merged[edit.Key] = edit.Value;

        return merged
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private string GetBaselineValue(string key)
    {
        if (IsBatchMode)
            return string.Empty;

        var values = MediaEditorSchemaCatalog.BuildValueMap(_detail, _canonicalValues);
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string SeasonEpisodeLabel(IReadOnlyDictionary<string, string> values)
    {
        var season = GetValue(values, "season_number");
        var episode = GetValue(values, "episode_number");
        var title = GetValue(values, "episode_title");
        return string.Join(" ", new[] { season, episode, title }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetCanonicalTargetKind(string targetGroup) =>
        targetGroup switch
        {
            "artist" or "narrator" => "person",
            "album" or "series" or "show" => "container",
            _ => "item",
        };

    private static string FormatProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return "Provider";

        return providerName switch
        {
            "fanart_tv" => "Fanart.tv",
            "tmdb" => "TMDB",
            "musicbrainz" => "MusicBrainz",
            "wikidata_reconciliation" => "Wikidata",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(providerName.Replace('_', ' ')),
        };
    }

    private string GetArtworkTabExplanation()
    {
        var mediaType = _detail?.MediaType ?? Request.MediaType ?? _selectedMediaType;

        return mediaType switch
        {
            "Movies" or "TV" =>
                "Showing Poster / Cover, Square Art, Background, Banner, and Logo. Square Art stays empty until a real square asset exists. Background, Banner, and Logo come from uploaded typed artwork or provider enrichment when available.",
            "Music" =>
                "Showing Album Art, Square Art, Background, and Logo. Banner is not shown for music. Square Art stays empty until a real square asset exists. Background and Logo come from uploaded typed artwork or provider enrichment when available.",
            "Books" or "Audiobooks" or "Comics" =>
                "Showing Cover, Square Art, and Background. Banner and Logo are not shown for this media type. Square Art stays empty until a real square asset exists. Background uses uploaded typed artwork or provider enrichment when available.",
            _ =>
                "Showing the artwork slots available for this media type. Square Art stays empty until a real square asset exists. Wide art comes from uploaded typed artwork or provider enrichment when available.",
        };
    }

    private static string GetArtworkEmptyStateLabel(string assetType) =>
        assetType switch
        {
            "SquareArt" => "No square art stored yet.",
            "Background" => "No background art stored yet.",
            "Banner" => "No banner art stored yet.",
            "Logo" => "No logo art stored yet.",
            "CoverArt" => "No cover art stored yet.",
            _ => "No artwork stored yet.",
        };

    private static IReadOnlyList<ArtworkSlotDefinition> ResolveArtworkSlots(string? mediaType) =>
        mediaType switch
        {
            "Movies" or "TV" =>
            [
                PosterCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
            ],
            "Music" =>
            [
                AlbumArtArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                LogoArtworkSlot,
            ],
            "Books" or "Audiobooks" or "Comics" =>
            [
                BookCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
            ],
            _ =>
            [
                PosterCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
            ],
        };

    private async Task RefreshArtworkStateAsync()
    {
        if (!IsSingleItem)
            return;

        var detailTask = ApiClient.GetRegistryItemDetailAsync(CurrentEntityId);
        var canonicalTask = Orchestrator.GetCanonicalValuesAsync(CurrentEntityId);
        var artworkTask = ApiClient.GetArtworkAsync(CurrentEntityId);

        await Task.WhenAll(detailTask, canonicalTask, artworkTask);

        _detail = detailTask.Result;
        _canonicalValues = canonicalTask.Result;
        _artwork = artworkTask.Result ?? new ArtworkEditorDto { EntityId = CurrentEntityId };
        NormalizeArtworkSelection();
        StateHasChanged();
    }

    private void NormalizeArtworkSelection()
    {
        var availableSlots = ArtworkSlots;
        if (availableSlots.Count == 0)
        {
            _selectedArtworkAssetType = null;
            _focusedArtworkVariantKey = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedArtworkAssetType)
            || !availableSlots.Any(slot => string.Equals(slot.AssetType, _selectedArtworkAssetType, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedArtworkAssetType = availableSlots[0].AssetType;
        }

        var selectedAssetType = _selectedArtworkAssetType!;
        var validKeys = GetArtworkGalleryItems(selectedAssetType).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        if (validKeys.Count == 0)
        {
            _focusedArtworkVariantKey = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_focusedArtworkVariantKey) || !validKeys.Contains(_focusedArtworkVariantKey))
        {
            _focusedArtworkVariantKey = GetArtworkGalleryItems(selectedAssetType).FirstOrDefault(item => item.IsPending)?.Key
                                        ?? GetArtworkGalleryItems(selectedAssetType).FirstOrDefault(item => item.IsPreferred)?.Key
                                        ?? GetArtworkGalleryItems(selectedAssetType).First().Key;
        }
    }

    private static string BuildPendingArtworkKey(string assetType) =>
        $"pending:{assetType.ToLowerInvariant()}";

    private static string BuildVariantKey(Guid variantId) =>
        variantId == Guid.Empty ? "synthetic" : variantId.ToString("D");

    protected sealed record ArtworkSlotDefinition(
        string AssetType,
        string Label,
        string Description,
        string Icon,
        string PreviewClass,
        string ImageClass,
        bool UploadEnabled,
        string UploadHelp,
        string MetaLabel);

    protected sealed record ArtworkStateBadge(string Label, string Tone);
    protected sealed record ArtworkVariantDisplayItem(
        string Key,
        Guid VariantId,
        string AssetType,
        string? ImageUrl,
        bool IsPreferred,
        bool IsPending,
        bool CanDelete,
        string Origin,
        string? ProviderName,
        DateTimeOffset? CreatedAt);

    protected string? GetUniverseExploreUrl() =>
        !string.IsNullOrWhiteSpace(_detail?.UniverseSummary?.UniverseQid)
            ? $"/universe/{_detail.UniverseSummary.UniverseQid}/explore"
            : null;

    protected string GetArtworkSlotMeta(ArtworkSlotDefinition slot) => slot.MetaLabel;

    protected string FormatUniverseStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "Pending"
            : status.Replace("_", " ");
}
