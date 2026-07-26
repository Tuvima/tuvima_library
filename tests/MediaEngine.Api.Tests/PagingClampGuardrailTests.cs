using System.Text.RegularExpressions;

namespace MediaEngine.Api.Tests;

/// <summary>
/// Guards against unbounded pagination: an endpoint handler that accepts a raw
/// <c>int? limit</c>/<c>int? offset</c> (or non-nullable <c>int limit</c>/<c>int offset</c>)
/// query parameter must clamp it through <c>PagedRequest.From</c> before using it, so a
/// caller cannot force an unbounded read against local SQLite via an oversized limit.
/// </summary>
public sealed class PagingClampGuardrailTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly Regex MapCallRegex = new(
        @"\.Map(?:Get|Post|Put|Delete|Methods)\s*\(\s*""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex GroupPrefixRegex = new(
        @"MapGroup\(\s*""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex RawPagingParamRegex = new(
        @"\bint\??\s+(?:limit|offset)\b",
        RegexOptions.Compiled);

    [Fact]
    public void EndpointHandlers_WithRawLimitOrOffset_ClampThroughPagedRequest()
    {
        var allowlist = ReadAllowlist();
        var endpointDir = Path.Combine(RepoRoot, "src", "MediaEngine.Api", "Endpoints");

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(endpointDir, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = ToRelativePath(path);

            // Dev-only harnesses and debug endpoints are not part of the production
            // request surface and are excluded from this scan.
            if (relativePath.Contains("/DevSupport/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Path.GetFileName(path).Contains("Debug", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(path);
            var groupPrefixMatch = GroupPrefixRegex.Match(text);
            var groupPrefix = groupPrefixMatch.Success ? groupPrefixMatch.Groups[1].Value : string.Empty;

            var mapMatches = MapCallRegex.Matches(text);
            for (var i = 0; i < mapMatches.Count; i++)
            {
                var current = mapMatches[i];
                var chunkStart = current.Index;
                var chunkEnd = i + 1 < mapMatches.Count ? mapMatches[i + 1].Index : text.Length;
                var chunk = text.Substring(chunkStart, chunkEnd - chunkStart);

                // Only the parameter list (before the handler's own lambda arrow) counts
                // as "the handler declares a raw limit/offset parameter" — this keeps
                // unrelated later body code (or a trailing private helper method with its
                // own `int limit` parameter) from producing a false match.
                var arrowIndex = chunk.IndexOf("=>", StringComparison.Ordinal);
                var paramSection = arrowIndex >= 0 ? chunk[..arrowIndex] : chunk;

                if (!RawPagingParamRegex.IsMatch(paramSection))
                    continue;

                if (chunk.Contains("PagedRequest.From", StringComparison.Ordinal))
                    continue;

                var route = CombineRoute(groupPrefix, current.Groups[1].Value);
                var key = $"{relativePath}:{route}";
                if (allowlist.Contains(key))
                    continue;

                offenders.Add(key);
            }
        }

        Assert.Empty(offenders);
    }

    private static HashSet<string> ReadAllowlist()
    {
        var path = Path.Combine(RepoRoot, "tests", "MediaEngine.Api.Tests", "PagingClampGuardrailAllowlist.txt");
        Assert.True(File.Exists(path), "Missing paging clamp guardrail allowlist.");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CombineRoute(string prefix, string segment)
    {
        var trimmedPrefix = prefix.TrimEnd('/');
        var trimmedSegment = segment.TrimStart('/');
        if (trimmedSegment.Length == 0)
            return trimmedPrefix.Length == 0 ? "/" : trimmedPrefix;

        return trimmedPrefix.Length == 0 ? "/" + trimmedSegment : trimmedPrefix + "/" + trimmedSegment;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
}
