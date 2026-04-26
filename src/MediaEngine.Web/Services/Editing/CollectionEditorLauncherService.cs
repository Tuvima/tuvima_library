using MediaEngine.Web.Components.Collections;
using MudBlazor;

namespace MediaEngine.Web.Services.Editing;

public sealed class CollectionEditorLauncherService
{
    private readonly IDialogService _dialogService;

    public CollectionEditorLauncherService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async Task<bool> OpenAsync(CollectionEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isManualPlaylist = string.Equals(request.Mode, "Playlist", StringComparison.OrdinalIgnoreCase);
        var isSmartPlaylist = string.Equals(request.Mode, "SmartPlaylist", StringComparison.OrdinalIgnoreCase);

        var dialog = await _dialogService.ShowAsync<CollectionEditorShell>(
            request.EditingCollection is null ? DialogTitleFor(request) : EditDialogTitleFor(request),
            new DialogParameters
            {
                { nameof(CollectionEditorShell.Request), request },
            },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = isManualPlaylist ? MaxWidth.Small : isSmartPlaylist ? MaxWidth.Medium : MaxWidth.Large,
                FullWidth = false,
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
            "Playlist" => "New Playlist",
            "SmartPlaylist" => "New Smart Playlist",
            _ => "New Collection",
        };

    private static string EditDialogTitleFor(CollectionEditorLaunchRequest request) =>
        request.Mode switch
        {
            "Playlist" => "Edit Playlist",
            "SmartPlaylist" => "Edit Smart Playlist",
            _ => "Edit Collection",
        };
}
