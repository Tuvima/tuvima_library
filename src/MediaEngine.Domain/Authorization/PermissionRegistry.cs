using System.Collections.Frozen;
using System.Collections.ObjectModel;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Domain.Authorization;

public sealed class PermissionRegistry : IPermissionRegistry
{
    private static readonly FrozenSet<ApplicationType> AllApplicationTypes =
        Enum.GetValues<ApplicationType>().ToFrozenSet();

    private readonly ReadOnlyCollection<PermissionDefinition> _definitions;
    private readonly FrozenDictionary<ApplicationPermissionId, PermissionDefinition> _byId;

    public PermissionRegistry(IEnumerable<PermissionDefinition>? pluginDefinitions = null)
    {
        var definitions = CreateBuiltIns().ToList();
        if (pluginDefinitions is not null)
        {
            foreach (var definition in pluginDefinitions)
            {
                ValidatePluginDefinition(definition);
                definitions.Add(definition);
            }
        }

        var duplicate = definitions.GroupBy(definition => definition.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate permission identifier '{duplicate.Key}'.");
        }

        _definitions = Array.AsReadOnly(definitions
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Id.Value, StringComparer.Ordinal)
            .ToArray());
        _byId = _definitions.ToFrozenDictionary(definition => definition.Id);
    }

    public IReadOnlyList<PermissionDefinition> GetAll() => _definitions;

    public bool TryGet(ApplicationPermissionId id, out PermissionDefinition definition) =>
        _byId.TryGetValue(id, out definition!);

    public IReadOnlyList<PermissionDefinition> GetAvailableFor(ApplicationType applicationType) =>
        Array.AsReadOnly(_definitions
            .Where(definition => definition.IsAvailable && definition.ApplicationTypes.Contains(applicationType))
            .ToArray());

    private static IEnumerable<PermissionDefinition> CreateBuiltIns()
    {
        var unavailable = new Dictionary<ApplicationPermissionId, string>
        {
            [ApplicationPermissionIds.SystemMetricsRead] = "No application-facing system metrics service exists.",
            [ApplicationPermissionIds.SystemAuditRead] = "The activity log is not a security audit read service.",
            [ApplicationPermissionIds.SystemLogsRead] = "No application-facing diagnostic log service exists.",
            [ApplicationPermissionIds.PlaybackSessionsControl] = "Authorized cross-session playback control is not implemented.",
            [ApplicationPermissionIds.IdentitySessionsRead] = "Only self-service session reads exist.",
            [ApplicationPermissionIds.AiInfer] = "No bounded application-facing inference service exists.",
        };

        var ids = typeof(ApplicationPermissionIds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(ApplicationPermissionId))
            .Select(field => (ApplicationPermissionId)field.GetValue(null)!)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            var category = CategoryFor(id);
            var isUnavailable = unavailable.TryGetValue(id, out var reason);
            yield return new PermissionDefinition(
                id,
                category,
                DisplayNameFor(id),
                DescriptionFor(id),
                RiskFor(id),
                AllApplicationTypes,
                RequiresUserContext(id),
                PermissionProvenance.BuiltIn,
                null,
                CategoryOrder(category) * 100 + index,
                isUnavailable ? PermissionAvailability.Unavailable : PermissionAvailability.Available,
                reason);
        }
    }

    private static void ValidatePluginDefinition(PermissionDefinition definition)
    {
        if (definition.Provenance != PermissionProvenance.Plugin || string.IsNullOrWhiteSpace(definition.PluginId))
        {
            throw new ArgumentException("Plugin permissions require plugin provenance and a plugin identifier.", nameof(definition));
        }

        if (!string.Equals(definition.PluginId, definition.PluginId.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Plugin identifiers in permission namespaces must be lowercase.", nameof(definition));
        }

        var prefix = $"plugin.{definition.PluginId}.";
        if (!definition.Id.Value.StartsWith(prefix, StringComparison.Ordinal) || definition.Id.Value.Length == prefix.Length)
        {
            throw new ArgumentException($"Plugin permission '{definition.Id}' must use the namespace '{prefix}'.", nameof(definition));
        }

        if (definition.PluginId.Length > 128
            || definition.PluginId[0] == '.'
            || definition.PluginId[^1] == '.'
            || definition.PluginId.Split('.').Any(segment =>
                segment.Length == 0
                || segment is "." or ".."
                || segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))))
        {
            throw new ArgumentException(
                "Plugin identifiers in permission namespaces must contain bounded lowercase dot-separated ASCII segments.",
                nameof(definition));
        }
    }

    private static string CategoryFor(ApplicationPermissionId id) => id.Value.Split('.')[0] switch
    {
        "system" => "Server & Monitoring",
        "library" or "artwork" => "Library",
        "playback" or "queue" or "progress" or "downloads" => "Playback",
        "analytics" => "Analytics",
        "metadata" => "Metadata",
        "providers" => "Providers",
        "ingestion" => "Ingestion",
        "review" => "Review",
        "collections" => "Collections",
        "view" => "View",
        "identity" => "Identity",
        "plugins" => "Plugins",
        "ai" => "AI",
        "network" => "Network",
        "storage" => "Storage",
        "backup" => "Backup",
        "events" => "Events",
        _ => throw new InvalidOperationException($"Unknown built-in permission category for '{id}'."),
    };

    private static int CategoryOrder(string category) => category switch
    {
        "Server & Monitoring" => 1,
        "Library" => 2,
        "Playback" => 3,
        "Analytics" => 4,
        "Metadata" => 5,
        "Providers" => 6,
        "Ingestion" => 7,
        "Review" => 8,
        "Collections" => 9,
        "View" => 10,
        "Identity" => 11,
        "Plugins" => 12,
        "AI" => 13,
        "Network" => 14,
        "Storage" => 15,
        "Backup" => 16,
        "Events" => 17,
        _ => 99,
    };

    private static PermissionRiskLevel RiskFor(ApplicationPermissionId id)
    {
        if (id == ApplicationPermissionIds.ViewPersonalRead
            || id == ApplicationPermissionIds.ViewOriginalsRead
            || id == ApplicationPermissionIds.AiInfer)
        {
            return PermissionRiskLevel.Sensitive;
        }

        if (id == ApplicationPermissionIds.MetadataMatch
            || id == ApplicationPermissionIds.IngestionRetry
            || id == ApplicationPermissionIds.ReviewResolve)
        {
            return PermissionRiskLevel.Write;
        }

        if (id.Value is "identity.users.write" or "identity.applications.write" or "plugins.manage" or
            "ai.manage" or "network.config.write" or "storage.config.write" or "backup.restore")
        {
            return PermissionRiskLevel.Administrative;
        }

        return id.Value.EndsWith(".write", StringComparison.Ordinal) || id.Value.EndsWith(".run", StringComparison.Ordinal) ||
               id.Value.EndsWith(".cancel", StringComparison.Ordinal) || id.Value.EndsWith(".control", StringComparison.Ordinal) ||
               id == ApplicationPermissionIds.ViewUpload
            ? PermissionRiskLevel.Write
            : PermissionRiskLevel.Read;
    }

    private static bool RequiresUserContext(ApplicationPermissionId id) =>
        id.Value.StartsWith("view.personal", StringComparison.Ordinal) ||
        id == ApplicationPermissionIds.ViewOriginalsRead || id == ApplicationPermissionIds.ViewUpload ||
        id.Value.StartsWith("view.galleries", StringComparison.Ordinal);

    private static string DisplayNameFor(ApplicationPermissionId id) =>
        string.Join(' ', id.Value.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string DescriptionFor(ApplicationPermissionId id) =>
        $"Allows the application to use the {id.Value} service when the resource and caller context are also authorized.";
}
