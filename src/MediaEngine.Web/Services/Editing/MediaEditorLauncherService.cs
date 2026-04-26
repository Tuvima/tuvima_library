using MediaEngine.Web.Components.MediaEditor;
using MudBlazor;

namespace MediaEngine.Web.Services.Editing;

public sealed class MediaEditorLauncherService
{
    private readonly IDialogService _dialogService;

    public MediaEditorLauncherService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async Task<bool> OpenAsync(MediaEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EntityIds.Count == 0)
            return false;

        if (request.Mode == SharedMediaEditorMode.Batch && request.EntityIds.Count > 1)
        {
            var confirmDialog = await _dialogService.ShowAsync<SharedMediaBatchConfirmDialog>(
                "Edit Items",
                new DialogOptions
                {
                    CloseButton = false,
                    NoHeader = true,
                    MaxWidth = MaxWidth.Small,
                    FullWidth = true,
                    BackdropClick = true,
                    CloseOnEscapeKey = true,
                });
            if (confirmDialog is null)
                return false;
            var confirmResult = await confirmDialog.Result;
            if (confirmResult is null || confirmResult.Canceled)
                return false;
        }

        var dialog = await _dialogService.ShowAsync<SharedMediaEditorShell>(
            request.Mode == SharedMediaEditorMode.Batch ? "Edit Items" : "Edit Item",
            new DialogParameters
            {
                { nameof(SharedMediaEditorShell.Request), request },
            },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = MaxWidth.Large,
                FullWidth = false,
                BackdropClick = false,
                CloseOnEscapeKey = true,
            });
        if (dialog is null)
            return false;

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }
}
