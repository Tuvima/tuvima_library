namespace MediaEngine.Domain.Authorization;

public enum PrincipalKind
{
    Anonymous,
    Human,
    DelegatedUserClient,
    ServiceApplication,
    DashboardTransport,
    Setup,
    HostRecovery,
}

public enum PermissionRiskLevel { Read, Write, Sensitive, Administrative }
public enum PermissionAvailability { Available, Unavailable }
public enum ApplicationType { UserClient, ServerIntegration, Automation, Other }
public enum PermissionProvenance { BuiltIn, Plugin }
public enum AdminUnlockMode { FixedDuration, UntilProfileSwitch, LockOnLeave }
public enum PrivateResourceAction { ReadMetadata, ReadContent, Create, Update, Delete, Share, Contribute }

public readonly record struct AccountFeatureId
{
    private static readonly HashSet<string> AllValues = new(StringComparer.Ordinal)
    {
        "read", "watch", "listen", "view",
    };

    public static readonly AccountFeatureId Read = new("read");
    public static readonly AccountFeatureId Watch = new("watch");
    public static readonly AccountFeatureId Listen = new("listen");
    public static readonly AccountFeatureId View = new("view");

    public static readonly IReadOnlyList<AccountFeatureId> All = Array.AsReadOnly([Read, Watch, Listen, View]);

    public AccountFeatureId(string value)
    {
        Value = RequireId(value, nameof(value));
        if (!AllValues.Contains(Value))
        {
            throw new ArgumentException($"Unknown account feature '{Value}'.", nameof(value));
        }
    }
    public string Value { get; }
    public override string ToString() => Value;

    private static string RequireId(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("An identifier is required.", parameterName) : value.Trim();
}

public readonly record struct ApplicationPermissionId
{
    public ApplicationPermissionId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A permission identifier is required.", nameof(value))
            : value.Trim();
        if (!IsValid(Value))
        {
            throw new ArgumentException("Permission identifiers must be lowercase dot-separated ASCII segments.", nameof(value));
        }
    }

    public string Value { get; }
    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        if (value[0] == '.' || value[^1] == '.')
        {
            return false;
        }

        var previousWasDot = false;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (previousWasDot)
                {
                    return false;
                }

                previousWasDot = true;
                continue;
            }

            previousWasDot = false;
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
