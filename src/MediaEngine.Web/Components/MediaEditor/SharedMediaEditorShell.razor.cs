using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MediaEngine.Web.Components.Navigation;
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
        new("CoverArt", "Poster / Cover", "Primary art used on cards and detail pages.", Icons.Material.Outlined.Photo, "portrait", "fit", true, "Best for posters and front-cover artwork.", "Primary");

    private static readonly ArtworkSlotDefinition BookCoverArtworkSlot =
        new("CoverArt", "Cover", "Primary front-cover art used on cards and detail pages.", Icons.Material.Outlined.MenuBook, "portrait", "fit", true, "Best for book, comic, and audiobook covers.", "Primary");

    private static readonly ArtworkSlotDefinition AlbumArtArtworkSlot =
        new("CoverArt", "Album Art", "Primary album art used across music views.", Icons.Material.Outlined.Album, "square", "fit", true, "Best for album covers and primary music art.", "Primary");

    private static readonly ArtworkSlotDefinition SquareArtArtworkSlot =
        new("SquareArt", "Square Art", "A dedicated square crop for tiles, shelves, and compact layouts.", Icons.Material.Outlined.CropSquare, "square", "fit", true, "Best for square variants that should not be auto-cropped from the primary cover.", "Square");

    private static readonly ArtworkSlotDefinition BackgroundArtworkSlot =
        new("Background", "Background", "A cinematic wide image for backgrounds and immersive layouts.", Icons.Material.Outlined.Panorama, "background", "fit", true, "Best for scenic or full-bleed background art.", "Wide");

    private static readonly ArtworkSlotDefinition BannerArtworkSlot =
        new("Banner", "Banner", "A wide promotional strip for shelves and collection headers.", Icons.Material.Outlined.PanoramaWideAngle, "banner", "fit", true, "Best for landscape banners and shelf headers.", "Strip");

    private static readonly ArtworkSlotDefinition LogoArtworkSlot =
        new("Logo", "Logo", "Title treatment or transparent branding art.", Icons.Material.Outlined.BrandingWatermark, "logo", "logo", true, "Best for transparent logos or wordmarks.", "Logo");

    private static readonly ArtworkSlotDefinition SeasonPosterArtworkSlot =
        new("SeasonPoster", "Season Poster", "Poster art stored for the season container.", Icons.Material.Outlined.ViewAgenda, "portrait", "fit", true, "Best for season-specific poster art.", "Season");

    private static readonly ArtworkSlotDefinition SeasonThumbArtworkSlot =
        new("SeasonThumb", "Season Thumb", "A wide season still or season thumbnail.", Icons.Material.Outlined.PhotoSizeSelectLarge, "background", "fit", true, "Best for season-specific thumbnail art.", "Season");

    private static readonly ArtworkSlotDefinition EpisodeStillArtworkSlot =
        new("EpisodeStill", "Episode Still", "An episode-specific still image.", Icons.Material.Outlined.LiveTv, "background", "fit", true, "Best for episode stills or screenshots.", "Still");

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
    private MediaEditorContextDto? _editorContext;
    private MediaEditorSchema _schema = MediaEditorSchemaCatalog.Resolve(null);
    private readonly Dictionary<string, string> _editedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _selectedSuggestedFieldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBrowserFile> _pendingArtworkFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingArtworkPreviewUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _artworkUploadErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArtworkEditorDto> _artworkStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScopeEditorState> _scopeStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _artworkApplyingKeys = new(StringComparer.OrdinalIgnoreCase);
    private ItemCanonicalSearchResponseDto? _canonicalSearchResponse;
    private string _activeTab = "details";
    private string _activeScopeId = string.Empty;
    private string _canonicalTargetGroup = "";
    private string _canonicalSearchQuery = "";
    private string _artworkUrlInput = string.Empty;
    private string? _artworkAddMenuAssetType;
    private string? _selectedCandidateId;
    private string? _selectedArtworkAssetType;
    private string? _focusedArtworkVariantKey;
    private ArtworkSlotDefinition? _zoomArtworkSlot;
    private ArtworkVariantDisplayItem? _zoomArtworkVariant;
    private string _selectedMediaType = "Books";
    private bool _loading = true;
    private bool _saving;
    private bool _reclassifying;
    private bool _searchingCanonical;
    private bool _artworkUrlSubmitting;
    private bool _confirmDiscard;
    private string? _dragTargetArtworkType;
    private string _reviewSummary = "Review the item identity.";
    private string _lastNonFileTab = "details";
    private bool _showArtworkUrlInput;

    protected IReadOnlyList<(string Id, string Label)> Tabs => ResolveVisibleTabs();
    protected IReadOnlyList<(string Key, string Label)> QuickSearchTargets => ResolveQuickSearchTargets();
    protected IReadOnlyList<ArtworkSlotDefinition> ArtworkSlots => ResolveArtworkSlots(ArtworkScope);
    protected bool SupportsCanonicalSearch => QuickSearchTargets.Count > 0;
    protected string? CanonicalSearchUnavailableReason => GetCanonicalSearchUnavailableReason();
    protected IReadOnlyList<AppNavItem> TabItems =>
        Tabs.Select(tab => new AppNavItem
        {
            Key = tab.Id,
            Label = tab.Label,
            Disabled = IsTabDisabled(tab.Id)
        }).ToList();
    protected IReadOnlyList<AppNavItem> ScopeTabItems =>
        (_editorContext?.Scopes ?? [])
            .OrderBy(scope => scope.Order)
            .Select(scope => new AppNavItem { Key = scope.ScopeId, Label = scope.Label })
            .ToList();
    protected IReadOnlyList<AppNavItem> QuickSearchTargetItems =>
        QuickSearchTargets
            .Select(target => new AppNavItem { Key = target.Key, Label = target.Label })
            .ToList();
    protected IReadOnlyList<AppNavItem> ArtworkSlotItems =>
        ArtworkSlots
            .Select(slot => new AppNavItem
            {
                Key = slot.AssetType,
                Label = slot.Label,
                Icon = slot.Icon,
                Description = slot.MetaLabel,
                Badge = FormatCountBadge(GetArtworkGalleryItems(slot.AssetType).Count)
            })
            .ToList();
    protected bool IsSingleItem => Request.EntityIds.Count == 1;
    protected bool IsBatchMode => Request.Mode == SharedMediaEditorMode.Batch || Request.EntityIds.Count > 1;
    protected Guid LaunchEntityId => Request.LaunchEntityId ?? Request.EntityIds[0];
    protected Guid CurrentEntityId => ActiveScope?.FieldEntityId ?? LaunchEntityId;
    protected bool IsDirty => _editedValues.Count > 0 || _pendingArtworkFiles.Count > 0;
    protected bool IsArtworkBusy => _artworkUrlSubmitting || _artworkApplyingKeys.Count > 0;
    protected bool HasGeneratedHeroArtwork => !string.IsNullOrWhiteSpace(GetGeneratedHeroUrl());
    protected string ArtworkTabExplanation => GetArtworkTabExplanation();
    protected ArtworkSlotDefinition? SelectedArtworkSlot =>
        ArtworkSlots.FirstOrDefault(slot => string.Equals(slot.AssetType, _selectedArtworkAssetType, StringComparison.OrdinalIgnoreCase))
        ?? ArtworkSlots.FirstOrDefault();
    protected ArtworkVariantDisplayItem? LeadArtworkVariant =>
        SelectedArtworkSlot is null ? null : GetLeadArtworkVariant(SelectedArtworkSlot.AssetType);
    protected ArtworkSlotDefinition? ZoomArtworkSlot => _zoomArtworkSlot;
    protected ArtworkVariantDisplayItem? ZoomArtworkVariant => _zoomArtworkVariant;
    protected bool IsArtworkZoomOpen => _zoomArtworkSlot is not null && _zoomArtworkVariant is not null;

    protected MediaEditorScopeDto? ActiveScope =>
        _editorContext?.Scopes
            .OrderBy(scope => scope.Order)
            .FirstOrDefault(scope => string.Equals(scope.ScopeId, _activeScopeId, StringComparison.OrdinalIgnoreCase))
        ?? _editorContext?.Scopes.OrderBy(scope => scope.Order).FirstOrDefault();
    protected MediaEditorScopeDto? ArtworkScope => ActiveScope;
    protected bool IsFileScope => string.Equals(ActiveScope?.ScopeId, "file", StringComparison.OrdinalIgnoreCase);
    protected string BreadcrumbText => BuildBreadcrumbText();

    protected string HeaderKicker =>
        Request.Mode switch
        {
            SharedMediaEditorMode.Review => "Review",
            SharedMediaEditorMode.Batch => $"{Request.EntityIds.Count} items",
            _ => ActiveScope?.Label ?? _schema.MediaType,
        };

    protected string HeaderTitle =>
        ActiveScope?.DisplayTitle
        ?? Request.HeaderTitle
        ?? _detail?.Title
        ?? (IsBatchMode ? $"Edit {Request.EntityIds.Count} Items" : "Edit Item");

    protected string? HeaderSubtitle =>
        ActiveScope?.DisplaySubtitle
        ?? Request.HeaderSubtitle
        ?? (IsSingleItem ? BuildHeaderSubtitle() : string.Join(" | ", Request.PreviewItems.Take(3).Select(x => x.Title)));

    protected string? CurrentCoverUrl => GetHeaderArtworkPreviewUrl();

    private sealed class ScopeEditorState
    {
        public RegistryItemDetailViewModel? Detail { get; init; }
        public List<CanonicalFieldViewModel> CanonicalValues { get; init; } = [];
        public List<ClaimHistoryDto> Claims { get; init; } = [];
        public List<RegistryItemHistoryDto> History { get; init; } = [];
        public ArtworkEditorDto Artwork { get; init; } = new();
    }

    protected override async Task OnInitializedAsync()
    {
        _activeTab = string.IsNullOrWhiteSpace(Request.InitialTab) ? "details" : Request.InitialTab;
        _lastNonFileTab = _activeTab == "file" ? "details" : _activeTab;
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
            _artworkStates.Clear();
            _editorContext = await ApiClient.GetMediaEditorContextAsync(LaunchEntityId);

            if (_editorContext is null)
            {
                var detailTask = ApiClient.GetRegistryItemDetailAsync(LaunchEntityId);
                var canonicalTask = Orchestrator.GetCanonicalValuesAsync(LaunchEntityId);
                var claimsTask = Orchestrator.GetClaimHistoryAsync(LaunchEntityId);
                var historyTask = ApiClient.GetItemHistoryAsync(LaunchEntityId);
                var artworkTask = ApiClient.GetArtworkAsync(LaunchEntityId);

                await Task.WhenAll(detailTask, canonicalTask, claimsTask, historyTask, artworkTask);

                _detail = detailTask.Result;
                _canonicalValues = canonicalTask.Result;
                _claims = claimsTask.Result;
                _history = historyTask.Result;
                _artwork = artworkTask.Result ?? new ArtworkEditorDto { EntityId = LaunchEntityId };
                _schema = MediaEditorSchemaCatalog.Resolve(_detail?.MediaType ?? Request.MediaType);
                _selectedMediaType = _detail?.MediaType ?? Request.MediaType ?? "Books";
            }
            else
            {
                _selectedMediaType = _editorContext.MediaType;
                _schema = MediaEditorSchemaCatalog.Resolve(_editorContext.MediaType);
                _activeScopeId = !string.IsNullOrWhiteSpace(Request.InitialScope)
                    ? Request.InitialScope!
                    : _editorContext.InitialScope;

                if (!_editorContext.Scopes.Any(scope => string.Equals(scope.ScopeId, _activeScopeId, StringComparison.OrdinalIgnoreCase)))
                    _activeScopeId = _editorContext.Scopes.OrderBy(scope => scope.Order).FirstOrDefault()?.ScopeId ?? string.Empty;

                await LoadScopeStateAsync(forceReload: true);
            }

            _pendingArtworkFiles.Clear();
            _pendingArtworkPreviewUrls.Clear();
            _artworkUploadErrors.Clear();
            _dragTargetArtworkType = null;
            NormalizeArtworkSelection();

            if (Request.Mode == SharedMediaEditorMode.Review)
            {
                var target = ReviewTargetResolver.Resolve(_detail?.MediaType ?? Request.MediaType, Request.ReviewTrigger ?? _detail?.ReviewTrigger);
                _activeTab = string.IsNullOrWhiteSpace(Request.InitialTab) ? target.InitialTab : Request.InitialTab!;
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup)
                    ? (ActiveScope?.CanonicalTargetGroup ?? target.CanonicalTargetGroup)
                    : Request.InitialCanonicalTargetGroup!;
                _reviewSummary = target.Summary;
            }
            else
            {
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup)
                    ? (ActiveScope?.CanonicalTargetGroup ?? _schema.DefaultTargetGroup)
                    : Request.InitialCanonicalTargetGroup!;
            }

            if (IsFileScope)
                _activeTab = "file";

            EnsureActiveTabVisible();
            _canonicalSearchQuery = BuildSuggestedSearchQuery();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task LoadScopeStateAsync(bool forceReload = false)
    {
        if (!IsSingleItem || _editorContext is null || ActiveScope is null)
            return;

        if (!forceReload && _scopeStates.TryGetValue(ActiveScope.ScopeId, out var cachedState))
        {
            ApplyScopeState(cachedState);
            return;
        }

        var detailTask = ApiClient.GetRegistryItemDetailAsync(ActiveScope.FieldEntityId);
        var canonicalTask = Orchestrator.GetCanonicalValuesAsync(ActiveScope.FieldEntityId);
        var claimsTask = Orchestrator.GetClaimHistoryAsync(ActiveScope.FieldEntityId);
        var historyTask = ApiClient.GetItemHistoryAsync(ActiveScope.FieldEntityId);
        var activeScopeSlots = ResolveArtworkSlots(ActiveScope);
        var artworkTask = ActiveScope.CanEditArtwork || activeScopeSlots.Count > 0
            ? ApiClient.GetScopeArtworkAsync(LaunchEntityId, ActiveScope.ScopeId)
            : Task.FromResult<ArtworkEditorDto?>(new ArtworkEditorDto { EntityId = ActiveScope.ArtworkOwnerEntityId ?? ActiveScope.FieldEntityId });

        await Task.WhenAll(detailTask, canonicalTask, claimsTask, historyTask, artworkTask);

        var state = new ScopeEditorState
        {
            Detail = detailTask.Result,
            CanonicalValues = canonicalTask.Result,
            Claims = claimsTask.Result,
            History = historyTask.Result,
            Artwork = artworkTask.Result ?? new ArtworkEditorDto { EntityId = ActiveScope.ArtworkOwnerEntityId ?? ActiveScope.FieldEntityId },
        };

        _scopeStates[ActiveScope.ScopeId] = state;
        _artworkStates[ActiveScope.ScopeId] = state.Artwork;
        ApplyScopeState(state);
    }

    private void ApplyScopeState(ScopeEditorState state)
    {
        _detail = state.Detail;
        _canonicalValues = state.CanonicalValues;
        _claims = state.Claims;
        _history = state.History;
        _artwork = state.Artwork;
        if (ActiveScope is not null)
            _artworkStates[ActiveScope.ScopeId] = state.Artwork;
        _selectedMediaType = _detail?.MediaType ?? _editorContext?.MediaType ?? Request.MediaType ?? "Books";
        _schema = MediaEditorSchemaCatalog.Resolve(_selectedMediaType);
        _canonicalTargetGroup = ActiveScope?.CanonicalTargetGroup ?? _schema.DefaultTargetGroup;
        _canonicalSearchQuery = BuildSuggestedSearchQuery();
        _canonicalSearchResponse = null;
        _selectedCandidateId = null;
        _selectedSuggestedFieldKeys.Clear();
        CloseArtworkZoom();
        EnsureActiveTabVisible();
        NormalizeArtworkSelection();
    }

    protected async Task SelectScopeAsync(string scopeId)
    {
        if (string.Equals(_activeScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(_activeTab) && !string.Equals(_activeTab, "file", StringComparison.OrdinalIgnoreCase))
            _lastNonFileTab = _activeTab;

        _activeScopeId = scopeId;
        _artworkAddMenuAssetType = null;
        _artworkUrlInput = string.Empty;
        _showArtworkUrlInput = false;

        if (string.Equals(scopeId, "file", StringComparison.OrdinalIgnoreCase))
        {
            _activeTab = "file";
        }
        else if (string.Equals(_activeTab, "file", StringComparison.OrdinalIgnoreCase))
        {
            _activeTab = _lastNonFileTab;
        }

        _loading = true;
        StateHasChanged();

        try
        {
            await LoadScopeStateAsync();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    protected async Task SelectArtworkScopeAsync(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
            return;

        _activeTab = "artwork";

        if (string.Equals(_activeScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
        {
            EnsureActiveTabVisible();
            return;
        }

        await SelectScopeAsync(scopeId);
    }

    private async Task LoadArtworkStateAsync(string? scopeId, bool forceReload = false)
    {
        if (!IsSingleItem || string.IsNullOrWhiteSpace(scopeId))
            return;

        if (!forceReload && _artworkStates.ContainsKey(scopeId))
            return;

        var scope = GetScopeById(scopeId);
        if (scope is null)
            return;

        var slots = ResolveArtworkSlots(scope);
        if (!scope.CanEditArtwork && slots.Count == 0)
        {
            _artworkStates[scope.ScopeId] = new ArtworkEditorDto { EntityId = scope.ArtworkOwnerEntityId ?? scope.FieldEntityId };
            return;
        }

        var artwork = await ApiClient.GetScopeArtworkAsync(LaunchEntityId, scope.ScopeId)
                      ?? new ArtworkEditorDto { EntityId = scope.ArtworkOwnerEntityId ?? scope.FieldEntityId };
        _artworkStates[scope.ScopeId] = artwork;
    }

    private Task LoadBatchAsync()
    {
        var mediaTypes = Request.PreviewItems.Select(x => x.MediaType ?? Request.MediaType ?? "Books");
        _schema = MediaEditorSchemaCatalog.Resolve(mediaTypes.FirstOrDefault());
        _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup) ? _schema.DefaultTargetGroup : Request.InitialCanonicalTargetGroup!;
        _loading = false;
        return Task.CompletedTask;
    }

    protected bool IsTabDisabled(string tabId) => !IsTabVisible(tabId);

    protected bool CanReclassifyMediaType =>
        IsSingleItem
        && !IsFileScope
        && (Request.Mode == SharedMediaEditorMode.Review || !string.IsNullOrWhiteSpace(_detail?.MediaType));

    protected IEnumerable<MediaEditorFieldGroup> GetGroupsForTab(string tabId)
    {
        if (!IsBatchMode)
        {
            var visibleKeys = GetVisibleFieldKeysForScope();
            var sourceTabIds = string.Equals(tabId, "options", StringComparison.OrdinalIgnoreCase)
                ? new[] { "options", "sorting" }
                : new[] { tabId };

            return _schema.Groups
                .Where(group => sourceTabIds.Contains(group.TabId, StringComparer.OrdinalIgnoreCase))
                .Select(group => new MediaEditorFieldGroup
                {
                    Id = group.Id,
                    Label = string.Equals(tabId, "options", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(group.TabId, "sorting", StringComparison.OrdinalIgnoreCase)
                        ? "Sort Order"
                        : group.Label,
                    TabId = tabId,
                    Fields = group.Fields
                        .Where(field => visibleKeys.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
                        .ToList(),
                })
                .Where(group => group.Fields.Count > 0)
                .ToList();
        }

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
                new[]
                {
                    new MediaEditorFieldGroup
                    {
                        Id = "batch_options",
                        Label = "Batch Options",
                        TabId = "options",
                        Fields = batchFields.Where(field => field.Key is "description" or "comment" or "rating").ToList(),
                    },
                    new MediaEditorFieldGroup
                    {
                        Id = "batch_sorting",
                        Label = "Sort Order",
                        TabId = "options",
                        Fields = batchFields.Where(field => field.Key.StartsWith("sort_", StringComparison.OrdinalIgnoreCase)).ToList(),
                    },
                }.Where(group => group.Fields.Count > 0).ToList(),
            _ => [],
        };
    }

    protected string GetPlaceholder(MediaEditorFieldDefinition field) =>
        IsBatchMode ? "Leave unchanged" : (field.Placeholder ?? field.Label);

    protected string GetEditableValue(string key)
    {
        if (_editedValues.TryGetValue(BuildScopedFieldKey(key), out var edited))
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
        var scopedKey = BuildScopedFieldKey(key);

        if (string.Equals(normalized, baseline, StringComparison.Ordinal))
        {
            _editedValues.Remove(scopedKey);
        }
        else if (string.IsNullOrWhiteSpace(normalized))
        {
            _editedValues.Remove(scopedKey);
        }
        else
        {
            _editedValues[scopedKey] = normalized;
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

            var fieldChangesByScope = _editedValues
                .Select(entry => (Key: ParseScopedKey(entry.Key), Value: entry.Value))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key.ScopeId) && ActiveScopeExists(entry.Key.ScopeId))
                .GroupBy(entry => entry.Key.ScopeId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fieldChangesByScope.Count > 0)
            {
                foreach (var scopeGroup in fieldChangesByScope)
                {
                    var scope = GetScopeById(scopeGroup.Key);
                    if (scope is null || !scope.CanEditFields)
                        continue;

                    var fields = scopeGroup.ToDictionary(entry => entry.Key.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
                    var saved = await ApiClient.SaveItemPreferencesAsync(scope.FieldEntityId, fields);
                    if (!saved)
                    {
                        Snackbar.Add($"Preference save failed for {scope.Label}.", Severity.Error);
                        return;
                    }
                }

                savedAnything = true;
            }

            foreach (var pendingArtwork in _pendingArtworkFiles.ToList())
            {
                var parsedKey = ParseScopedKey(pendingArtwork.Key);
                var scope = GetScopeById(parsedKey.ScopeId);
                if (scope is null)
                    continue;

                await using var stream = pendingArtwork.Value.OpenReadStream(10 * 1024 * 1024);
                var uploaded = await ApiClient.UploadScopeArtworkVariantAsync(
                    LaunchEntityId,
                    scope.ScopeId,
                    parsedKey.Key,
                    stream,
                    pendingArtwork.Value.Name);

                if (!uploaded)
                {
                    var label = ResolveArtworkSlots(scope).FirstOrDefault(slot =>
                        string.Equals(slot.AssetType, parsedKey.Key, StringComparison.OrdinalIgnoreCase))?.Label
                        ?? parsedKey.Key;
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
            _scopeStates.Clear();
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

        var scope = ArtworkScope;
        if (scope is null)
            return;

        var scopedKey = BuildScopedArtworkKey(scope.ScopeId, assetType);

        _pendingArtworkFiles[scopedKey] = file;
        _artworkUploadErrors.Remove(scopedKey);
        await using var stream = file.OpenReadStream(10 * 1024 * 1024);
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/png" : file.ContentType;
        _pendingArtworkPreviewUrls[scopedKey] = $"data:{contentType};base64,{Convert.ToBase64String(ms.ToArray())}";
        _dragTargetArtworkType = null;
        _selectedArtworkAssetType = assetType;
        _artworkAddMenuAssetType = null;
        _showArtworkUrlInput = false;
        _artworkUrlInput = string.Empty;
        NormalizeArtworkSelection();
        await UploadPendingArtworkAsync(scope, assetType, file, scopedKey);
    }

    protected void ClearArtworkSelection(string assetType)
    {
        var scopedKey = BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType);
        _pendingArtworkFiles.Remove(scopedKey);
        _pendingArtworkPreviewUrls.Remove(scopedKey);
        _artworkUploadErrors.Remove(scopedKey);
        if (string.Equals(_dragTargetArtworkType, assetType, StringComparison.OrdinalIgnoreCase))
            _dragTargetArtworkType = null;
        if (_zoomArtworkVariant is { IsPending: true } && string.Equals(_zoomArtworkVariant.AssetType, assetType, StringComparison.OrdinalIgnoreCase))
            CloseArtworkZoom();
        NormalizeArtworkSelection();
    }

    protected bool HasPendingArtwork(string assetType) =>
        _pendingArtworkFiles.ContainsKey(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType));

    protected string? GetPendingArtworkFileName(string assetType) =>
        _pendingArtworkFiles.TryGetValue(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType), out var file) ? file.Name : null;

    protected string? GetArtworkUploadError(string assetType) =>
        _artworkUploadErrors.TryGetValue(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType), out var error) ? error : null;

    protected bool HasArtworkUploadError(string assetType) =>
        !string.IsNullOrWhiteSpace(GetArtworkUploadError(assetType));

    protected string? GetArtworkPreviewUrl(string assetType)
    {
        if (string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase))
            return GetGeneratedHeroUrl();

        if (_pendingArtworkPreviewUrls.TryGetValue(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType), out var pendingPreview))
            return pendingPreview;

        return GetPreferredArtworkVariant(assetType)?.ImageUrl;
    }

    protected int GetArtworkAssetCount(string assetType) =>
        string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(GetGeneratedHeroUrl()) ? 0 : 1)
            : GetArtworkVariants(assetType).Count;

    protected string GetArtworkSourceLabel(string assetType)
    {
        if (string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(GetGeneratedHeroUrl()) ? "No generated hero banner yet." : "Generated from preferred artwork.";

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

    protected string GetArtworkActionLabel(string assetType) => "Add";

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

    private ArtworkEditorDto? GetCurrentArtworkEditor()
    {
        if (ArtworkScope is null)
            return _artwork;

        return _artworkStates.TryGetValue(ArtworkScope.ScopeId, out var artwork)
            ? artwork
            : _artwork;
    }

    private string? GetGeneratedHeroUrl()
    {
        if (ArtworkScope is null)
            return _detail?.HeroUrl;

        if (ActiveScope is not null && string.Equals(ArtworkScope.ScopeId, ActiveScope.ScopeId, StringComparison.OrdinalIgnoreCase))
            return _detail?.HeroUrl;

        return _scopeStates.TryGetValue(ArtworkScope.ScopeId, out var state)
            ? state.Detail?.HeroUrl
            : null;
    }

    protected IReadOnlyList<ArtworkVariantDto> GetArtworkVariants(string assetType) =>
        GetCurrentArtworkEditor()?.Slots.FirstOrDefault(slot =>
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

        if (_pendingArtworkPreviewUrls.TryGetValue(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType), out var pendingPreview))
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

    protected IReadOnlyList<ArtworkVariantDisplayItem> GetArtworkRowItems(string assetType) =>
        GetArtworkGalleryItems(assetType)
            .OrderByDescending(item => item.IsPreferred)
            .ThenByDescending(item => item.IsPending)
            .ThenByDescending(item => item.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

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

    protected void SelectArtworkSlot(string assetType) => ApplyArtworkSlotSelection(assetType);

    protected Task SelectTabAsync(string tabId)
    {
        var normalizedTabId = string.Equals(tabId, "sorting", StringComparison.OrdinalIgnoreCase) ? "options" : tabId;
        if (!IsTabDisabled(normalizedTabId))
        {
            _activeTab = normalizedTabId;
            if (!string.Equals(normalizedTabId, "file", StringComparison.OrdinalIgnoreCase))
                _lastNonFileTab = normalizedTabId;
        }

        return Task.CompletedTask;
    }

    protected Task SetCanonicalTargetGroupAsync(string targetGroup)
    {
        SetCanonicalTargetGroup(targetGroup);
        return Task.CompletedTask;
    }

    protected Task SelectArtworkSlotAsync(string assetType)
    {
        ApplyArtworkSlotSelection(assetType);
        return Task.CompletedTask;
    }

    protected bool IsArtworkAddMenuOpen(string assetType) =>
        string.Equals(_artworkAddMenuAssetType, assetType, StringComparison.OrdinalIgnoreCase);

    protected void ToggleArtworkAddMenu(string assetType)
    {
        var isOpen = IsArtworkAddMenuOpen(assetType);
        ApplyArtworkSlotSelection(assetType, clearTransientUi: false);

        if (isOpen)
        {
            _artworkAddMenuAssetType = null;
            _showArtworkUrlInput = false;
            _artworkUrlInput = string.Empty;
            return;
        }

        _artworkAddMenuAssetType = assetType;
        _showArtworkUrlInput = false;
        _artworkUrlInput = string.Empty;
    }

    protected void OpenArtworkUrlInput(string assetType)
    {
        ApplyArtworkSlotSelection(assetType, clearTransientUi: false);
        _artworkAddMenuAssetType = assetType;
        _artworkUrlInput = string.Empty;
        _showArtworkUrlInput = true;
    }

    private void ApplyArtworkSlotSelection(string assetType, bool clearTransientUi = true)
    {
        _selectedArtworkAssetType = assetType;
        if (clearTransientUi)
        {
            _artworkAddMenuAssetType = null;
            _artworkUrlInput = string.Empty;
            _showArtworkUrlInput = false;
        }

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

    protected async Task RetryArtworkUploadAsync(string assetType)
    {
        var scope = ArtworkScope;
        if (scope is null)
            return;

        var scopedKey = BuildScopedArtworkKey(scope.ScopeId, assetType);
        if (!_pendingArtworkFiles.TryGetValue(scopedKey, out var file))
            return;

        await UploadPendingArtworkAsync(scope, assetType, file, scopedKey);
    }

    protected async Task ApplyArtworkUrlAsync()
    {
        var scope = ArtworkScope;
        var slot = SelectedArtworkSlot;
        if (scope is null || slot is null || string.IsNullOrWhiteSpace(_artworkUrlInput))
            return;

        _artworkUrlSubmitting = true;
        _artworkUploadErrors.Remove(BuildScopedArtworkKey(scope.ScopeId, slot.AssetType));
        StateHasChanged();

        try
        {
            var ok = await ApiClient.UploadScopeArtworkFromUrlAsync(
                LaunchEntityId,
                scope.ScopeId,
                slot.AssetType,
                _artworkUrlInput.Trim());

            if (!ok)
            {
                var error = ApiClient.LastError ?? "Remote artwork download failed.";
                _artworkUploadErrors[BuildScopedArtworkKey(scope.ScopeId, slot.AssetType)] = error;
                Snackbar.Add(error, Severity.Error);
                return;
            }

            _artworkUrlInput = string.Empty;
            _artworkAddMenuAssetType = null;
            _showArtworkUrlInput = false;
            await RefreshArtworkStateAsync(scope.ScopeId);
            Snackbar.Add($"{slot.Label} updated.", Severity.Success);
        }
        finally
        {
            _artworkUrlSubmitting = false;
            StateHasChanged();
        }
    }

    protected void HandleArtworkUrlInput(ChangeEventArgs args) =>
        _artworkUrlInput = args.Value?.ToString() ?? string.Empty;

    protected void ToggleArtworkUrlInput()
    {
        _showArtworkUrlInput = !_showArtworkUrlInput;
        if (!_showArtworkUrlInput)
        {
            _artworkUrlInput = string.Empty;
            _artworkAddMenuAssetType = null;
        }
        else
        {
            _artworkAddMenuAssetType = _selectedArtworkAssetType;
        }
    }

    private async Task UploadPendingArtworkAsync(MediaEditorScopeDto scope, string assetType, IBrowserFile file, string scopedKey)
    {
        _artworkApplyingKeys.Add(scopedKey);
        _artworkUploadErrors.Remove(scopedKey);
        StateHasChanged();

        try
        {
            await using var stream = file.OpenReadStream(10 * 1024 * 1024);
            var uploaded = await ApiClient.UploadScopeArtworkVariantAsync(
                LaunchEntityId,
                scope.ScopeId,
                assetType,
                stream,
                file.Name);

            if (!uploaded)
            {
                var error = ApiClient.LastError ?? $"{assetType} upload failed.";
                _artworkUploadErrors[scopedKey] = error;
                Snackbar.Add(error, Severity.Error);
                return;
            }

            _pendingArtworkFiles.Remove(scopedKey);
            _pendingArtworkPreviewUrls.Remove(scopedKey);
            _artworkUploadErrors.Remove(scopedKey);
            await RefreshArtworkStateAsync(scope.ScopeId);
            Snackbar.Add($"{ResolveArtworkSlots(scope).FirstOrDefault(slot => string.Equals(slot.AssetType, assetType, StringComparison.OrdinalIgnoreCase))?.Label ?? assetType} updated.", Severity.Success);
        }
        finally
        {
            _artworkApplyingKeys.Remove(scopedKey);
            StateHasChanged();
        }
    }

    protected async Task HandleArtworkVariantClickAsync(
        ArtworkSlotDefinition slot,
        ArtworkVariantDisplayItem item)
    {
        FocusArtworkVariant(item.Key);

        if (item.IsPending || item.VariantId == Guid.Empty || item.IsPreferred)
        {
            OpenArtworkZoom(slot, item);
            return;
        }

        await SetPreferredArtworkVariantAsync(item.VariantId);
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

    protected void OpenArtworkZoom(ArtworkSlotDefinition slot, ArtworkVariantDisplayItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ImageUrl))
            return;

        _zoomArtworkSlot = slot;
        _zoomArtworkVariant = item;
    }

    protected void CloseArtworkZoom()
    {
        _zoomArtworkSlot = null;
        _zoomArtworkVariant = null;
    }

    protected string BuildArtworkVariantHoverLabel(ArtworkSlotDefinition slot, ArtworkVariantDisplayItem item)
    {
        var interaction = item.IsPending || item.IsPreferred
            ? "Click to preview."
            : "Click to make this the primary image.";

        return $"{BuildArtworkVariantSummary(slot, item)} {interaction}";
    }

    protected string BuildArtworkVariantSummary(ArtworkSlotDefinition slot, ArtworkVariantDisplayItem item)
    {
        var parts = new List<string> { slot.Label };

        if (item.IsPreferred)
            parts.Add("Primary");
        else if (item.IsPending)
            parts.Add("Pending");
        else if (!string.IsNullOrWhiteSpace(item.Origin))
            parts.Add(item.Origin);

        if (!string.IsNullOrWhiteSpace(item.ProviderName))
            parts.Add(item.ProviderName!);

        if (item.CreatedAt is DateTimeOffset createdAt)
            parts.Add(createdAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));

        return string.Join(" | ", parts);
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
        if (!IsSingleItem || IsFileScope)
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
        QuickSearchTargets.FirstOrDefault(target => string.Equals(target.Key, targetGroup, StringComparison.OrdinalIgnoreCase)).Label
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

        foreach (var edit in _editedValues
                     .Where(entry => string.Equals(ParseScopedKey(entry.Key).ScopeId, ActiveScope?.ScopeId, StringComparison.OrdinalIgnoreCase)))
        {
            merged[ParseScopedKey(edit.Key).Key] = edit.Value;
        }

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

    private IReadOnlyList<(string Id, string Label)> ResolveVisibleTabs()
    {
        if (IsBatchMode)
        {
            return TabDefinitions
                .Where(tab => tab.Id is "details" or "options")
                .ToList();
        }

        if (IsFileScope)
            return [("file", "File")];

        return TabDefinitions
            .Where(tab => IsTabVisible(tab.Id))
            .ToList();
    }

    private IReadOnlyList<(string Key, string Label)> ResolveQuickSearchTargets()
    {
        if (IsBatchMode)
            return _schema.QuickSearchTargets;

        return (_selectedMediaType, ActiveScope?.ScopeId) switch
        {
            ("TV", "series") => [("show", "Show")],
            ("TV", "season") => [],
            ("TV", "episode") => [("show_episode", "Episode"), ("show", "Show")],
            ("Music", "artist") => [("artist", "Artist")],
            ("Music", "album") => [("album", "Album"), ("artist", "Artist")],
            ("Music", "track") => [("track", "Track"), ("album", "Album"), ("artist", "Artist")],
            ("Movies", _) => [("movie_identity", "Movie")],
            ("Comics", "series") => [("series", "Series")],
            ("Comics", _) => [("issue", "Issue"), ("series", "Series")],
            ("Audiobooks", "series") => [("series", "Series"), ("narrator", "Narrator")],
            ("Audiobooks", _) => [("audiobook_identity", "Audiobook"), ("series", "Series"), ("narrator", "Narrator")],
            ("Books", "series") => [("series", "Series")],
            ("Books", _) => [("book_identity", "Book"), ("series", "Series")],
            _ => _schema.QuickSearchTargets,
        };
    }

    private IEnumerable<string> GetVisibleFieldKeysForScope()
    {
        if (IsFileScope || ActiveScope is null)
            return [];

        return (_selectedMediaType, ActiveScope.ScopeId) switch
        {
            ("TV", "series") => ["show_name", "year", "network", "runtime", "genre", "language", "description", "rating", "comment", "sort_series"],
            ("TV", "season") => ["season_number", "description", "comment"],
            ("TV", "episode") => ["season_number", "episode_number", "episode_title", "runtime", "release_date", "description", "language", "rating", "comment", "sort_title"],
            ("Music", "artist") => ["artist", "genre", "description", "language", "rating", "comment", "sort_artist"],
            ("Music", "album") => ["album", "album_artist", "artist", "genre", "year", "language", "description", "rating", "comment", "sort_album"],
            ("Music", "track") => ["title", "artist", "album", "composer", "track_number", "disc_number", "duration", "rating", "comment", "sort_title"],
            ("Movies", "movie") => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
            ("Books", "series") or ("Audiobooks", "series") or ("Comics", "series") => ["series", "series_position", "description", "genre", "comment", "sort_series"],
            ("Books", "work") => ["title", "subtitle", "author", "series", "series_position", "publisher", "year", "language", "description", "genre", "rating", "comment", "sort_title", "sort_series"],
            ("Audiobooks", "work") => ["title", "author", "narrator", "series", "series_position", "publisher", "year", "duration", "description", "genre", "language", "rating", "comment", "sort_title", "sort_series"],
            ("Comics", "volume_issue") => ["series", "volume", "series_position", "title", "author", "illustrator", "publisher", "year", "description", "genre", "comment", "sort_title", "sort_series"],
            ("Books", "volume_issue") => ["title", "subtitle", "author", "series", "series_position", "publisher", "year", "language", "description", "genre", "rating", "comment", "sort_title", "sort_series"],
            ("Audiobooks", "volume_issue") => ["title", "author", "narrator", "series", "series_position", "publisher", "year", "duration", "description", "genre", "language", "rating", "comment", "sort_title", "sort_series"],
            _ => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
        };
    }

    private string GetArtworkTabExplanation()
    {
        if (ArtworkScope is null)
            return "Artwork follows the scope selected above.";

        return (_selectedMediaType, ArtworkScope.ScopeId) switch
        {
            ("TV", "series") =>
                "Series scope manages poster, square art, background, banner, and logo for the show. Those images are shared across episodes.",
            ("TV", "season") =>
                "Season scope manages season poster and season thumb artwork for the selected season.",
            ("TV", "episode") =>
                "Episode scope only manages episode stills. Show and season artwork stay on their parent scopes.",
            ("Music", "artist") =>
                "Artist scope manages background, banner, and logo art for the artist owner.",
            ("Music", "album") =>
                "Album scope manages cover and square art for the album.",
            ("Music", "track") =>
                "Track scope normally inherits artwork from the artist and album. Track artwork is read-only here.",
            ("Movies", "movie") =>
                "Movie scope manages poster, square art, background, banner, and logo for the movie.",
            ("Books", "work") or ("Audiobooks", "work") or ("Comics", "work") or ("Books", "volume_issue") or ("Audiobooks", "volume_issue") or ("Comics", "volume_issue") =>
                "Work scope manages cover, square art, and background art for this title.",
            ("Books", "series") or ("Audiobooks", "series") or ("Comics", "series") =>
                "Series scope separates parent metadata from the individual volume or issue. Artwork stays on the work scope in this pass.",
            _ =>
                ArtworkScope.ScopeSummary ?? "Showing the artwork slots available for the selected owner.",
        };
    }

    protected string GetArtworkReadOnlyTitle() =>
        (_selectedMediaType, ArtworkScope?.ScopeId) switch
        {
            ("Music", "track") => "Track artwork is inherited",
            ("Books", "series") or ("Audiobooks", "series") or ("Comics", "series") => "Series artwork is managed on the title scope",
            _ => "This owner does not manage artwork",
        };

    protected string GetArtworkReadOnlyText() =>
        (_selectedMediaType, ArtworkScope?.ScopeId) switch
        {
            ("Music", "track") => "Tracks inherit their visuals from the Artist and Album owners. Switch owners to update the artwork people actually see.",
            ("Books", "series") or ("Audiobooks", "series") or ("Comics", "series") => "Series metadata is separated from the individual work or volume. Artwork still lives on the work-level owner in this pass.",
            _ => ArtworkScope?.ReadOnlyHint ?? "This owner does not expose editable artwork.",
        };

    protected IReadOnlyList<MediaEditorScopeDto> GetArtworkReadOnlySwitchTargets()
    {
        if (_editorContext is null || ArtworkScope is null)
            return [];

        return (_selectedMediaType, ArtworkScope.ScopeId) switch
        {
            ("Music", "track") => _editorContext.Scopes
                .Where(scope => scope.ScopeId is "artist" or "album")
                .OrderBy(scope => scope.Order)
                .ToList(),
            ("Books", "series") or ("Audiobooks", "series") or ("Comics", "series") => _editorContext.Scopes
                .Where(scope => scope.ScopeId is "work" or "volume_issue")
                .OrderBy(scope => scope.Order)
                .ToList(),
            _ => [],
        };
    }

    private bool IsTabVisible(string tabId)
    {
        if (IsBatchMode)
            return tabId is "details" or "options";

        if (IsFileScope)
            return string.Equals(tabId, "file", StringComparison.OrdinalIgnoreCase);

        return tabId switch
        {
            "details" => true,
            "universe" => SupportsUniverseTab(),
            "artwork" => SupportsArtworkTab(),
            "options" => GetGroupsForTab("options").Any(),
            _ => false,
        };
    }

    private bool SupportsUniverseTab()
    {
        if (ActiveScope is null)
            return false;

        return (_selectedMediaType, ActiveScope.ScopeId) switch
        {
            ("TV", "series") => true,
            ("Movies", "movie") => true,
            ("Books", "series") or ("Books", "work") or ("Books", "volume_issue") => true,
            ("Audiobooks", "series") or ("Audiobooks", "work") or ("Audiobooks", "volume_issue") => true,
            ("Comics", "series") or ("Comics", "work") or ("Comics", "volume_issue") => true,
            _ => false,
        };
    }

    private bool SupportsArtworkTab()
    {
        if (ActiveScope is null)
            return false;

        return ActiveScope.CanEditArtwork
               || ResolveArtworkSlots(ActiveScope).Count > 0
               || GetArtworkReadOnlySwitchTargets().Count > 0;
    }

    private string? GetCanonicalSearchUnavailableReason()
    {
        if (!IsSingleItem || IsFileScope || SupportsCanonicalSearch || ActiveScope is null)
            return null;

        return (_selectedMediaType, ActiveScope.ScopeId) switch
        {
            ("TV", "season") => "Season metadata is edited here, but matching happens on the Series or Episode scopes.",
            _ => null,
        };
    }

    private void EnsureActiveTabVisible()
    {
        if (IsTabVisible(_activeTab))
            return;

        if (string.Equals(_activeTab, "sorting", StringComparison.OrdinalIgnoreCase) && IsTabVisible("options"))
        {
            _activeTab = "options";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastNonFileTab) && IsTabVisible(_lastNonFileTab))
        {
            _activeTab = _lastNonFileTab;
            return;
        }

        _activeTab = Tabs.FirstOrDefault().Id ?? "details";
    }

    private static string GetArtworkEmptyStateLabel(string assetType) =>
        assetType switch
        {
            "SquareArt" => "No square art stored yet.",
            "Background" => "No background art stored yet.",
            "Banner" => "No banner art stored yet.",
            "Logo" => "No logo art stored yet.",
            "CoverArt" => "No cover art stored yet.",
            "SeasonPoster" => "No season poster stored yet.",
            "SeasonThumb" => "No season thumb stored yet.",
            "EpisodeStill" => "No episode still stored yet.",
            _ => "No artwork stored yet.",
        };

    private IReadOnlyList<ArtworkSlotDefinition> ResolveArtworkSlots(MediaEditorScopeDto? scope) =>
        (_selectedMediaType, scope?.ScopeId, scope?.CanEditArtwork) switch
        {
            ("TV", "series", true) or ("Movies", "movie", true) =>
            [
                PosterCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
            ],
            ("Music", "artist", true) =>
            [
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
            ],
            ("Music", "album", true) =>
            [
                AlbumArtArtworkSlot,
                SquareArtArtworkSlot,
            ],
            ("TV", "season", true) =>
            [
                SeasonPosterArtworkSlot,
                SeasonThumbArtworkSlot,
            ],
            ("TV", "episode", true) =>
            [
                EpisodeStillArtworkSlot,
            ],
            ("Books", "work", true) or ("Books", "volume_issue", true) or ("Audiobooks", "work", true) or ("Audiobooks", "volume_issue", true) or ("Comics", "work", true) or ("Comics", "volume_issue", true) =>
            [
                BookCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
            ],
            _ => [],
        };

    private async Task RefreshArtworkStateAsync(string? scopeId = null)
    {
        if (!IsSingleItem)
            return;

        var targetScopeId = scopeId ?? ArtworkScope?.ScopeId;
        if (string.IsNullOrWhiteSpace(targetScopeId))
            return;

        _artworkStates.Remove(targetScopeId);
        _scopeStates.Remove(targetScopeId);

        if (string.Equals(ActiveScope?.ScopeId, targetScopeId, StringComparison.OrdinalIgnoreCase))
            await LoadScopeStateAsync(forceReload: true);
        else
            await LoadArtworkStateAsync(targetScopeId, forceReload: true);

        CloseArtworkZoom();
        NormalizeArtworkSelection();
        StateHasChanged();
    }

    private void NormalizeArtworkSelection()
    {
        var availableSlots = ArtworkSlots;
        if (availableSlots.Count == 0)
        {
            _artworkAddMenuAssetType = null;
            _artworkUrlInput = string.Empty;
            _showArtworkUrlInput = false;
            _selectedArtworkAssetType = null;
            _focusedArtworkVariantKey = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedArtworkAssetType)
            || !availableSlots.Any(slot => string.Equals(slot.AssetType, _selectedArtworkAssetType, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedArtworkAssetType = availableSlots[0].AssetType;
        }

        if (!string.IsNullOrWhiteSpace(_artworkAddMenuAssetType)
            && !availableSlots.Any(slot => string.Equals(slot.AssetType, _artworkAddMenuAssetType, StringComparison.OrdinalIgnoreCase)))
        {
            _artworkAddMenuAssetType = null;
            _artworkUrlInput = string.Empty;
            _showArtworkUrlInput = false;
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

    private static string? FormatCountBadge(int count) => count > 0 ? count.ToString() : null;

    private string BuildScopedFieldKey(string key) =>
        IsBatchMode || ActiveScope is null
            ? key
            : $"{ActiveScope.ScopeId}|{key}";

    private string BuildScopedArtworkKey(string? scopeId, string assetType) =>
        IsBatchMode || string.IsNullOrWhiteSpace(scopeId)
            ? assetType
            : $"{scopeId}|{assetType}";

    private static (string ScopeId, string Key) ParseScopedKey(string compositeKey)
    {
        var splitIndex = compositeKey.IndexOf('|');
        return splitIndex > 0
            ? (compositeKey[..splitIndex], compositeKey[(splitIndex + 1)..])
            : (string.Empty, compositeKey);
    }

    private MediaEditorScopeDto? GetScopeById(string? scopeId) =>
        string.IsNullOrWhiteSpace(scopeId)
            ? null
            : _editorContext?.Scopes.FirstOrDefault(scope =>
                string.Equals(scope.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));

    private bool ActiveScopeExists(string scopeId) => GetScopeById(scopeId) is not null;

    private string BuildBreadcrumbText()
    {
        if (_editorContext is null || ActiveScope is null)
            return string.Empty;

        var breadcrumbParts = _editorContext.Scopes
            .Where(scope => !string.Equals(scope.ScopeId, "file", StringComparison.OrdinalIgnoreCase)
                            && scope.Order <= ActiveScope.Order
                            && !string.IsNullOrWhiteSpace(scope.BreadcrumbLabel))
            .OrderBy(scope => scope.Order)
            .Select(scope => scope.BreadcrumbLabel)
            .ToList();

        if (IsFileScope)
            breadcrumbParts.Add("File");

        return string.Join(" > ", breadcrumbParts);
    }

    private string? GetHeaderArtworkPreviewUrl()
    {
        foreach (var slot in ResolveArtworkSlots(ActiveScope))
        {
            var previewUrl = GetHeaderArtworkPreviewUrl(slot.AssetType);
            if (!string.IsNullOrWhiteSpace(previewUrl))
                return previewUrl;
        }

        return Request.CoverUrl;
    }

    private string? GetHeaderArtworkPreviewUrl(string assetType)
    {
        if (string.Equals(assetType, "Hero", StringComparison.OrdinalIgnoreCase))
            return _detail?.HeroUrl;

        return _artwork?.Slots.FirstOrDefault(slot =>
            string.Equals(slot.AssetType, assetType, StringComparison.OrdinalIgnoreCase))?.Variants
            .OrderByDescending(variant => variant.IsPreferred)
            .ThenByDescending(variant => variant.CreatedAt)
            .FirstOrDefault()?.ImageUrl;
    }

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
