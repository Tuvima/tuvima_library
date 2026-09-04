using MediaEngine.Web.Components.Collections;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Services.Editing;

public sealed class CollectionEditorLauncherService
{
    private readonly IDialogService _dialogService;
    private readonly AdministratorElevationNavigationService? _elevation;

    public CollectionEditorLauncherService(IDialogService dialogService, AdministratorElevationNavigationService? elevation = null)
    {
        _dialogService = dialogService;
        _elevation = elevation;
    }

    public CollectionEditorInlineSession BeginInline(CollectionEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EditingCollection is null)
            throw new InvalidOperationException("Inline collection editing requires an existing collection.");

        return new CollectionEditorInlineSession(request);
    }

    public async Task<bool> OpenAsync(CollectionEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isManualPlaylist = request.Mode == CollectionEditorMode.ManualPlaylist;
        var isSmartPlaylist = request.Mode == CollectionEditorMode.SmartPlaylist;

        if (!isManualPlaylist && !isSmartPlaylist && _elevation is not null && !await _elevation.EnsureElevatedAsync())
            return false;

        if (request.EditingCollection is null && request.Mode == CollectionEditorMode.CuratedCollection)
            return await OpenGuidedSetupAsync(request);

        var isCollectionEditor = !isManualPlaylist && !isSmartPlaylist;
        var dialog = await _dialogService.ShowAsync<CollectionEditorShell>(
            EditDialogTitleFor(request),
            new DialogParameters
            {
                { nameof(CollectionEditorShell.Request), request },
            },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = isCollectionEditor ? MaxWidth.ExtraLarge : isManualPlaylist ? MaxWidth.Small : MaxWidth.Medium,
                FullWidth = isCollectionEditor,
                BackdropClick = false,
                CloseOnEscapeKey = true,
            });

        if (dialog is null)
            return false;

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private async Task<bool> OpenGuidedSetupAsync(CollectionEditorLaunchRequest request)
    {
        var dialog = await _dialogService.ShowAsync<CollectionWizard>(
            DialogTitleFor(request),
            new DialogParameters
            {
                { nameof(CollectionWizard.Request), request },
            },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = request.Mode == CollectionEditorMode.CuratedCollection ? MaxWidth.ExtraLarge : MaxWidth.Medium,
                FullWidth = request.Mode == CollectionEditorMode.CuratedCollection,
                BackdropClick = false,
                CloseOnEscapeKey = true,
            });

        if (dialog is null)
            return false;

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private static string DialogTitleFor(CollectionEditorLaunchRequest request) =>
        request.Mode switch
        {
            CollectionEditorMode.ManualPlaylist => "New Playlist",
            CollectionEditorMode.SmartPlaylist => "New Smart Playlist",
            _ => "New Collection",
        };

    private static string EditDialogTitleFor(CollectionEditorLaunchRequest request) =>
        request.Mode switch
        {
            CollectionEditorMode.ManualPlaylist => "Playlist",
            CollectionEditorMode.SmartPlaylist => "Edit Smart Playlist",
            _ => "Edit Collection",
        };
}

public sealed class CollectionEditorInlineSession
{
    internal CollectionEditorInlineSession(CollectionEditorLaunchRequest request)
    {
        Request = request;
    }

    public CollectionEditorLaunchRequest Request { get; }
}
