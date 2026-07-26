using System.Text.RegularExpressions;

namespace MediaEngine.Api.Tests;

/// <summary>
/// Guardrail: every HTTP route mapped in <c>src/MediaEngine.Api/Endpoints/*.cs</c> must
/// resolve a role requirement — either via a <c>Require*()</c> call on the route's own
/// chain, or inherited from a <c>Require*()</c> call on the file's <c>MapGroup(...)</c>
/// declaration. <see cref="Security.RoleAuthorizationFilter"/> otherwise leaves the route
/// reachable by any authenticated role (including Consumer), which is the exact gap this
/// packet closes. This is a source scan (mirroring <see cref="Phase7AiEndpointGuardrailTests"/>
/// and <see cref="ProblemDetailsGuardrailTests"/>) rather than a hosted-server enumeration,
/// since this test project does not boot the real Engine host.
/// </summary>
public sealed class RouteAuthorizationGuardrailTests
{
    /// <summary>
    /// Routes intentionally reachable without a role filter. Keep this list to documented
    /// exceptions only — every other mapped route must carry a Require*() guard. See
    /// docs/architecture/security.md for the rationale behind each entry.
    /// </summary>
    private static readonly HashSet<string> AllowlistedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        // ApiKeyMiddleware exempt path — used by external apps to test connectivity
        // before they have an API key. Deliberately left unguarded (see SystemEndpoints.cs).
        "/system/status",
        // ASP.NET Core health check endpoint, mapped directly in Program.cs (not in
        // src/MediaEngine.Api/Endpoints), so it never appears in this scan today — listed
        // defensively in case a health route is ever added under Endpoints/.
        "/health",
    };

    /// <summary>
    /// Path prefixes that never require a role filter because they are framework-owned
    /// surfaces (Swagger UI/JSON), not application endpoints defined under Endpoints/.
    /// </summary>
    private static readonly string[] AllowlistedPrefixes = ["/swagger"];

    /// <summary>
    /// Receiver identifiers used across Endpoints/*.cs for route registration: the raw
    /// <c>IEndpointRouteBuilder</c> parameter (named <c>app</c> or <c>routes</c>
    /// depending on the file) and the two conventional <c>MapGroup(...)</c> result
    /// variable names (<c>group</c>, <c>grp</c>). A route chained on any other
    /// identifier would not be recognised by this scan.
    /// </summary>
    private static readonly string[] ConventionalReceivers = ["app", "routes", "group", "grp"];

    private static readonly Regex GroupDeclarationRegex = new(
        @"var\s+(?<var>\w+)\s*=\s*(?:app|routes)\.MapGroup\(",
        RegexOptions.Compiled);

    private static readonly Regex MapCallRegex = new(
        @"(?<recv>\w+)\.Map(?:Get|Post|Put|Delete|Patch)\(\s*""(?<path>[^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex RequireCallRegex = new(
        @"\.Require(?:Admin|AdminOrCurator|AnyRole)\(\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryMappedRoute_ResolvesARoleRequirementGuard()
    {
        var repoRoot = FindRepoRoot();
        var endpointsDir = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var files = Directory.GetFiles(endpointsDir, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.True(files.Length > 30, $"Expected the Endpoints directory to contain the full endpoint file set, found only {files.Length}.");

        var gaps = new List<string>();

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var source = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Receivers a Map(Get|Post|Put|Delete|Patch) call may legally chain off in
            // this file: the conventional set, plus any locally declared group variable
            // (covers the rare case a file picks a non-conventional name).
            var recognisedReceivers = new HashSet<string>(ConventionalReceivers, StringComparer.Ordinal);

            // Group variables whose MapGroup(...) declaration statement itself carries a
            // Require*() call — every route chained on that variable inherits the guard.
            var guardedGroupVars = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match groupMatch in GroupDeclarationRegex.Matches(source))
            {
                var varName = groupMatch.Groups["var"].Value;
                recognisedReceivers.Add(varName);

                var stmtEnd = source.IndexOf(';', groupMatch.Index);
                if (stmtEnd < 0)
                {
                    stmtEnd = source.Length;
                }

                var declarationStatement = source[groupMatch.Index..stmtEnd];
                if (RequireCallRegex.IsMatch(declarationStatement))
                {
                    guardedGroupVars.Add(varName);
                }
            }

            var mapMatches = MapCallRegex.Matches(source)
                .Where(m => recognisedReceivers.Contains(m.Groups["recv"].Value))
                .OrderBy(m => m.Index)
                .ToList();

            for (var i = 0; i < mapMatches.Count; i++)
            {
                var match = mapMatches[i];
                var receiver = match.Groups["recv"].Value;
                var routePath = match.Groups["path"].Value;

                if (guardedGroupVars.Contains(receiver) || IsAllowlisted(routePath))
                {
                    continue;
                }

                // The route's own chain runs from this Map call to the start of the next
                // one (or end of file for the last route in the method).
                var chainEnd = i + 1 < mapMatches.Count ? mapMatches[i + 1].Index : source.Length;
                var chain = source[match.Index..chainEnd];

                if (!RequireCallRegex.IsMatch(chain))
                {
                    gaps.Add($"{fileName}: \"{routePath}\" (receiver '{receiver}')");
                }
            }
        }

        Assert.True(gaps.Count == 0,
            "The following routes have no role requirement guard. Add .RequireAdmin() / " +
            ".RequireAdminOrCurator() / .RequireAnyRole() to the route or its MapGroup(...) " +
            "declaration, or — only for a deliberately public route — add it to the allowlist " +
            "in RouteAuthorizationGuardrailTests:\n" + string.Join("\n", gaps));
    }

    /// <summary>
    /// Every Require*() extension must also attach <see cref="Security.RoleRequirementMetadata"/>
    /// so the role requirement is discoverable from endpoint metadata, not just from the
    /// filter pipeline. Guards against a future edit that adds a new overload but forgets
    /// the metadata call.
    /// </summary>
    [Fact]
    public void RoleFilterExtensions_ExposeGroupOverloadsAndAttachMetadata()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Api", "Security", "RoleAuthorizationFilter.cs"));

        Assert.Contains("public sealed record RoleRequirementMetadata(IReadOnlyList<string> Roles);", source, StringComparison.Ordinal);

        Assert.Contains("public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder)", source, StringComparison.Ordinal);
        Assert.Contains("public static RouteHandlerBuilder RequireAdminOrCurator(this RouteHandlerBuilder builder)", source, StringComparison.Ordinal);
        Assert.Contains("public static RouteHandlerBuilder RequireAnyRole(this RouteHandlerBuilder builder)", source, StringComparison.Ordinal);
        Assert.Contains("public static RouteGroupBuilder RequireAdmin(this RouteGroupBuilder builder)", source, StringComparison.Ordinal);
        Assert.Contains("public static RouteGroupBuilder RequireAdminOrCurator(this RouteGroupBuilder builder)", source, StringComparison.Ordinal);
        Assert.Contains("public static RouteGroupBuilder RequireAnyRole(this RouteGroupBuilder builder)", source, StringComparison.Ordinal);

        var metadataAttachmentCount = Regex.Matches(source, @"\.WithMetadata\(new RoleRequirementMetadata\(").Count;
        Assert.True(metadataAttachmentCount == 6,
            $"Expected all 6 Require*() extensions (3 RouteHandlerBuilder + 3 RouteGroupBuilder) to attach RoleRequirementMetadata, found {metadataAttachmentCount}.");
    }

    private static bool IsAllowlisted(string routePath)
    {
        if (AllowlistedRoutes.Contains(routePath))
        {
            return true;
        }

        foreach (var prefix in AllowlistedPrefixes)
        {
            if (routePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
