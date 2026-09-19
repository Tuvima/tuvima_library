using MediaEngine.Contracts.Metadata;

namespace MediaEngine.Web.Components.MediaEditor;

public static class RetailHierarchyPreviewPolicy
{
    public static bool CanApply(
        bool previewRequired,
        bool loading,
        bool candidatePreviewMatches,
        MediaEditorMembershipPreviewDto? preview)
    {
        if (!previewRequired)
        {
            return true;
        }

        if (loading || !candidatePreviewMatches || preview is null)
        {
            return false;
        }

        return IsNoOp(preview) || preview.CanApply;
    }

    public static string? GetStatus(
        bool previewRequired,
        bool loading,
        bool candidatePreviewMatches,
        MediaEditorMembershipPreviewDto? preview)
    {
        if (!previewRequired)
        {
            return null;
        }

        if (loading)
        {
            return "Checking the selected match for hierarchy changes…";
        }

        if (!candidatePreviewMatches || preview is null)
        {
            return "Hierarchy preview is unavailable. Select this match again to retry before applying it.";
        }

        if (IsNoOp(preview) || preview.CanApply)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(preview.ConflictMessage)
            ? preview.Message
            : preview.ConflictMessage;
        return string.IsNullOrWhiteSpace(reason)
            ? "This retail match cannot be applied because its hierarchy change is not valid."
            : $"{reason} This retail match cannot be applied until the hierarchy issue is resolved.";
    }

    public static bool ShouldShowImpactNotice(MediaEditorMembershipPreviewDto? preview) =>
        preview is { CanApply: true }
        && !string.IsNullOrWhiteSpace(preview.Action)
        && !string.Equals(preview.Action, "none", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(preview.CurrentPath)
        && !string.IsNullOrWhiteSpace(preview.TargetPath)
        && !string.Equals(preview.CurrentPath.Trim(), preview.TargetPath.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsNoOp(MediaEditorMembershipPreviewDto preview) =>
        string.Equals(preview.Action, "none", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(preview.CurrentPath)
        && !string.IsNullOrWhiteSpace(preview.TargetPath)
        && string.Equals(preview.CurrentPath.Trim(), preview.TargetPath.Trim(), StringComparison.OrdinalIgnoreCase);
}
