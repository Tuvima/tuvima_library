using MediaEngine.Contracts.Universe;
using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Components.MediaEditor;

public partial class SharedMediaEditorShell
{
    private SharedEntityEditorWorkspace? _sharedEntityWorkspace;
    private SharedEntityEditorContextDto? _sharedEntityContext;
    private bool _sharedEntityDirty;
    private bool _sharedEntityMode;
    private bool? _pendingSharedEntityMode;
    private bool _switchSharedEntityModeAfterSave;

    protected bool IsSharedEntityMode => _sharedEntityMode;
    protected bool CanSwitchEditorSurface => Request.Mode == SharedMediaEditorMode.Normal
        && Request.SharedEntityTarget is not null
        && Request.EntityIds.Count == 1
        && (Request.LaunchEntityId ?? Request.EntityIds[0]) != Guid.Empty;
    protected bool HasPendingSharedEntityModeSwitch => _pendingSharedEntityMode.HasValue;
    protected string PendingSharedEntityModeTitle => _pendingSharedEntityMode == true
        ? "the shared Universe editor"
        : Request.HeaderTitle ?? "the original media item";
    protected string SharedEntityModeActionLabel => IsSharedEntityMode
        ? $"Return to {Request.HeaderTitle ?? "media item"}"
        : "Edit shared Universe";
    protected string CurrentEditorSurfaceTitle => IsSharedEntityMode
        ? _sharedEntityContext?.label ?? Request.SharedEntityTarget?.UniverseQid ?? "the shared entity"
        : HeaderTitle;

    protected void InitializeSharedEntityMode() =>
        _sharedEntityMode = Request.SharedEntityTarget is not null && Request.EntityIds.Count == 0;

    protected async Task RequestSharedEntityModeSwitchAsync()
    {
        if (!CanSwitchEditorSurface)
            return;

        var targetMode = !IsSharedEntityMode;
        if (IsDirty)
        {
            _pendingSharedEntityMode = targetMode;
            return;
        }

        await SetSharedEntityModeAsync(targetMode);
    }

    protected void KeepEditingCurrentSurface() => _pendingSharedEntityMode = null;

    protected async Task DiscardAndSwitchSharedEntityModeAsync()
    {
        if (_pendingSharedEntityMode is not bool targetMode)
            return;

        _pendingSharedEntityMode = null;
        ResetEditorChanges();
        await SetSharedEntityModeAsync(targetMode);
    }

    protected async Task SaveAndSwitchSharedEntityModeAsync()
    {
        if (_pendingSharedEntityMode is not bool targetMode || _saving)
            return;

        if (IsSharedEntityMode)
        {
            if (_sharedEntityWorkspace is null || !await _sharedEntityWorkspace.SaveAsync())
                return;

            _hasCommittedChanges = true;
            _pendingSharedEntityMode = null;
            await SetSharedEntityModeAsync(targetMode);
            return;
        }

        _switchSharedEntityModeAfterSave = true;
        await SaveAsyncCore(applyMembershipMove: false);
        _switchSharedEntityModeAfterSave = false;
    }

    private async Task<bool> CompletePendingSharedEntityModeSwitchAsync()
    {
        if (!_switchSharedEntityModeAfterSave || _pendingSharedEntityMode is not bool targetMode)
            return false;

        _switchSharedEntityModeAfterSave = false;
        _pendingSharedEntityMode = null;
        if (targetMode)
            _hasCommittedChanges = true;
        await SetSharedEntityModeAsync(targetMode);
        return true;
    }

    private async Task SetSharedEntityModeAsync(bool sharedEntityMode)
    {
        _pendingSharedEntityMode = null;
        _sharedEntityDirty = false;
        _sharedEntityWorkspace = null;
        _sharedEntityContext = null;
        _sharedEntityMode = false;

        if (Request.EntityIds.Count > 0)
        {
            var returnEntityId = Request.LaunchEntityId ?? Request.EntityIds.FirstOrDefault();
            if (returnEntityId != Guid.Empty)
            {
                await LoadSingleItemAsync(returnEntityId, resetEditorState: true);
                await LoadRefreshScheduleAsync();
            }
        }

        _sharedEntityMode = sharedEntityMode;
        await InvokeAsync(StateHasChanged);
    }

    private Task OnSharedEntityDirtyChanged(bool dirty)
    {
        _sharedEntityDirty = dirty;
        return InvokeAsync(StateHasChanged);
    }

    private Task OnSharedEntityContextChanged(SharedEntityEditorContextDto? context)
    {
        _sharedEntityContext = context;
        return InvokeAsync(StateHasChanged);
    }

    private async Task OnSharedEntityCommittedAsync(SharedEntityEditorCommitKind kind)
    {
        _hasCommittedChanges = true;
        if (kind == SharedEntityEditorCommitKind.Artwork && Request.OnArtworkChanged is not null)
        {
            try
            {
                await Request.OnArtworkChanged.Invoke();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "The shared entity artwork was saved, but its host refresh callback failed");
            }
        }
        await InvokeAsync(StateHasChanged);
    }
}
