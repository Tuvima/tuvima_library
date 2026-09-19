using MediaEngine.Web.Components.Collections;
using MudBlazor;

namespace MediaEngine.Web.Services.Editing;

public sealed class GalleryEditorLauncherService(IDialogService dialogService)
{
    public async Task<bool> OpenAsync(GalleryEditorLaunchRequest request)
    {
        var dialog = await dialogService.ShowAsync<GalleryEditorShell>(
            request.EditingGallery is null ? "New Gallery" : "Edit Gallery",
            new DialogParameters { { nameof(GalleryEditorShell.Request), request } },
            new DialogOptions
            {
                CloseButton = false,
                NoHeader = true,
                MaxWidth = MaxWidth.ExtraLarge,
                FullWidth = true,
                BackdropClick = false,
                CloseOnEscapeKey = true,
            });

        if (dialog is null)
            return false;

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }
}
