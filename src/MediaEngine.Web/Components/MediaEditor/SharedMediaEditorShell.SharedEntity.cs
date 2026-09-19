using MediaEngine.Contracts.Universe;

namespace MediaEngine.Web.Components.MediaEditor;

public partial class SharedMediaEditorShell
{
    private SharedEntityEditorWorkspace? _sharedEntityWorkspace;
    private SharedEntityEditorContextDto? _sharedEntityContext;
    private bool _sharedEntityDirty;

    protected bool IsSharedEntityMode => Request.SharedEntityTarget is not null;

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
