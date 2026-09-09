using System.Collections.Frozen;

namespace MediaEngine.Domain.Authorization;

public sealed record PermissionDefinition
{
    public PermissionDefinition(
        ApplicationPermissionId id,
        string category,
        string displayName,
        string description,
        PermissionRiskLevel riskLevel,
        IEnumerable<ApplicationType> applicationTypes,
        bool requiresUserContext,
        PermissionProvenance provenance,
        string? pluginId,
        int sortOrder,
        PermissionAvailability availability,
        string? unavailableReason)
    {
        Id = id;
        Category = RequireText(category, nameof(category));
        DisplayName = RequireText(displayName, nameof(displayName));
        Description = RequireText(description, nameof(description));
        RiskLevel = riskLevel;
        ApplicationTypes = (applicationTypes ?? throw new ArgumentNullException(nameof(applicationTypes))).ToFrozenSet();
        if (ApplicationTypes.Count == 0)
        {
            throw new ArgumentException("At least one application type is required.", nameof(applicationTypes));
        }

        RequiresUserContext = requiresUserContext;
        Provenance = provenance;
        PluginId = string.IsNullOrWhiteSpace(pluginId) ? null : pluginId.Trim();
        SortOrder = sortOrder;
        Availability = availability;
        UnavailableReason = string.IsNullOrWhiteSpace(unavailableReason) ? null : unavailableReason.Trim();
        if (Availability == PermissionAvailability.Unavailable && UnavailableReason is null)
        {
            throw new ArgumentException("Unavailable permissions require a reason.", nameof(unavailableReason));
        }

        if (Availability == PermissionAvailability.Available && UnavailableReason is not null)
        {
            throw new ArgumentException("Available permissions cannot have an unavailable reason.", nameof(unavailableReason));
        }
    }

    public ApplicationPermissionId Id { get; }
    public string Category { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public PermissionRiskLevel RiskLevel { get; }
    public IReadOnlySet<ApplicationType> ApplicationTypes { get; }
    public bool RequiresUserContext { get; }
    public PermissionProvenance Provenance { get; }
    public string? PluginId { get; }
    public int SortOrder { get; }
    public PermissionAvailability Availability { get; }
    public string? UnavailableReason { get; }
    public bool IsBuiltIn => Provenance == PermissionProvenance.BuiltIn;
    public bool IsAvailable => Availability == PermissionAvailability.Available;

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
