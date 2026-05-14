using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Components.MediaEditor;

public partial class SharedMediaEditorShell
{
    private static readonly string[] TabDisplayOrder =
    [
        "details",
        "episodes",
        "tracks",
        "artwork",
        "links",
        "options",
        "file",
        "history",
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

    private static readonly ArtworkSlotDefinition DiscArtArtworkSlot =
        new("DiscArt", "Disc Art", "Transparent disc or label art for movies and music releases.", Icons.Material.Outlined.Album, "square", "fit", true, "Best for CD, vinyl, or disc-face artwork.", "Disc");

    private static readonly ArtworkSlotDefinition ClearArtArtworkSlot =
        new("ClearArt", "Clear Art", "Transparent key art designed to sit over a background image.", Icons.Material.Outlined.FilterNone, "logo", "fit", true, "Best for transparent character or title overlay art.", "Clear");

    private static readonly ArtworkSlotDefinition SeasonPosterArtworkSlot =
        new("SeasonPoster", "Season Poster", "Poster art stored for the season container.", Icons.Material.Outlined.ViewAgenda, "portrait", "fit", true, "Best for season-specific poster art.", "Season");

    private static readonly ArtworkSlotDefinition SeasonThumbArtworkSlot =
        new("SeasonThumb", "Season Thumb", "A wide season still or season thumbnail.", Icons.Material.Outlined.PhotoSizeSelectLarge, "background", "fit", true, "Best for season-specific thumbnail art.", "Season");

    private static readonly ArtworkSlotDefinition EpisodeStillArtworkSlot =
        new("EpisodeStill", "Episode Still", "An episode-specific still image.", Icons.Material.Outlined.LiveTv, "background", "fit", true, "Best for episode stills or screenshots.", "Still");

    [Inject] protected IEngineApiClient ApiClient { get; set; } = null!;
    [Inject] protected UIOrchestratorService Orchestrator { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;
    [Inject] protected IJSRuntime JS { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter] public MediaEditorLaunchRequest Request { get; set; } = new();

    private LibraryItemDetailViewModel? _detail;
    private List<CanonicalFieldViewModel> _canonicalValues = [];
    private List<ClaimHistoryDto> _claims = [];
    private List<LibraryItemHistoryDto> _history = [];
    private ArtworkEditorDto? _artwork;
    private MediaEditorContextDto? _editorContext;
    private MediaEditorNavigatorDto? _navigator;
    private MediaEditorSchema _schema = MediaEditorSchemaCatalog.Resolve(null);
    private readonly Dictionary<string, string> _editedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inlineOverrideKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _clearedInlineOverrideKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingInlineRevertKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _selectedSuggestedFieldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid?> _selectedMembershipTargetIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaEditorMembershipSuggestionDto> _selectedMembershipSuggestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MediaEditorMembershipSuggestionDto>> _membershipSuggestions = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _quarantining;
    private bool _confirmDiscard;
    private bool _showQuarantineConfirm;
    private string? _dragTargetArtworkType;
    private string? _loadError;
    private string _reviewSummary = "Review the item identity.";
    private string _primaryActionLabel = "Review Metadata";
    private MediaEditorIdentityIntent _identityIntent = MediaEditorIdentityIntent.None;
    private string _lastNonFileTab = "details";
    private bool _showArtworkUrlInput;
    private MediaEditorMembershipPreviewDto? _pendingMembershipPreview;
    private string? _fieldToFocus;

    protected IReadOnlyList<(string Id, string Label, string Icon)> Tabs => ResolveVisibleTabs();
    protected IReadOnlyList<(string Key, string Label)> QuickSearchTargets => ResolveQuickSearchTargets();
    protected IReadOnlyList<ArtworkSlotDefinition> ArtworkSlots => ResolveArtworkSlots(ArtworkScope);
    protected bool SupportsCanonicalSearch => QuickSearchTargets.Count > 0;
    protected string? CanonicalSearchUnavailableReason => GetCanonicalSearchUnavailableReason();
    protected IReadOnlyList<MediaEditorScopeDto> ScopeOptions =>
        (_editorContext?.Scopes ?? [])
            .OrderBy(scope => scope.Order)
            .ToList();
    protected int ActiveTabIndex => GetSelectedIndex(Tabs.Select(tab => tab.Id), _activeTab);
    protected int CanonicalTargetIndex => GetSelectedIndex(QuickSearchTargets.Select(target => target.Key), _canonicalTargetGroup);
    protected bool IsSingleItem => Request.EntityIds.Count == 1;
    protected bool IsBatchMode => Request.Mode == SharedMediaEditorMode.Batch || Request.EntityIds.Count > 1;
    protected Guid LaunchEntityId => Request.LaunchEntityId ?? Request.EntityIds[0];
    protected MediaEditorIdentityIntent EffectiveIdentityIntent =>
        _identityIntent == MediaEditorIdentityIntent.None ? Request.IdentityIntent : _identityIntent;
    protected Guid EditorContextEntityId => _editorContext?.LaunchEntityId ?? LaunchEntityId;
    protected Guid CurrentEntityId => ActiveScope?.FieldEntityId ?? EditorContextEntityId;
    protected bool IsDirty => _editedValues.Count > 0 || _pendingArtworkFiles.Count > 0;
    protected bool IsArtworkBusy => _artworkUrlSubmitting || _artworkApplyingKeys.Count > 0;
    protected string ArtworkTabExplanation => GetArtworkTabExplanation();
    protected bool HasSeriesNavigator => false;
    protected bool ShowLegacyScopeSwitcher => false;
    protected string ScopePickerLabel => ActiveScope?.Label ?? "Scope";
    protected string? ScopePickerTitle => GetScopePickerTitle(ActiveScope);
    protected ArtworkSlotDefinition? SelectedArtworkSlot =>
        ArtworkSlots.FirstOrDefault(slot => string.Equals(slot.AssetType, _selectedArtworkAssetType, StringComparison.OrdinalIgnoreCase))
        ?? ArtworkSlots.FirstOrDefault();
    protected ArtworkVariantDisplayItem? LeadArtworkVariant =>
        SelectedArtworkSlot is null ? null : GetLeadArtworkVariant(SelectedArtworkSlot.AssetType);
    protected ArtworkSlotDefinition? ZoomArtworkSlot => _zoomArtworkSlot;
    protected ArtworkVariantDisplayItem? ZoomArtworkVariant => _zoomArtworkVariant;
    protected bool IsArtworkZoomOpen => _zoomArtworkSlot is not null && _zoomArtworkVariant is not null;
    protected MediaEditorNavigatorNodeDto? NavigatorRootNode =>
        _navigator?.Nodes.FirstOrDefault(node => node.IsRoot)
        ?? _navigator?.Nodes.FirstOrDefault(node => node.ParentNodeId is null);
    protected MediaEditorNavigatorNodeDto? SelectedNavigatorNode =>
        _navigator?.Nodes.FirstOrDefault(node => node.EntityId == EditorContextEntityId)
        ?? NavigatorRootNode;
    protected bool SupportsDeleteAction => GetQuarantineTargetEntityIds().Count > 0;
    protected bool IsContainerEditor => string.Equals(_editorContext?.EditorMode, "container", StringComparison.OrdinalIgnoreCase);
    protected bool IsSingularEditor => !IsBatchMode && !IsContainerEditor;
    protected MediaEditorScopeDto? ContainerRootScope =>
        IsContainerEditor
            ? _editorContext?.Scopes
                .Where(scope => !string.Equals(scope.ScopeId, "file", StringComparison.OrdinalIgnoreCase))
                .OrderBy(scope => scope.Order)
                .FirstOrDefault()
            : null;
    protected string ContentTabId =>
        GetAvailableTabIds().FirstOrDefault(tabId =>
            string.Equals(tabId, "episodes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tabId, "tracks", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    protected string ContentTabLabel => GetTabLabel(ContentTabId);
    protected IReadOnlyList<MediaEditorNavigatorNodeDto> ContentRootChildren =>
        NavigatorRootNode is null ? [] : GetNavigatorChildren(NavigatorRootNode.NodeId);
    protected string CurrentTargetLabel => _editorContext?.CurrentTargetSummary?.Label ?? ActiveScope?.Label ?? "Item";
    protected string CurrentTargetTitle => _editorContext?.CurrentTargetSummary?.Title ?? ActiveScope?.DisplayTitle ?? HeaderTitle;
    protected string? CurrentTargetSubtitle => _editorContext?.CurrentTargetSummary?.Subtitle ?? ActiveScope?.DisplaySubtitle;

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
            _ when IsContainerEditor => ContainerRootScope?.Label ?? ActiveScope?.Label ?? _schema.MediaType,
            _ => ActiveScope?.Label ?? _schema.MediaType,
        };

    protected string HeaderTitle =>
        (IsContainerEditor ? ContainerRootScope?.DisplayTitle : ActiveScope?.DisplayTitle)
        ?? Request.HeaderTitle
        ?? _detail?.Title
        ?? (IsBatchMode ? $"Edit {Request.EntityIds.Count} Items" : "Edit Item");

    protected string? HeaderSubtitle =>
        (IsContainerEditor ? ContainerRootScope?.DisplaySubtitle : ActiveScope?.DisplaySubtitle)
        ?? Request.HeaderSubtitle
        ?? (IsSingleItem ? BuildHeaderSubtitle() : string.Join(" | ", Request.PreviewItems.Take(3).Select(x => x.Title)));

    protected string? CurrentCoverUrl => GetHeaderArtworkPreviewUrl();

    private sealed class ScopeEditorState
    {
        public LibraryItemDetailViewModel? Detail { get; init; }
        public List<CanonicalFieldViewModel> CanonicalValues { get; init; } = [];
        public List<ClaimHistoryDto> Claims { get; init; } = [];
        public List<LibraryItemHistoryDto> History { get; init; } = [];
        public ArtworkEditorDto Artwork { get; init; } = new();
    }

    protected override async Task OnInitializedAsync()
    {
        _activeTab = NormalizeTabId(string.IsNullOrWhiteSpace(Request.InitialTab) ? "details" : Request.InitialTab);
        _lastNonFileTab = _activeTab == "file" ? "details" : _activeTab;
        _schema = MediaEditorSchemaCatalog.Resolve(Request.MediaType);

        if (IsSingleItem)
            await LoadSingleItemAsync(resetEditorState: true);
        else
            await LoadBatchAsync();
    }

    private async Task LoadSingleItemAsync(Guid? targetEntityId = null, bool resetEditorState = false)
    {
        _loading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            var entityId = targetEntityId ?? LaunchEntityId;

            if (resetEditorState)
            {
                _editedValues.Clear();
                _selectedMembershipTargetIds.Clear();
                _membershipSuggestions.Clear();
                _pendingArtworkFiles.Clear();
                _pendingArtworkPreviewUrls.Clear();
                _artworkUploadErrors.Clear();
                _scopeStates.Clear();
                _artworkStates.Clear();
            }

            _editorContext = await ApiClient.GetMediaEditorContextAsync(entityId);
            _navigator = await ApiClient.GetMediaEditorNavigatorAsync(entityId);

            if (_editorContext is null)
            {
                var detailTask = ApiClient.GetLibraryItemDetailAsync(entityId);
                var canonicalTask = Orchestrator.GetCanonicalValuesAsync(entityId);
                var claimsTask = Orchestrator.GetClaimHistoryAsync(entityId);
                var historyTask = ApiClient.GetItemHistoryAsync(entityId);
                var artworkTask = ApiClient.GetArtworkAsync(entityId);

                await Task.WhenAll(detailTask, canonicalTask, claimsTask, historyTask, artworkTask);

                _detail = detailTask.Result;
                _canonicalValues = canonicalTask.Result;
                _claims = claimsTask.Result;
                _history = historyTask.Result;
                _artwork = artworkTask.Result ?? new ArtworkEditorDto { EntityId = entityId };
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

            _dragTargetArtworkType = null;
            _showQuarantineConfirm = false;
            _pendingMembershipPreview = null;
            NormalizeArtworkSelection();

            if (Request.Mode == SharedMediaEditorMode.Review)
            {
                var target = ReviewTargetResolver.Resolve(_detail?.MediaType ?? Request.MediaType, Request.ReviewTrigger ?? _detail?.ReviewTrigger);
                _activeTab = NormalizeTabId(string.IsNullOrWhiteSpace(Request.InitialTab) ? target.InitialTab : Request.InitialTab!);
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup)
                    ? (ActiveScope?.CanonicalTargetGroup ?? target.CanonicalTargetGroup)
                    : Request.InitialCanonicalTargetGroup!;
                _reviewSummary = target.Summary;
                _identityIntent = Request.IdentityIntent == MediaEditorIdentityIntent.None ? target.Intent : Request.IdentityIntent;
                _primaryActionLabel = target.PrimaryActionLabel;
            }
            else
            {
                _identityIntent = Request.IdentityIntent;
                _primaryActionLabel = ResolveFooterPrimaryActionLabel(_identityIntent);
                _canonicalTargetGroup = string.IsNullOrWhiteSpace(Request.InitialCanonicalTargetGroup)
                    ? (ActiveScope?.CanonicalTargetGroup ?? _schema.DefaultTargetGroup)
                    : Request.InitialCanonicalTargetGroup!;
            }

            if (IsFileScope)
                _activeTab = "file";

            EnsureActiveTabVisible();
                _canonicalSearchQuery = BuildSuggestedSearchQuery();
        }
        catch
        {
            _loadError = "This item could not be loaded for editing.";
            Snackbar.Add(_loadError, Severity.Error);
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

        var stateKey = BuildScopeStateKey(ActiveScope.FieldEntityId, ActiveScope.ScopeId);
        if (!forceReload && _scopeStates.TryGetValue(stateKey, out var cachedState))
        {
            ApplyScopeState(cachedState);
            return;
        }

        var detailTask = ApiClient.GetLibraryItemDetailAsync(ActiveScope.FieldEntityId);
        var canonicalTask = Orchestrator.GetCanonicalValuesAsync(ActiveScope.FieldEntityId);
        var claimsTask = Orchestrator.GetClaimHistoryAsync(ActiveScope.FieldEntityId);
        var historyTask = ApiClient.GetItemHistoryAsync(ActiveScope.FieldEntityId);
        var activeScopeSlots = ResolveArtworkSlots(ActiveScope);
        var artworkTask = ActiveScope.CanEditArtwork || activeScopeSlots.Count > 0
            ? ApiClient.GetScopeArtworkAsync(EditorContextEntityId, ActiveScope.ScopeId)
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

        _scopeStates[stateKey] = state;
        _artworkStates[stateKey] = state.Artwork;
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
            _artworkStates[BuildScopeStateKey(ActiveScope.FieldEntityId, ActiveScope.ScopeId)] = state.Artwork;
        _selectedMediaType = _detail?.MediaType ?? _editorContext?.MediaType ?? Request.MediaType ?? "Books";
        _schema = MediaEditorSchemaCatalog.Resolve(_selectedMediaType);
        _canonicalTargetGroup = ActiveScope?.CanonicalTargetGroup ?? _schema.DefaultTargetGroup;
        _canonicalSearchQuery = BuildSuggestedSearchQuery();
        _canonicalSearchResponse = null;
        _selectedCandidateId = null;
        _selectedSuggestedFieldKeys.Clear();
        _showQuarantineConfirm = false;
        _pendingMembershipPreview = null;
        CloseArtworkZoom();
        EnsureActiveTabVisible();
        NormalizeArtworkSelection();
    }

    protected async Task SelectScopeAsync(string scopeId)
    {
        if (string.Equals(_activeScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
            return;

        var wasFileScope = IsFileScope;
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
        else if (wasFileScope && string.Equals(_activeTab, "file", StringComparison.OrdinalIgnoreCase))
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

    protected async Task SelectNavigatorNodeAsync(Guid entityId)
    {
        if (entityId == Guid.Empty || entityId == EditorContextEntityId)
            return;

        _loading = true;
        StateHasChanged();

        try
        {
            await LoadSingleItemAsync(entityId, resetEditorState: false);
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

        var scope = GetScopeById(scopeId);
        if (scope is null)
            return;

        var stateKey = BuildScopeStateKey(scope.FieldEntityId, scope.ScopeId);
        if (!forceReload && _artworkStates.ContainsKey(stateKey))
            return;

        var slots = ResolveArtworkSlots(scope);
        if (!scope.CanEditArtwork && slots.Count == 0)
        {
            _artworkStates[stateKey] = new ArtworkEditorDto { EntityId = scope.ArtworkOwnerEntityId ?? scope.FieldEntityId };
            return;
        }

        var artwork = await ApiClient.GetScopeArtworkAsync(EditorContextEntityId, scope.ScopeId)
                      ?? new ArtworkEditorDto { EntityId = scope.ArtworkOwnerEntityId ?? scope.FieldEntityId };
        _artworkStates[stateKey] = artwork;
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
            var sourceTabIds = tabId switch
            {
                "details" => new[] { "details" },
                "options" => new[] { "options", "sorting" },
                _ => new[] { tabId },
            };

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

    protected static int GetFieldLineCount(MediaEditorFieldDefinition field)
    {
        if (!string.Equals(field.InputKind, "textarea", StringComparison.OrdinalIgnoreCase))
            return 1;

        return string.Equals(field.Key, "description", StringComparison.OrdinalIgnoreCase) ? 7 : 4;
    }

    protected string GetEditableValue(string key)
    {
        var scopedKey = BuildScopedFieldKey(key);

        if (_editedValues.TryGetValue(scopedKey, out var edited) && !_clearedInlineOverrideKeys.Contains(scopedKey))
            return edited;

        if (IsBatchMode)
            return string.Empty;

        if (!_clearedInlineOverrideKeys.Contains(scopedKey)
            && _editorContext?.DisplayOverrides.TryGetValue(key, out var overrideValue) == true)
        {
            return overrideValue;
        }

        var values = MediaEditorSchemaCatalog.BuildValueMap(_detail, _canonicalValues);
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    protected void OnFieldInput(string key, string? value)
    {
        var normalized = IsMultilineField(key)
            ? NormalizeMultilineInput(key, value)
            : (value ?? string.Empty).Trim();
        var baseline = IsBatchMode ? string.Empty : GetBaselineValue(key);
        var scopedKey = BuildScopedFieldKey(key);
        _clearedInlineOverrideKeys.Remove(scopedKey);

        if (string.Equals(normalized, baseline, StringComparison.Ordinal))
        {
            _editedValues.Remove(scopedKey);
        }
        else if (string.IsNullOrWhiteSpace(normalized))
        {
            if (ShouldSaveAsDisplayOverride(scopedKey, key) && !string.IsNullOrWhiteSpace(baseline))
                _editedValues[scopedKey] = string.Empty;
            else
                _editedValues.Remove(scopedKey);
        }
        else
        {
            _editedValues[scopedKey] = normalized;
        }
    }

    protected Task SaveAsync() => SaveAsyncCore(applyMembershipMove: false);

    protected Task ConfirmMembershipMoveAsync() => SaveAsyncCore(applyMembershipMove: true);

    private async Task SaveAsyncCore(bool applyMembershipMove)
    {
        if (!IsDirty && (!applyMembershipMove || _pendingMembershipPreview is null))
        {
            if (Request.Mode == SharedMediaEditorMode.Review)
            {
                await ResolveReviewWithoutChangesAsync();
                return;
            }

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

            if (!applyMembershipMove)
            {
                var membershipPreview = await PreviewMembershipChangeAsync();
                if (membershipPreview is not null
                    && !string.Equals(membershipPreview.Action, "none", StringComparison.OrdinalIgnoreCase))
                {
                    if (!membershipPreview.CanApply)
                    {
                        Snackbar.Add(membershipPreview.ConflictMessage ?? membershipPreview.Message, Severity.Error);
                        return;
                    }

                    _pendingMembershipPreview = membershipPreview;
                    return;
                }
            }

            var savedAnything = false;

            if (applyMembershipMove && _pendingMembershipPreview is not null)
            {
                var membershipResult = await ApiClient.ApplyMediaEditorMembershipAsync(CurrentEntityId, BuildMembershipPreviewRequest());
                if (membershipResult is null || !membershipResult.CanApply)
                {
                    Snackbar.Add(membershipResult?.ConflictMessage ?? membershipResult?.Message ?? "Membership update failed.", Severity.Error);
                    return;
                }

                savedAnything = true;
            }

            var fieldChangesByScope = _editedValues
                .Select(entry => (RawKey: entry.Key, Key: ParseScopedKey(entry.Key), Value: entry.Value))
                .Where(entry => entry.Key.EntityId != Guid.Empty)
                .GroupBy(entry => new ScopedEditorKey(entry.Key.EntityId, entry.Key.ScopeId), ScopedEditorKeyComparer.Instance)
                .ToList();

            foreach (var scopeGroup in fieldChangesByScope)
            {
                var overrideFields = scopeGroup
                    .Where(entry => ShouldSaveAsDisplayOverride(entry.RawKey, entry.Key.Key))
                    .ToDictionary(entry => entry.Key.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
                var preferenceFields = scopeGroup
                    .Where(entry => !ShouldSaveAsDisplayOverride(entry.RawKey, entry.Key.Key))
                    .ToDictionary(entry => entry.Key.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

                if (preferenceFields.Count > 0)
                {
                    var saved = await ApiClient.SaveItemPreferencesAsync(scopeGroup.Key.EntityId, preferenceFields);
                    if (!saved)
                    {
                        Snackbar.Add($"Preference save failed for {scopeGroup.Key.ScopeId}.", Severity.Error);
                        return;
                    }

                    savedAnything = true;
                }

                if (overrideFields.Count > 0)
                {
                    var savedOverrides = await ApiClient.SaveItemDisplayOverridesAsync(scopeGroup.Key.EntityId, overrideFields);
                    if (!savedOverrides)
                    {
                        Snackbar.Add($"Display override save failed for {scopeGroup.Key.ScopeId}.", Severity.Error);
                        return;
                    }

                    savedAnything = true;
                }
            }

            foreach (var pendingArtwork in _pendingArtworkFiles.ToList())
            {
                var parsedKey = ParseScopedKey(pendingArtwork.Key);
                if (parsedKey.EntityId == Guid.Empty || string.IsNullOrWhiteSpace(parsedKey.ScopeId))
                    continue;

                await using var stream = pendingArtwork.Value.OpenReadStream(10 * 1024 * 1024);
                var uploaded = await ApiClient.UploadScopeArtworkVariantAsync(
                    parsedKey.EntityId,
                    parsedKey.ScopeId,
                    parsedKey.Key,
                    stream,
                    pendingArtwork.Value.Name);

                if (!uploaded)
                {
                    Snackbar.Add($"{parsedKey.Key} upload failed.", Severity.Error);
                    return;
                }

                savedAnything = true;
            }

            if (!savedAnything)
            {
                if (Request.Mode == SharedMediaEditorMode.Review)
                {
                    await ResolveReviewWithoutChangesAsync();
                    return;
                }

                MudDialog.Cancel();
                return;
            }

            if (Request.Mode == SharedMediaEditorMode.Review)
            {
                if (!await ResolveReviewCoreAsync())
                    return;

                Snackbar.Add(applyMembershipMove && _pendingMembershipPreview is not null
                    ? "Changes saved, membership updated, and review resolved."
                    : "Changes saved and review resolved.", Severity.Success);
                MudDialog.Close(DialogResult.Ok(true));
                return;
            }

            Snackbar.Add(applyMembershipMove && _pendingMembershipPreview is not null
                ? "Changes saved and membership updated."
                : "Changes saved.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        finally
        {
            _saving = false;
        }
    }

    protected async Task ResolveReviewWithoutChangesAsync()
    {
        if (Request.Mode != SharedMediaEditorMode.Review)
            return;

        _saving = true;
        StateHasChanged();

        try
        {
            if (!await ResolveReviewCoreAsync())
                return;

            Snackbar.Add("Review resolved.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        finally
        {
            _saving = false;
            StateHasChanged();
        }
    }

    private async Task<bool> ResolveReviewCoreAsync(string? providerName = null, string? providerItemId = null, string? selectedQid = null)
    {
        if (Request.ReviewItemId is not { } reviewItemId)
        {
            Snackbar.Add("Review was not resolved because this editor was not opened from a review item.", Severity.Error);
            return false;
        }

        var overrides = BuildReviewFieldOverrides();
        var resolved = await Orchestrator.ResolveReviewAsync(
            reviewItemId,
            new ReviewResolveRequestDto
            {
                SelectedQid = selectedQid,
                ProviderName = providerName,
                ProviderItemId = providerItemId,
                FieldOverrides = overrides.Count == 0 ? null : overrides,
            });

        if (!resolved)
        {
            Snackbar.Add("Review was not resolved because changes could not be saved.", Severity.Error);
            return false;
        }

        _editedValues.Clear();
        _pendingArtworkFiles.Clear();
        _pendingArtworkPreviewUrls.Clear();
        _pendingMembershipPreview = null;
        return true;
    }

    private List<FieldOverrideDto> BuildReviewFieldOverrides() =>
        _editedValues
            .Select(entry => (Key: ParseScopedKey(entry.Key).Key, entry.Value))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(entry => new FieldOverrideDto { Key = entry.Key, Value = entry.Value })
            .ToList();

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

    protected async Task ConfirmNavigationWithUnsavedChanges(LocationChangingContext context)
    {
        if (IsDirty && !await JS.InvokeAsync<bool>("confirm", "You have unsaved changes. Leave without saving?"))
            context.PreventNavigation();
    }

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
            _navigator = null;
            await LoadSingleItemAsync(resetEditorState: true);
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
        if (_pendingArtworkPreviewUrls.TryGetValue(BuildScopedArtworkKey(ArtworkScope?.ScopeId, assetType), out var pendingPreview))
            return pendingPreview;

        return GetPreferredArtworkVariant(assetType)?.ImageUrl;
    }

    protected int GetArtworkAssetCount(string assetType) => GetArtworkVariants(assetType).Count;

    protected string GetArtworkSourceLabel(string assetType)
    {
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

    protected string GetArtworkGalleryTitle(ArtworkSlotDefinition slot) =>
        slot.AssetType switch
        {
            "Background" => "Banner / Backdrop Gallery",
            "Banner" => "Banner Gallery",
            "Logo" => "Logo Gallery",
            "SquareArt" => "Square Art Gallery",
            "DiscArt" => "Disc Art Gallery",
            "ClearArt" => "Clear Art Gallery",
            _ => $"{slot.Label} Gallery",
        };

    protected string GetArtworkGallerySubtitle(ArtworkSlotDefinition slot) =>
        $"Select an image to make it the active {slot.Label.ToLowerInvariant()} for this item. Your choice is applied immediately.";

    protected string GetArtworkCardSource(ArtworkVariantDisplayItem item) =>
        string.IsNullOrWhiteSpace(item.ProviderName)
            ? item.Origin
            : item.ProviderName!;

    protected string GetArtworkCardDimensions(ArtworkSlotDefinition slot) =>
        slot.PreviewClass switch
        {
            "portrait" => "1200 x 1800",
            "square" => "1200 x 1200",
            "background" => "1920 x 1080",
            "banner" => "1920 x 356",
            "logo" => "1200 x 450",
            _ => "High resolution",
        };

    protected IReadOnlyList<(string Label, string Class)> GetArtworkCardBadges(ArtworkVariantDisplayItem item)
    {
        var badges = new List<(string Label, string Class)>();

        if (item.IsPreferred)
            badges.Add(("Active", "sme-artwork-badge--active"));
        if (item.IsPending)
            badges.Add(("Pending", "sme-artwork-badge--pending"));
        if (string.Equals(item.Origin, "Uploaded", StringComparison.OrdinalIgnoreCase))
            badges.Add(("Uploaded", "sme-artwork-badge--uploaded"));
        if (string.Equals(item.Origin, "Local Embedded", StringComparison.OrdinalIgnoreCase))
            badges.Add(("Embedded", "sme-artwork-badge--info"));
        return badges;
    }

    protected string GetArtworkActionLabel(string assetType) => "Add";

    protected string GetArtworkAcceptedTypes(string assetType) =>
        string.Equals(assetType, "Logo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, "DiscArt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, "ClearArt", StringComparison.OrdinalIgnoreCase)
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

        return _artworkStates.TryGetValue(BuildScopeStateKey(ArtworkScope.FieldEntityId, ArtworkScope.ScopeId), out var artwork)
            ? artwork
            : _artwork;
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

    protected Task OnTabChanged(int index)
    {
        if (index < 0 || index >= Tabs.Count)
            return Task.CompletedTask;

        return SelectTabInternalAsync(Tabs[index].Id);
    }

    protected Task OnCanonicalTargetChanged(int index)
    {
        if (index < 0 || index >= QuickSearchTargets.Count)
            return Task.CompletedTask;

        SetCanonicalTargetGroup(QuickSearchTargets[index].Key);
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

        await RefreshArtworkStateAsync(notifyParent: true);
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
                EditorContextEntityId,
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
            await RefreshArtworkStateAsync(scope.ScopeId, notifyParent: true);
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
                EditorContextEntityId,
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
            await RefreshArtworkStateAsync(scope.ScopeId, notifyParent: true);
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

        await RefreshArtworkStateAsync(notifyParent: true);
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
                    SearchMode = GetCanonicalSearchMode(),
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
        if (IsWikidataIntent)
            return $"Search Wikidata directly for the correct {label.ToLowerInvariant()} identity.";

        return Request.Mode == SharedMediaEditorMode.Review
            ? $"Resolve the blocking {label.ToLowerInvariant()} identity."
            : $"Find the canonical {label.ToLowerInvariant()} and apply only the fields you want.";
    }

    private static string NormalizeMultilineInput(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();

        return string.Equals(key, "description", StringComparison.OrdinalIgnoreCase)
            ? NormalizeDescriptionParagraphs(normalized)
            : normalized;
    }

    private static string NormalizeDescriptionParagraphs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join("\n\n",
            value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)));
    }

    private bool IsWikidataIntent =>
        EffectiveIdentityIntent is MediaEditorIdentityIntent.FixWikidataMatch
            or MediaEditorIdentityIntent.ConfirmWikidataMatch
            or MediaEditorIdentityIntent.MarkWikidataMissing;

    private string GetCanonicalSearchMode() =>
        IsWikidataIntent ? "wikidata_only" : "retail_only";

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

        var acceptedSuggested = GetAcceptedSuggestedKeys(GetCandidateId(candidate));
        var selectedSuggested = acceptedSuggested
            .Where(key => candidate.SuggestedFields.ContainsKey(key))
            .ToDictionary(key => key, key => candidate.SuggestedFields[key], StringComparer.OrdinalIgnoreCase);

        var response = await ApiClient.ReplaceRetailMatchAsync(
            CurrentEntityId,
            new ReplaceRetailMatchRequestDto
            {
                TargetFieldGroup = _canonicalTargetGroup,
                ProviderName = candidate.ProviderName,
                ProviderItemId = candidate.ProviderItemId ?? string.Empty,
                RequiredFields = candidate.RequiredFields,
                SuggestedFields = selectedSuggested,
                BridgeIds = candidate.BridgeIds,
                ReviewItemId = Request.ReviewItemId,
            });

        await FinishMatchActionAsync(response, "Retail match applied; Wikidata alignment queued.");
    }

    protected async Task ApplyLinkedCandidateAsync(UniverseCandidateDto candidate)
    {
        if (!candidate.IsApplicable)
            return;

        var qid = candidate.QidFields.TryGetValue("wikidata_qid", out var qidField)
            ? qidField
            : candidate.Qid;

        var response = await ApiClient.ReplaceWikidataMatchAsync(
            CurrentEntityId,
            new ReplaceWikidataMatchRequestDto
            {
                Action = "replace",
                Qid = qid,
                ReviewItemId = Request.ReviewItemId,
            });

        await FinishMatchActionAsync(response, "Wikidata identity replaced; canonical fields refreshed.");
    }

    protected async Task MarkWikidataMissingAsync()
    {
        var response = await ApiClient.ReplaceWikidataMatchAsync(
            CurrentEntityId,
            new ReplaceWikidataMatchRequestDto
            {
                Action = "mark_missing",
                ReviewItemId = Request.ReviewItemId,
            });

        await FinishMatchActionAsync(response, "Retail match kept; Wikidata marked missing.");
    }

    protected async Task ClearWikidataMatchAsync()
    {
        var response = await ApiClient.ReplaceWikidataMatchAsync(
            CurrentEntityId,
            new ReplaceWikidataMatchRequestDto
            {
                Action = "clear",
                ReviewItemId = Request.ReviewItemId,
            });

        await FinishMatchActionAsync(response, "Wikidata identity cleared; retail match kept.");
    }

    private Task FinishMatchActionAsync(ItemCanonicalApplyResponseDto? response, string fallbackMessage)
    {
        if (response is null)
        {
            Snackbar.Add("Match update failed.", Severity.Error);
            return Task.CompletedTask;
        }

        Snackbar.Add(string.IsNullOrWhiteSpace(response.Message) ? fallbackMessage : response.Message, Severity.Success);
        MudDialog.Close(DialogResult.Ok(true));
        return Task.CompletedTask;
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

        if (Request.Mode == SharedMediaEditorMode.Review)
        {
            if (!await ResolveReviewCoreAsync(providerName, providerItemId, qidFields.Values.FirstOrDefault()))
                return;

            Snackbar.Add($"{response.Message} Review resolved.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
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
                     .Where(entry =>
                     {
                         var parsed = ParseScopedKey(entry.Key);
                         return parsed.EntityId == CurrentEntityId
                                && string.Equals(parsed.ScopeId, ActiveScope?.ScopeId, StringComparison.OrdinalIgnoreCase);
                     }))
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

        if (_editorContext?.DisplayOverrides.TryGetValue(key, out var overrideValue) == true)
        {
            return overrideValue;
        }

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

    private static string FormatProviderName(string? providerName, string? mediaType = null)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return "Provider";

        return providerName switch
        {
            "fanart_tv" => "Fanart.tv",
            "tmdb" => "TMDB",
            "imdb" => "IMDb",
            "comicvine" => "Comic Vine",
            "comic_vine" => "Comic Vine",
            "musicbrainz" => "MusicBrainz",
            "apple_api" => ReviewTargetResolver.NormalizeMediaType(mediaType) == "Music" ? "Apple Music" : "Apple Books",
            "apple_music" => "Apple Music",
            "apple_books" => "Apple Books",
            "google_books" => "Google Books",
            "wikidata_reconciliation" => "Wikidata",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(providerName.Replace('_', ' ')),
        };
    }

    private IReadOnlyList<(string Id, string Label, string Icon)> ResolveVisibleTabs()
    {
        if (IsBatchMode)
            return [("details", "Details", GetTabIcon("details")), ("options", "Options", GetTabIcon("options"))];

        return GetAvailableTabIds()
            .OrderBy(tabId => Array.IndexOf(TabDisplayOrder, tabId))
            .Where(IsTabVisible)
            .Select(tabId => (tabId, GetTabLabel(tabId), GetTabIcon(tabId)))
            .ToList();
    }

    private IReadOnlyList<(string Key, string Label)> ResolveQuickSearchTargets()
    {
        if (IsBatchMode)
            return _schema.QuickSearchTargets;

        return (_selectedMediaType, ActiveScope?.ScopeId) switch
        {
            ("TV", "series") => [("show", "Show")],
            ("TV", "season") => [("show", "Show")],
            ("TV", "episode") => [("show_episode", "Episode"), ("show", "Show")],
            ("Music", "album") => [("album", "Album"), ("artist", "Artist")],
            ("Music", "track") => [("track", "Track"), ("album", "Album"), ("artist", "Artist")],
            ("Movies", _) => [("movie_identity", "Movie")],
            ("Comics", _) => [("issue", "Issue")],
            ("Audiobooks", _) => [("audiobook_identity", "Audiobook"), ("narrator", "Narrator")],
            ("Books", _) => [("book_identity", "Book")],
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
            ("TV", "episode") => ["season_number", "episode_number", "episode_title", "runtime", "release_date", "description", "language", "rating", "comment", "sort_title"],
            ("Music", "album") => ["album", "album_artist", "artist", "genre", "year", "language", "description", "rating", "comment", "sort_album"],
            ("Music", "track") => ["title", "artist", "album", "composer", "track_number", "disc_number", "duration", "rating", "comment", "sort_title"],
            ("Movies", "item") => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
            ("Books", "item") => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
            ("Audiobooks", "item") => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
            ("Comics", "item") => _schema.Groups.SelectMany(group => group.Fields).Select(field => field.Key),
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
            ("TV", "episode") =>
                "Episode scope only manages episode stills. Show and season artwork stay on their parent scopes.",
            ("Music", "album") =>
                "Album scope manages cover and square art for the album.",
            ("Music", "track") =>
                "Track scope inherits art from the album. Use the album target to update the artwork people actually see.",
            ("Movies", "item") =>
                "Movie scope manages poster, square art, background, banner, and logo for the movie.",
            ("Books", "item") or ("Audiobooks", "item") or ("Comics", "item") =>
                "Item scope manages cover, square art, and background art for this title.",
            _ =>
                ArtworkScope.ScopeSummary ?? "Showing the artwork slots available for the selected owner.",
        };
    }

    protected string GetArtworkReadOnlyTitle() =>
        (_selectedMediaType, ArtworkScope?.ScopeId) switch
        {
            ("Music", "track") => "Track artwork is inherited",
            _ => "This owner does not manage artwork",
        };

    protected string GetArtworkReadOnlyText() =>
        (_selectedMediaType, ArtworkScope?.ScopeId) switch
        {
            ("Music", "track") => "Tracks inherit their visuals from the album owner. Switch back to the album to update shared artwork.",
            _ => ArtworkScope?.ReadOnlyHint ?? "This owner does not expose editable artwork.",
        };

    protected IReadOnlyList<MediaEditorScopeDto> GetArtworkReadOnlySwitchTargets()
    {
        if (_editorContext is null)
            return [];

        if (_selectedMediaType == "Music" && string.Equals(ArtworkScope?.ScopeId, "track", StringComparison.OrdinalIgnoreCase))
        {
            return _editorContext.Scopes
                .Where(scope => string.Equals(scope.ScopeId, "album", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return [];
    }

    private bool IsTabVisible(string tabId)
    {
        if (IsBatchMode)
            return tabId is "details" or "options";

        return GetAvailableTabIds().Contains(tabId, StringComparer.OrdinalIgnoreCase);
    }

    private string? GetCanonicalSearchUnavailableReason()
    {
        if (!IsSingleItem || IsFileScope || SupportsCanonicalSearch || ActiveScope is null)
            return null;

        return "Canonical search is not available for this selection.";
    }

    private void EnsureActiveTabVisible()
    {
        if (IsTabVisible(_activeTab))
            return;

        if (!string.IsNullOrWhiteSpace(_lastNonFileTab) && IsTabVisible(_lastNonFileTab))
        {
            _activeTab = _lastNonFileTab;
            return;
        }

        _activeTab = Tabs.FirstOrDefault().Id ?? "details";
    }

    private IReadOnlyList<string> GetAvailableTabIds() =>
        _editorContext?.AvailableTabs?.Count > 0
            ? _editorContext.AvailableTabs
            : ["details", "artwork", "links", "options"];

    private string GetTabLabel(string tabId) =>
        tabId switch
        {
            "details" => "Details",
            "episodes" => _editorContext?.ContentTabLabel ?? "Episodes",
            "tracks" => _editorContext?.ContentTabLabel ?? "Tracks",
            "artwork" => "Artwork",
            "links" => "Match & Identity",
            "options" => "Options",
            "file" => "File",
            "history" => "History",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(tabId.Replace('_', ' ')),
        };

    private static string GetTabIcon(string tabId) =>
        tabId switch
        {
            "details" => Icons.Material.Outlined.Article,
            "episodes" => Icons.Material.Outlined.LiveTv,
            "tracks" => Icons.Material.Outlined.LibraryMusic,
            "artwork" => Icons.Material.Outlined.PhotoLibrary,
            "links" => Icons.Material.Outlined.TravelExplore,
            "options" => Icons.Material.Outlined.Tune,
            "file" => Icons.Material.Outlined.InsertDriveFile,
            "history" => Icons.Material.Outlined.History,
            _ => Icons.Material.Outlined.Tab,
        };

    protected string GetDirtySaveLabel() =>
        IsBatchMode
            ? $"Apply ({Request.EntityIds.Count})"
            : ResolveFooterPrimaryActionLabel(EffectiveIdentityIntent);

    protected string GetResolveReviewLabel() =>
        ResolveFooterPrimaryActionLabel(EffectiveIdentityIntent) switch
        {
            "Save Local Changes" => "Resolve Review",
            var label => label,
        };

    protected string GetDetailsPanelTitle(MediaEditorFieldGroup group)
    {
        if (group.Fields.Any(field => field.IdentityField))
            return "Canonical Identity";

        return group.Label;
    }

    protected string? GetDetailsPanelDescription(MediaEditorFieldGroup group)
    {
        if (group.Fields.Any(field => field.IdentityField))
            return "Controlled by canonical identity. Change via Retail Match or Wikidata Match.";

        if (string.Equals(group.Id, "display", StringComparison.OrdinalIgnoreCase)
            || group.Fields.Any(field => IsDisplayOverrideKey(field.Key)))
        {
            return "Local overrides and presentation preferences. These do not affect identity.";
        }

        return null;
    }

    protected IReadOnlyList<MediaEditorFieldDefinition> GetDetailsMetadataFields() =>
        GetGroupsForTab("details")
            .SelectMany(group => group.Fields)
            .Where(field => !IsDisplayOverrideKey(field.Key))
            .ToList();

    private bool IsMultilineField(string key) =>
        GetGroupsForTab("details")
            .SelectMany(group => group.Fields)
            .Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(field.InputKind, "textarea", StringComparison.OrdinalIgnoreCase));

    protected bool IsInlineFieldReadOnly(string key)
    {
        if (!IsFieldLocked(key))
            return false;

        var scopedKey = BuildScopedFieldKey(key);
        return !_inlineOverrideKeys.Contains(scopedKey)
               && !HasActiveDisplayOverride(key)
               && !(_editedValues.ContainsKey(scopedKey) && !_clearedInlineOverrideKeys.Contains(scopedKey));
    }

    protected bool IsInlineFieldAutoFocus(string key)
    {
        var scopedKey = BuildScopedFieldKey(key);
        if (!string.Equals(_fieldToFocus, scopedKey, StringComparison.OrdinalIgnoreCase))
            return false;

        _fieldToFocus = null;
        return true;
    }

    protected string? GetInlineFieldHelpText(MediaEditorFieldDefinition field)
    {
        if (HasActiveDisplayOverride(field.Key))
            return "Local override. Revert to use the canonical value.";

        if (IsInlineOverrideEnabled(field.Key))
            return "Local override is enabled. Changes will be saved locally.";

        if (IsFieldLocked(field.Key))
            return "Canonical value. Unlock to save a local override.";

        return field.IdentityField ? "Used for matching and identity." : null;
    }

    protected string? GetInlineFieldActionIcon(string key)
    {
        if (HasActiveDisplayOverride(key))
            return Icons.Material.Outlined.Restore;

        if (IsInlineOverrideEnabled(key))
            return Icons.Material.Outlined.LockOpen;

        return IsFieldLocked(key) ? Icons.Material.Outlined.Lock : null;
    }

    protected string GetInlineFieldActionLabel(string key, string label) =>
        HasActiveDisplayOverride(key)
            ? $"Remove local override for {label}"
            : $"Use local override for {label}";

    protected Color GetInlineFieldActionColor(string key) =>
        HasActiveDisplayOverride(key)
            ? Color.Warning
            : IsInlineOverrideEnabled(key) ? Color.Warning : Color.Default;

    protected Task HandleInlineFieldActionAsync(string key, string label)
    {
        var scopedKey = BuildScopedFieldKey(key);

        if (HasActiveDisplayOverride(key))
        {
            _pendingInlineRevertKeys.Add(scopedKey);
            StateHasChanged();
            return Task.CompletedTask;
        }

        _inlineOverrideKeys.Add(scopedKey);
        _clearedInlineOverrideKeys.Remove(scopedKey);
        _pendingInlineRevertKeys.Remove(scopedKey);
        _fieldToFocus = scopedKey;
        return InvokeAsync(StateHasChanged);
    }

    protected bool IsInlineFieldConfirmingRevert(string key) =>
        _pendingInlineRevertKeys.Contains(BuildScopedFieldKey(key));

    protected Task ConfirmInlineFieldRevertAsync(string key)
    {
        var scopedKey = BuildScopedFieldKey(key);
        if (HasSavedDisplayOverride(key))
        {
            _editedValues[scopedKey] = string.Empty;
            _clearedInlineOverrideKeys.Add(scopedKey);
        }
        else
        {
            _editedValues.Remove(scopedKey);
            _clearedInlineOverrideKeys.Remove(scopedKey);
        }

        _inlineOverrideKeys.Remove(scopedKey);
        _pendingInlineRevertKeys.Remove(scopedKey);
        return InvokeAsync(StateHasChanged);
    }

    protected void CancelInlineFieldRevert(string key)
    {
        _pendingInlineRevertKeys.Remove(BuildScopedFieldKey(key));
        StateHasChanged();
    }

    protected IReadOnlyList<(string Label, string Value)> GetMatchInformationRows()
    {
        var summary = _editorContext?.IdentitySummary;
        var provider = GetRetailMatchDisplayName(summary);
        var hasProviderEvidence = !string.IsNullOrWhiteSpace(provider) || !string.IsNullOrWhiteSpace(summary?.ProviderItemId);
        var hasQid = !string.IsNullOrWhiteSpace(summary?.WikidataQid);

        return
        [
            ("Retail Match", !string.IsNullOrWhiteSpace(provider) ? provider : hasProviderEvidence ? "Retail matched" : "Not linked"),
            ("Wikidata", hasQid ? summary!.WikidataQid! : "No QID"),
            ("Status", GetMatchStatusLabel(summary, hasProviderEvidence, hasQid)),
            ("Universe", string.IsNullOrWhiteSpace(summary?.UniverseName) ? "-" : summary!.UniverseName!),
        ];
    }

    private string GetMatchStatusLabel(MediaEditorIdentitySummaryDto? summary, bool hasProvider, bool hasQid)
    {
        if (Request.Mode == SharedMediaEditorMode.Review)
            return "Needs review";

        var wikidataStatus = NormalizeIdentityStatus(summary?.WikidataStatus);
        if (wikidataStatus is "user_confirmed" or "user_replaced")
            return "User reviewed";

        if (wikidataStatus is "auto_aligned" or "confirmed")
            return "Identified";

        if (wikidataStatus is "missing" or "provider_only")
            return "Provider only";

        if (wikidataStatus is "user_rejected")
            return "Rejected";

        if (!string.IsNullOrWhiteSpace(_detail?.Status))
        {
            var detailStatus = FormatDetailStatus(_detail.Status);
            if (!string.IsNullOrWhiteSpace(detailStatus))
                return detailStatus;
        }

        if (hasProvider && hasQid)
            return "Identified";

        if (hasProvider)
            return "Pending";

        return "Pending";
    }

    private static string? FormatDetailStatus(string? status)
    {
        var normalized = NormalizeIdentityStatus(status);
        return normalized switch
        {
            "" => null,
            "work" or "edition" or "asset" or "confirmed" or "identified" => "Identified",
            "pending" or "processing" => "Pending",
            "needs_review" or "review" => "Needs review",
            "provider_only" => "Provider only",
            "rejected" => "Rejected",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized.Replace('_', ' ')),
        };
    }

    private string GetRetailMatchDisplayName(MediaEditorIdentitySummaryDto? summary)
    {
        var providerName = NormalizeRetailProviderLabel(summary?.ProviderName);
        if (!string.IsNullOrWhiteSpace(providerName))
            return FormatProviderName(providerName, _selectedMediaType);

        return string.Empty;
    }

    private static string? NormalizeRetailProviderLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "provider", StringComparison.OrdinalIgnoreCase)
            || IsBridgeOnlyProvider(trimmed)
            || LooksLikeBridgeIdentifier(trimmed)
            || Guid.TryParse(trimmed, out _))
        {
            return null;
        }

        return trimmed;
    }

    private static bool IsBridgeOnlyProvider(string value) =>
        value is "imdb" or "imdb_id" or "isbn" or "isbn_10" or "isbn_13" or "asin" or "wikidata" or "wikidata_qid"
        || string.Equals(value, "imdb", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "imdb_id", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "isbn", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "isbn_10", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "isbn_13", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "asin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "wikidata", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "wikidata_qid", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBridgeIdentifier(string value) =>
        value.Contains(':', StringComparison.Ordinal)
        || value.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
        || value.Contains("_id:", StringComparison.OrdinalIgnoreCase);

    protected string GetMatchInformationNote()
    {
        var note = HasCanonicalWikidataIdentity
            ? "Canonical identity is matched. Locked fields use canonical values unless you add a local override."
            : "Retail data is available. Wikidata identity is not confirmed yet.";

        if (Request.Mode == SharedMediaEditorMode.Review)
            note += " Resolve the review after confirming the match.";

        return note;
    }

    protected string GetReviewBannerActionLabel() =>
        _identityIntent switch
        {
            MediaEditorIdentityIntent.FixRetailMatch => "Find",
            MediaEditorIdentityIntent.ConfirmRetailMatch => "Confirm",
            MediaEditorIdentityIntent.FixWikidataMatch => "Fix QID",
            MediaEditorIdentityIntent.ConfirmWikidataMatch => "Choose QID",
            MediaEditorIdentityIntent.MarkWikidataMissing => "Provider-Only",
            MediaEditorIdentityIntent.ReclassifyMediaType => "Change Type",
            MediaEditorIdentityIntent.ConfirmArtwork => "Review Art",
            MediaEditorIdentityIntent.ResolveWriteback => "Retry",
            _ => _primaryActionLabel,
        };

    protected static string GetMediaTypeButtonLabel(string? mediaType) =>
        ReviewTargetResolver.NormalizeMediaType(mediaType) switch
        {
            "Audiobooks" => "Audio",
            "Movies" => "Movie",
            "Comics" => "Comics",
            "Music" => "Music",
            "TV" => "TV",
            _ => "Books",
        };

    protected static string GetMediaTypeMenuLabel(string? mediaType) =>
        ReviewTargetResolver.NormalizeMediaType(mediaType) switch
        {
            "Audiobooks" => "Audiobooks",
            "Movies" => "Movies",
            "Comics" => "Comics",
            "Music" => "Music",
            "TV" => "TV",
            _ => "Books",
        };

    private static string ResolveFooterPrimaryActionLabel(MediaEditorIdentityIntent intent) =>
        intent switch
        {
            MediaEditorIdentityIntent.FixRetailMatch => "Use Selected Retail Match",
            MediaEditorIdentityIntent.ConfirmRetailMatch => "Confirm Retail Match",
            MediaEditorIdentityIntent.FixWikidataMatch => "Replace Wikidata Match",
            MediaEditorIdentityIntent.MarkWikidataMissing => "Mark Provider-Only",
            MediaEditorIdentityIntent.ConfirmArtwork => "Confirm Artwork",
            MediaEditorIdentityIntent.ReclassifyMediaType => "Change Media Type",
            MediaEditorIdentityIntent.ResolveWriteback => "Retry Writeback",
            _ => "Save Local Changes",
        };

    private static string NormalizeIdentityStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();

    private bool IsDisplayOverrideKey(string key) =>
        _editorContext?.DisplayOverrideKeys.Contains(key, StringComparer.OrdinalIgnoreCase) == true;

    private bool HasSavedDisplayOverride(string key) =>
        _editorContext?.DisplayOverrides.ContainsKey(key) == true;

    protected bool HasActiveDisplayOverride(string key)
    {
        var scopedKey = BuildScopedFieldKey(key);
        if (_clearedInlineOverrideKeys.Contains(scopedKey))
            return false;

        return HasSavedDisplayOverride(key)
               || (_editedValues.ContainsKey(scopedKey) && ShouldSaveAsDisplayOverride(scopedKey, key));
    }

    private bool IsInlineOverrideEnabled(string key)
    {
        var scopedKey = BuildScopedFieldKey(key);
        return _inlineOverrideKeys.Contains(scopedKey)
               && !HasActiveDisplayOverride(key)
               && !_clearedInlineOverrideKeys.Contains(scopedKey);
    }

    protected IReadOnlyList<string> GetActiveOverrideLabels()
    {
        var labels = GetGroupsForTab("details")
            .SelectMany(group => group.Fields)
            .Where(field => HasActiveDisplayOverride(field.Key))
            .Select(field => field.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in _editorContext?.DisplayOverrideKeys ?? [])
        {
            if (!HasActiveDisplayOverride(key))
                continue;

            var label = GetDisplayOverrideLabel(key);
            if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                labels.Add(label);
        }

        return labels;
    }

    private bool ShouldSaveAsDisplayOverride(string scopedKey, string key) =>
        IsDisplayOverrideKey(key)
        || _inlineOverrideKeys.Contains(scopedKey)
        || _clearedInlineOverrideKeys.Contains(scopedKey)
        || HasSavedDisplayOverride(key);

    protected bool IsFieldLocked(string key) =>
        (_editorContext?.FieldLockMap.TryGetValue(key, out var locked) == true && locked)
        || (HasCanonicalWikidataIdentity
            && IsIdentityField(key)
            && !IsDisplayOverrideKey(key));

    private bool HasCanonicalWikidataIdentity =>
        !string.IsNullOrWhiteSpace(_editorContext?.IdentitySummary?.WikidataQid)
        && (_editorContext?.IdentitySummary?.WikidataStatus is "confirmed" or "auto_aligned" or "user_confirmed" or "user_replaced"
            || !string.IsNullOrWhiteSpace(_editorContext?.IdentitySummary?.WikidataQid));

    private bool IsIdentityField(string key) =>
        _schema.Groups
            .SelectMany(group => group.Fields)
            .Any(field => field.IdentityField && string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    protected IReadOnlyList<string> GetDisplayDetailOverrideKeys() =>
        (_editorContext?.DisplayOverrideKeys ?? [])
            .Where(key => key is "display_title" or "display_subtitle")
            .ToList();

    private static string GetArtworkEmptyStateLabel(string assetType) =>
        assetType switch
        {
            "SquareArt" => "No square art stored yet.",
            "Background" => "No background art stored yet.",
            "Banner" => "No banner art stored yet.",
            "Logo" => "No logo art stored yet.",
            "DiscArt" => "No disc art stored yet.",
            "ClearArt" => "No clear art stored yet.",
            "CoverArt" => "No cover art stored yet.",
            "SeasonPoster" => "No season poster stored yet.",
            "SeasonThumb" => "No season thumb stored yet.",
            "EpisodeStill" => "No episode still stored yet.",
            _ => "No artwork stored yet.",
        };

    private static string? GetScopePickerTitle(MediaEditorScopeDto? scope)
    {
        if (scope is null)
            return null;

        if (!string.IsNullOrWhiteSpace(scope.DisplayTitle)
            && !string.Equals(scope.DisplayTitle, scope.Label, StringComparison.OrdinalIgnoreCase))
        {
            return scope.DisplayTitle;
        }

        return string.IsNullOrWhiteSpace(scope.DisplaySubtitle) ? null : scope.DisplaySubtitle;
    }

    protected bool IsActiveScopeId(string scopeId) =>
        string.Equals(ActiveScope?.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<ArtworkSlotDefinition> ResolveArtworkSlots(MediaEditorScopeDto? scope) =>
        (_selectedMediaType, scope?.ScopeId, scope?.CanEditArtwork) switch
        {
            ("TV", "series", true) =>
            [
                PosterCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
                ClearArtArtworkSlot,
            ],
            ("Movies", "item", true) =>
            [
                PosterCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
                BannerArtworkSlot,
                LogoArtworkSlot,
                ClearArtArtworkSlot,
                DiscArtArtworkSlot,
            ],
            ("TV", "season", true) =>
            [
                SeasonPosterArtworkSlot,
                SeasonThumbArtworkSlot,
            ],
            ("Music", "album", true) =>
            [
                AlbumArtArtworkSlot,
                SquareArtArtworkSlot,
                DiscArtArtworkSlot,
                ClearArtArtworkSlot,
            ],
            ("TV", "episode", true) =>
            [
                EpisodeStillArtworkSlot,
            ],
            ("Books", "item", true) or ("Audiobooks", "item", true) or ("Comics", "item", true) =>
            [
                BookCoverArtworkSlot,
                SquareArtArtworkSlot,
                BackgroundArtworkSlot,
            ],
            _ => [],
        };

    private IReadOnlyList<string> GetPlacementFieldKeysForActiveScope()
    {
        if (ActiveScope is null)
            return [];

        return (_selectedMediaType, ActiveScope.ScopeId) switch
        {
            ("TV", "episode") => ["show_name", "season_number", "episode_number", "episode_title"],
            ("Music", "track") => ["artist", "album", "track_number", "disc_number"],
            _ => [],
        };
    }

    private bool SupportsMembershipSuggestions(string fieldKey) =>
        !string.IsNullOrWhiteSpace(GetMembershipSuggestionField(fieldKey));

    private string? GetMembershipSuggestionField(string fieldKey) =>
        (_selectedMediaType, ActiveScope?.ScopeId, fieldKey) switch
        {
            ("TV", "episode", "show_name") => "show",
            ("TV", "episode", "season_number") => "season",
            ("Music", "track", "artist") => "artist",
            ("Music", "track", "album") => "album",
            _ => null,
        };

    private async Task OnMembershipFieldInputAsync(string key, string? value)
    {
        OnFieldInput(key, value);

        var scopedKey = BuildScopedFieldKey(key);
        _pendingMembershipPreview = null;
        _selectedMembershipTargetIds.Remove(scopedKey);
        _selectedMembershipSuggestions.Remove(scopedKey);
        ClearDependentMembershipSelections(key);

        var suggestionField = GetMembershipSuggestionField(key);
        var query = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(suggestionField) || query.Length < 2)
        {
            _membershipSuggestions.Remove(scopedKey);
            return;
        }

        var parentContext = ResolveMembershipSuggestionParentContext(key);
        var suggestions = await ApiClient.GetMediaEditorMembershipSuggestionsAsync(
            CurrentEntityId,
            suggestionField,
            query,
            source: "local",
            parentEntityId: parentContext.ParentEntityId,
            parentValue: parentContext.ParentValue);

        _membershipSuggestions[scopedKey] = suggestions;
        StateHasChanged();
    }

    private async Task ApplyMembershipSuggestionAsync(string key, MediaEditorMembershipSuggestionDto suggestion)
    {
        OnFieldInput(key, suggestion.Label);

        var scopedKey = BuildScopedFieldKey(key);
        _selectedMembershipTargetIds[scopedKey] = suggestion.EntityId;
        _selectedMembershipSuggestions[scopedKey] = suggestion;
        _membershipSuggestions.Remove(scopedKey);
        _pendingMembershipPreview = null;

        ClearDependentMembershipSelections(key);

        if (string.Equals(key, "season_number", StringComparison.OrdinalIgnoreCase))
            _editedValues[scopedKey] = ExtractLeadingNumber(suggestion.Label);

        await InvokeAsync(StateHasChanged);
    }

    private IReadOnlyList<MediaEditorMembershipSuggestionDto> GetMembershipSuggestions(string key) =>
        _membershipSuggestions.TryGetValue(BuildScopedFieldKey(key), out var suggestions)
            ? suggestions
            : [];

    private void ClearMembershipSuggestions(string key) =>
        _membershipSuggestions.Remove(BuildScopedFieldKey(key));

    private (Guid? ParentEntityId, string? ParentValue) ResolveMembershipSuggestionParentContext(string fieldKey)
    {
        if (string.Equals(fieldKey, "season_number", StringComparison.OrdinalIgnoreCase))
        {
            var showKey = BuildScopedFieldKey("show_name");
            return (
                _selectedMembershipTargetIds.TryGetValue(showKey, out var targetId) ? targetId : null,
                GetEditableValue("show_name"));
        }

        if (string.Equals(fieldKey, "album", StringComparison.OrdinalIgnoreCase))
            return (null, GetEditableValue("artist"));

        return (null, null);
    }

    protected bool CanSearchRetailMembership(string fieldKey) =>
        (_selectedMediaType, ActiveScope?.ScopeId, fieldKey) switch
        {
            ("TV", "episode", "show_name") => true,
            ("Music", "track", "artist") => true,
            ("Music", "track", "album") => true,
            _ => false,
        };

    protected async Task SearchRetailMembershipAsync(string fieldKey)
    {
        if (!CanSearchRetailMembership(fieldKey))
            return;

        var query = GetEditableValue(fieldKey).Trim();
        if (query.Length < 2)
        {
            Snackbar.Add("Enter at least two characters before searching retail results.", Severity.Info);
            return;
        }

        var suggestionField = GetMembershipSuggestionField(fieldKey);
        if (string.IsNullOrWhiteSpace(suggestionField))
            return;

        var parentContext = ResolveMembershipSuggestionParentContext(fieldKey);
        var scopedKey = BuildScopedFieldKey(fieldKey);
        var suggestions = await ApiClient.GetMediaEditorMembershipSuggestionsAsync(
            CurrentEntityId,
            suggestionField,
            query,
            source: "retail",
            parentEntityId: parentContext.ParentEntityId,
            parentValue: parentContext.ParentValue);

        _membershipSuggestions[scopedKey] = suggestions;
        StateHasChanged();
    }

    private void ClearDependentMembershipSelections(string fieldKey)
    {
        if (string.Equals(fieldKey, "show_name", StringComparison.OrdinalIgnoreCase))
        {
            _selectedMembershipTargetIds.Remove(BuildScopedFieldKey("season_number"));
            _selectedMembershipSuggestions.Remove(BuildScopedFieldKey("season_number"));
            _membershipSuggestions.Remove(BuildScopedFieldKey("season_number"));
        }
        else if (string.Equals(fieldKey, "artist", StringComparison.OrdinalIgnoreCase))
        {
            _selectedMembershipTargetIds.Remove(BuildScopedFieldKey("album"));
            _selectedMembershipSuggestions.Remove(BuildScopedFieldKey("album"));
            _membershipSuggestions.Remove(BuildScopedFieldKey("album"));
        }
    }

    private async Task<MediaEditorMembershipPreviewDto?> PreviewMembershipChangeAsync()
    {
        if (!IsSingleItem || ActiveScope is null)
            return null;

        var placementFields = GetPlacementFieldKeysForActiveScope();
        if (placementFields.Count == 0)
            return null;

        var hasPlacementEdit = placementFields.Any(field =>
        {
            var scopedKey = BuildScopedFieldKey(field);
            return _editedValues.ContainsKey(scopedKey) || _selectedMembershipTargetIds.ContainsKey(scopedKey);
        });

        if (!hasPlacementEdit)
            return null;

        var preview = await ApiClient.PreviewMediaEditorMembershipAsync(CurrentEntityId, BuildMembershipPreviewRequest());
        return preview;
    }

    private MediaEditorMembershipPreviewRequestDto BuildMembershipPreviewRequest()
    {
        var fieldValues = BuildDraftFields()
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);

        var targetIds = _selectedMembershipTargetIds
            .Where(entry =>
            {
                var parsed = ParseScopedKey(entry.Key);
                return parsed.EntityId == CurrentEntityId
                       && string.Equals(parsed.ScopeId, ActiveScope?.ScopeId, StringComparison.OrdinalIgnoreCase);
            })
            .ToDictionary(entry => ParseScopedKey(entry.Key).Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        var selectedSuggestions = _selectedMembershipSuggestions
            .Where(entry =>
            {
                var parsed = ParseScopedKey(entry.Key);
                return parsed.EntityId == CurrentEntityId
                       && string.Equals(parsed.ScopeId, ActiveScope?.ScopeId, StringComparison.OrdinalIgnoreCase);
            })
            .ToDictionary(entry => ParseScopedKey(entry.Key).Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        return new MediaEditorMembershipPreviewRequestDto
        {
            ScopeId = ActiveScope?.ScopeId,
            FieldValues = fieldValues,
            SelectedTargetIds = targetIds,
            SelectedSuggestions = selectedSuggestions,
        };
    }

    private IReadOnlyList<MediaEditorNavigatorNodeDto> GetNavigatorChildren(Guid? parentNodeId)
    {
        if (_navigator is null)
            return [];

        return _navigator.Nodes
            .Where(node => node.ParentNodeId == parentNodeId)
            .OrderBy(node => node.Depth)
            .ThenBy(node => ParseOrdinalLabel(node.OrdinalLabel))
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<MediaEditorNavigatorNodeDto> GetQuarantineTargetNodes(MediaEditorNavigatorNodeDto node)
    {
        if (_navigator is null)
            return [];

        if (node.IsLeaf)
            return node.CanQuarantine ? [node] : [];

        var descendants = new List<MediaEditorNavigatorNodeDto>();
        foreach (var child in GetNavigatorChildren(node.NodeId))
        {
            descendants.AddRange(GetQuarantineTargetNodes(child));
        }

        return descendants
            .Where(item => item.CanQuarantine)
            .GroupBy(item => item.EntityId)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<Guid> GetQuarantineTargetEntityIds()
    {
        if (IsBatchMode || ActiveScope is null)
            return [];

        if (IsContainerEditor && SelectedNavigatorNode is not null)
            return GetQuarantineTargetNodes(SelectedNavigatorNode).Select(node => node.EntityId).ToList();

        return CurrentEntityId == Guid.Empty ? [] : [CurrentEntityId];
    }

    protected async Task ToggleQuarantineConfirmAsync()
    {
        _showQuarantineConfirm = !_showQuarantineConfirm;
        if (!_showQuarantineConfirm)
            return;

        await InvokeAsync(StateHasChanged);
    }

    protected async Task QuarantineCurrentSelectionAsync()
    {
        var entityIds = GetQuarantineTargetEntityIds();
        if (entityIds.Count == 0)
            return;

        _quarantining = true;
        StateHasChanged();

        try
        {
            var response = entityIds.Count == 1
                ? await ApiClient.RejectLibraryCatalogItemAsync(entityIds[0])
                : await ApiClient.BatchRejectLibraryCatalogItemsAsync(entityIds.ToArray());

            if (response is null)
            {
                Snackbar.Add("Quarantine failed.", Severity.Error);
                return;
            }

            Snackbar.Add(entityIds.Count == 1 ? "Item quarantined." : $"{entityIds.Count} items quarantined.", Severity.Success);

            if (IsContainerEditor)
            {
                var fallbackEntityId = ResolveNavigatorFallbackEntityAfterQuarantine();
                if (fallbackEntityId != Guid.Empty)
                {
                    await LoadSingleItemAsync(fallbackEntityId, resetEditorState: false);
                    return;
                }
            }

            MudDialog.Close(DialogResult.Ok(true));
        }
        finally
        {
            _quarantining = false;
            _showQuarantineConfirm = false;
            StateHasChanged();
        }
    }

    private Guid ResolveNavigatorFallbackEntityAfterQuarantine()
    {
        var currentNode = SelectedNavigatorNode;
        if (currentNode is null || _navigator is null)
            return Guid.Empty;

        if (currentNode.ParentNodeId is Guid parentNodeId)
        {
            var parentNode = _navigator.Nodes.FirstOrDefault(node => node.NodeId == parentNodeId);
            if (parentNode is not null)
                return parentNode.EntityId;
        }

        return NavigatorRootNode?.EntityId ?? Guid.Empty;
    }

    protected bool IsContentNodeSelected(MediaEditorNavigatorNodeDto node) =>
        node.EntityId == EditorContextEntityId;

    protected string GetContentEmptyStateText() =>
        _activeTab switch
        {
            "episodes" => "No episodes are available for this show yet.",
            "tracks" => "No tracks are available for this album yet.",
            _ => "No content is available for this selection.",
        };

    protected string GetMembershipSuggestionMeta(MediaEditorMembershipSuggestionDto suggestion)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(suggestion.Source))
            parts.Add(string.Equals(suggestion.Source, "retail", StringComparison.OrdinalIgnoreCase) ? "Retail" : "Local");

        if (!string.IsNullOrWhiteSpace(suggestion.ProviderName))
            parts.Add(FormatProviderName(suggestion.ProviderName));

        if (!string.IsNullOrWhiteSpace(suggestion.Subtitle))
            parts.Add(suggestion.Subtitle!);

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    protected string GetDeleteConfirmationText()
    {
        var count = GetQuarantineTargetEntityIds().Count;
        if (count <= 0)
            return "Quarantine this item?";

        return count == 1
            ? "Quarantine this item?"
            : $"Quarantine {count} linked items?";
    }

    protected string GetDisplayOverrideLabel(string key) =>
        key switch
        {
            "display_title" => "Display Title",
            "display_subtitle" => "Display Subtitle",
            "sort_title" => "Sort Title",
            "sort_album" => "Sort Album",
            "sort_series" => "Sort Series",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key.Replace('_', ' ')),
        };

    protected string GetDisplayOverrideHint(string key) =>
        key switch
        {
            "display_title" => "Shown in the library instead of the locked canonical title.",
            "display_subtitle" => "Optional local subtitle or tagline.",
            _ => "Local alias used for presentation only.",
        };

    private string NormalizeTabId(string? tabId) =>
        (tabId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "identity" => "links",
            "universe" => "links",
            "id" => "links",
            "inspector" => "file",
            "" => "details",
            var normalized => normalized,
        };

    private async Task SelectTabInternalAsync(string tabId)
    {
        if (IsTabDisabled(tabId))
            return;

        var normalized = NormalizeTabId(tabId);
        if (string.Equals(normalized, "file", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(ActiveScope?.ScopeId, "file", StringComparison.OrdinalIgnoreCase) && ActiveScopeExists("file"))
            {
                _lastNonFileTab = string.IsNullOrWhiteSpace(_lastNonFileTab) ? "details" : _lastNonFileTab;
                await SelectScopeAsync("file");
                return;
            }

            _activeTab = "file";
            return;
        }

        _activeTab = normalized;
        _lastNonFileTab = normalized;

        if (IsFileScope)
        {
            var fallbackScopeId = _editorContext?.Scopes
                .OrderBy(scope => scope.Order)
                .FirstOrDefault(scope => !string.Equals(scope.ScopeId, "file", StringComparison.OrdinalIgnoreCase))
                ?.ScopeId;

            if (!string.IsNullOrWhiteSpace(fallbackScopeId))
                await SelectScopeAsync(fallbackScopeId);
        }
    }

    private static int ParseOrdinalLabel(string? ordinalLabel)
    {
        if (string.IsNullOrWhiteSpace(ordinalLabel))
            return int.MaxValue;

        var digits = new string(ordinalLabel.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : int.MaxValue;
    }

    private static string ExtractLeadingNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var digits = new string(input.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? input : digits;
    }

    private async Task RefreshArtworkStateAsync(string? scopeId = null, bool notifyParent = false)
    {
        if (!IsSingleItem)
            return;

        var targetScopeId = scopeId ?? ArtworkScope?.ScopeId;
        if (string.IsNullOrWhiteSpace(targetScopeId))
            return;

        var targetScope = GetScopeById(targetScopeId);
        if (targetScope is null)
            return;

        var stateKey = BuildScopeStateKey(targetScope.FieldEntityId, targetScope.ScopeId);

        _artworkStates.Remove(stateKey);
        _scopeStates.Remove(stateKey);

        if (string.Equals(ActiveScope?.ScopeId, targetScopeId, StringComparison.OrdinalIgnoreCase))
            await LoadScopeStateAsync(forceReload: true);
        else
            await LoadArtworkStateAsync(targetScopeId, forceReload: true);

        CloseArtworkZoom();
        NormalizeArtworkSelection();
        StateHasChanged();

        if (notifyParent && Request.OnArtworkChanged is not null)
            await Request.OnArtworkChanged.Invoke();
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
            : $"{ActiveScope.FieldEntityId:D}|{ActiveScope.ScopeId}|{key}";

    private string BuildScopedArtworkKey(string? scopeId, string assetType) =>
        IsBatchMode || string.IsNullOrWhiteSpace(scopeId)
            ? assetType
            : $"{GetScopeById(scopeId)?.FieldEntityId ?? EditorContextEntityId:D}|{scopeId}|{assetType}";

    private static (Guid EntityId, string ScopeId, string Key) ParseScopedKey(string compositeKey)
    {
        var segments = compositeKey.Split('|', 3, StringSplitOptions.None);
        if (segments.Length == 3 && Guid.TryParse(segments[0], out var entityId))
            return (entityId, segments[1], segments[2]);

        if (segments.Length == 2)
            return (Guid.Empty, segments[0], segments[1]);

        return (Guid.Empty, string.Empty, compositeKey);
    }

    private static string BuildScopeStateKey(Guid entityId, string scopeId) =>
        $"{entityId:D}|{scopeId}";

    private readonly record struct ScopedEditorKey(Guid EntityId, string ScopeId);

    private sealed class ScopedEditorKeyComparer : IEqualityComparer<ScopedEditorKey>
    {
        public static ScopedEditorKeyComparer Instance { get; } = new();

        public bool Equals(ScopedEditorKey x, ScopedEditorKey y) =>
            x.EntityId == y.EntityId && string.Equals(x.ScopeId, y.ScopeId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ScopedEditorKey obj) =>
            HashCode.Combine(obj.EntityId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ScopeId ?? string.Empty));
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
        var headerScope = IsContainerEditor ? ContainerRootScope ?? ActiveScope : ActiveScope;
        foreach (var slot in ResolveArtworkSlots(headerScope))
        {
            var previewUrl = GetHeaderArtworkPreviewUrl(headerScope, slot.AssetType);
            if (!string.IsNullOrWhiteSpace(previewUrl))
                return previewUrl;
        }

        return Request.CoverUrl;
    }

    private string? GetHeaderArtworkPreviewUrl(MediaEditorScopeDto? scope, string assetType)
    {
        var artwork = scope is null
            ? _artwork
            : _artworkStates.TryGetValue(BuildScopeStateKey(scope.FieldEntityId, scope.ScopeId), out var scopedArtwork)
                ? scopedArtwork
                : _artwork;

        return artwork?.Slots.FirstOrDefault(slot =>
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

    protected string GetArtworkSlotCount(ArtworkSlotDefinition slot) =>
        FormatCountBadge(GetArtworkGalleryItems(slot.AssetType).Count) ?? "0";

    protected Task OnSelectedMediaTypeChanged(string value)
    {
        _selectedMediaType = value;
        return Task.CompletedTask;
    }

    protected string FormatUniverseStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "Pending"
            : status.Replace("_", " ");

    private static int GetSelectedIndex(IEnumerable<string?> keys, string? activeKey)
    {
        var index = 0;
        foreach (var key in keys)
        {
            if (string.Equals(key, activeKey, StringComparison.OrdinalIgnoreCase))
                return index;

            index++;
        }

        return 0;
    }
}
