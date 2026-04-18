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

        var dialog = await _dialogService.ShowAsync<CollectionEditorShell>(
            request.EditingCollection is null ? "New Collection" : "Edit Collection",
            new DialogParameters
            {
                { nameof(CollectionEditorShell.Request), request },
            },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = MaxWidth.ExtraLarge,
                FullWidth = true,
                BackdropClick = false,
            });

        if (dialog is null)
            return false;

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }
}
