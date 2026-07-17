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

    public MediaEditorInlineSession BeginInline(MediaEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Mode != SharedMediaEditorMode.Normal)
            throw new InvalidOperationException("Only normal single-item editing can be hosted inline.");

        if (request.EntityIds.Count != 1)
            throw new InvalidOperationException("Inline editing requires exactly one media entity.");

        return new MediaEditorInlineSession(request);
    }

    public async Task<bool> OpenAsync(MediaEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EntityIds.Count == 0)
            return false;

        if (request.Mode == SharedMediaEditorMode.Batch && request.EntityIds.Count <= 1)
            return false;

        if (request.Mode == SharedMediaEditorMode.Batch)
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
                MaxWidth = MaxWidth.False,
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

public sealed class MediaEditorInlineSession
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MediaEditorInlineSession(MediaEditorLaunchRequest request)
    {
        Request = request;
    }

    public MediaEditorLaunchRequest Request { get; }

    public Task<bool> Completion => _completion.Task;

    public bool TryComplete(bool applied) => _completion.TrySetResult(applied);
}
