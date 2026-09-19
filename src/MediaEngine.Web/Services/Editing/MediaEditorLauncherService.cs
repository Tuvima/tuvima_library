using MediaEngine.Contracts.Universe;
using MediaEngine.Web.Components.MediaEditor;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Services.Editing;

public sealed class MediaEditorLauncherService
{
    private readonly IDialogService _dialogService;
    private readonly AdministratorSurfaceAccessService? _administratorAccess;

    public MediaEditorLauncherService(IDialogService dialogService, AdministratorSurfaceAccessService? administratorAccess = null)
    {
        _dialogService = dialogService;
        _administratorAccess = administratorAccess;
    }

    public async Task<bool> OpenAsync(MediaEditorLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sharedTarget = request.SharedEntityTarget;
        if (sharedTarget is null && request.EntityIds.Count == 0)
        {
            return false;
        }

        if (sharedTarget is not null
            && (request.Mode != SharedMediaEditorMode.Normal || !IsValidSharedTarget(sharedTarget)))
        {
            return false;
        }

        if (_administratorAccess is not null && !await _administratorAccess.EnsureUnlockedAsync())
        {
            return false;
        }

        if (request.Mode == SharedMediaEditorMode.Batch && request.EntityIds.Count <= 1)
        {
            return false;
        }

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
            {
                return false;
            }

            var confirmResult = await confirmDialog.Result;
            if (confirmResult is null || confirmResult.Canceled)
            {
                return false;
            }
        }

        if (string.Equals(request.LaunchEntityKind, "Person", StringComparison.OrdinalIgnoreCase))
        {
            var personDialog = await _dialogService.ShowAsync<PersonEditorDialog>(
                "Edit Person",
                new DialogParameters { { nameof(PersonEditorDialog.Request), request } },
                new DialogOptions
                {
                    CloseButton = false,
                    NoHeader = true,
                    MaxWidth = MaxWidth.ExtraLarge,
                    FullWidth = true,
                    BackdropClick = false,
                    CloseOnEscapeKey = true,
                });
            if (personDialog is null)
            {
                return false;
            }

            var personResult = await personDialog.Result;
            return personResult is not null && !personResult.Canceled;
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
                MaxWidth = MaxWidth.ExtraLarge,
                FullWidth = true,
                BackdropClick = false,
                CloseOnEscapeKey = true,
            });
        if (dialog is null)
        {
            return false;
        }

        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private static bool IsValidSharedTarget(SharedEntityEditorTargetDto target)
    {
        if (string.IsNullOrWhiteSpace(target.UniverseQid))
        {
            return false;
        }

        if (string.Equals(target.Kind, SharedEntityEditorTargetKinds.Universe, StringComparison.OrdinalIgnoreCase))
        {
            return !target.FictionalEntityId.HasValue
                && string.Equals(target.Qid, target.UniverseQid, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(target.Kind, SharedEntityEditorTargetKinds.FictionalEntity, StringComparison.OrdinalIgnoreCase)
            && target.FictionalEntityId.HasValue
            && !string.IsNullOrWhiteSpace(target.Qid);
    }
}
