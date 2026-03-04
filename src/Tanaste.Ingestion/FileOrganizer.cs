using Microsoft.Extensions.Logging;
using Tanaste.Domain.Enums;
using Tanaste.Ingestion.Contracts;
using Tanaste.Ingestion.Models;

namespace Tanaste.Ingestion;

/// <summary>
/// Calculates organised destination paths from tokenized templates and
/// performs collision-safe, retry-backed file moves.
///
/// ──────────────────────────────────────────────────────────────────
/// Template tokens (spec: Phase 7 – Extension Points § Organization Templates)
/// ──────────────────────────────────────────────────────────────────
///  Tokens are surrounded by curly braces and resolved from the candidate's
///  claims at the time of organization.  Unresolved tokens are replaced with
///  the literal string "Unknown".
///
///  Built-in tokens:
///   {Title}      — title claim (confidence-winner)
///   {Author}     — author claim
///   {Year}       — year claim (4-digit integer)
///   {MediaType}  — MediaType enum name (Movie, Epub, Comic, …)
///   {Extension}  — file extension WITHOUT leading dot (e.g. "mp4", "epub")
///   {Series}     — series claim
///   {Publisher}  — publisher claim
///
///  Custom tokens may be injected via the <c>IReadOnlyDictionary</c> overload
///  of <see cref="CalculatePath"/>.
///
/// ──────────────────────────────────────────────────────────────────
/// Collision handling
/// ──────────────────────────────────────────────────────────────────
///  When the computed destination already exists, a numeric suffix is appended:
///  <c>Title (2).epub</c>, <c>Title (3).epub</c>, etc.
///  Spec: "If a naming conflict occurs … MUST append a unique suffix."
///
/// ──────────────────────────────────────────────────────────────────
/// Move retry
/// ──────────────────────────────────────────────────────────────────
///  On <see cref="IOException"/>, the move is retried with exponential
///  back-off up to <c>MaxMoveAttempts</c>.
///  Spec: Phase 7 – Lock handling § Retry Exponential Backoff.
/// </summary>
public sealed class FileOrganizer : IFileOrganizer
{
    private const int MaxMoveAttempts = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<FileOrganizer> _logger;

    // Characters that are illegal in file/directory names on Windows and most POSIX systems.
    private static readonly char[] InvalidPathChars =
        Path.GetInvalidFileNameChars()
            .Concat(Path.GetInvalidPathChars())
            .Distinct()
            .ToArray();

    public FileOrganizer(ILogger<FileOrganizer> logger)
    {
        _logger = logger;
    }

    // Matches an optional leading space followed by ({Token}) — used for conditional groups.
    private static readonly System.Text.RegularExpressions.Regex ConditionalGroupRegex = new(
        @"\s?\(\{([^}]+)\}\)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches any remaining {Token} references not inside parentheses.
    private static readonly System.Text.RegularExpressions.Regex UnresolvedTokenRegex = new(
        @"\{[^}]+\}",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches two or more consecutive whitespace characters.
    private static readonly System.Text.RegularExpressions.Regex MultiSpaceRegex = new(
        @"\s{2,}",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // -------------------------------------------------------------------------
    // IFileOrganizer
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public string CalculatePath(IngestionCandidate candidate, string template)
        => CalculatePath(candidate, template, extraTokens: null);

    /// <summary>
    /// Overload that accepts additional caller-supplied token values.
    /// </summary>
    public string CalculatePath(
        IngestionCandidate candidate,
        string template,
        IReadOnlyDictionary<string, string>? extraTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(candidate);

        var tokens = BuildTokens(candidate, extraTokens);

        string resolved = ResolveTemplate(template, tokens);

        return resolved;
    }

    /// <inheritdoc/>
    public string? ValidateTemplate(string template, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(template))
        {
            error = "Template cannot be empty.";
            return null;
        }

        // Build sample tokens with representative values.
        var sampleTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"]     = "Sample Book",
            ["Author"]    = "Jane Author",
            ["Year"]      = "2024",
            ["MediaType"] = "Epub",
            ["Extension"] = "epub",
            ["Ext"]       = ".epub",
            ["Series"]    = "Great Series",
            ["Publisher"]  = "Publisher Co",
            ["Category"]  = "Books",
            ["HubName"]   = "Sample Book",
            ["Format"]    = "Epub",
            ["Edition"]   = "Hardcover",
        };

        string resolved = ResolveTemplate(template, sampleTokens);

        // Validate: no empty parentheses.
        if (resolved.Contains("()", StringComparison.Ordinal))
        {
            error = "Template produces empty parentheses '()'. Check your token names.";
            return null;
        }

        // Validate: no double spaces.
        if (resolved.Contains("  ", StringComparison.Ordinal))
        {
            error = "Template produces double spaces. Check token placement.";
            return null;
        }

        // Validate: no consecutive path separators.
        if (resolved.Contains("//", StringComparison.Ordinal)
            || resolved.Contains("\\\\", StringComparison.Ordinal))
        {
            error = "Template produces consecutive path separators.";
            return null;
        }

        return resolved;
    }

    /// <inheritdoc/>
    public async Task<bool> ExecuteMoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Move skipped — source file not found: {Source}", sourcePath);
            return false;
        }

        // Ensure the destination directory exists.
        string? destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        // Resolve collision: if destination exists, find a free name.
        string finalDest = ResolveCollision(destinationPath);

        var delay = InitialRetryDelay;
        for (int attempt = 1; attempt <= MaxMoveAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Move(sourcePath, finalDest);
                _logger.LogInformation("Moved {Source} → {Destination}", sourcePath, finalDest);
                return true;
            }
            catch (IOException ex) when (attempt < MaxMoveAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Move attempt {Attempt}/{Max} failed for {Source}; retrying in {Delay}ms.",
                    attempt, MaxMoveAttempts, sourcePath, (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay *= 2; // exponential back-off
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move {Source} → {Destination}.", sourcePath, finalDest);
                return false;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Template resolution engine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Three-pass conditional template resolution:
    /// 1. Resolve conditional groups: <c> ({Token})</c> — collapse entirely when token is empty.
    /// 2. Resolve remaining bare <c>{Token}</c> references via standard replacement.
    /// 3. Cleanup: collapse multiple spaces, trim each path segment.
    /// </summary>
    private static string ResolveTemplate(string template, Dictionary<string, string> tokens)
    {
        // Pass 1 — Conditional groups: ` ({Token})` or `({Token})`
        // If the token value is empty/whitespace, remove the entire group (incl. leading space).
        // If the token has content, replace with ` (Value)` preserving the leading space.
        string resolved = ConditionalGroupRegex.Replace(template, match =>
        {
            string tokenName = match.Groups[1].Value;
            bool hasLeadingSpace = match.Value.Length > 0 && char.IsWhiteSpace(match.Value[0]);

            if (tokens.TryGetValue(tokenName, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                string sanitized = Sanitize(value);
                if (!string.IsNullOrWhiteSpace(sanitized) && sanitized != "Unknown")
                    return hasLeadingSpace ? $" ({sanitized})" : $"({sanitized})";
            }

            // Token is empty/missing — collapse the entire group.
            return string.Empty;
        });

        // Pass 2 — Standard token replacement for remaining {Token} references.
        // For bare (non-conditional) tokens, empty values become "Unknown".
        foreach (var (key, value) in tokens)
        {
            string sanitized = Sanitize(value);
            string replacement = string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
            resolved = resolved.Replace($"{{{key}}}", replacement, StringComparison.OrdinalIgnoreCase);
        }

        // Replace any still-unresolved tokens with "Unknown".
        resolved = UnresolvedTokenRegex.Replace(resolved, "Unknown");

        // Pass 3 — Cleanup.
        resolved = MultiSpaceRegex.Replace(resolved, " ");

        // Trim each path segment individually.
        var segments = resolved.Split('/');
        for (int i = 0; i < segments.Length; i++)
            segments[i] = segments[i].Trim();
        resolved = string.Join('/', segments);

        return resolved;
    }

    // -------------------------------------------------------------------------
    // Token resolution
    // -------------------------------------------------------------------------

    private static Dictionary<string, string> BuildTokens(
        IngestionCandidate candidate,
        IReadOnlyDictionary<string, string>? extra)
    {
        // candidate.Metadata is a flat KV bag populated by the processor and scorer.
        // We try known claim keys; fall back to "Unknown" for absent keys.
        var meta = candidate.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ext = Path.GetExtension(candidate.Path); // includes the dot: ".epub"

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"]     = meta.GetValueOrDefault("title",     "Unknown"),
            ["Author"]    = meta.GetValueOrDefault("author",    "Unknown"),
            ["Year"]      = meta.GetValueOrDefault("year",      string.Empty),
            ["MediaType"] = candidate.DetectedMediaType?.ToString() ?? "Unknown",
            ["Extension"] = ext.TrimStart('.'),
            ["Ext"]       = ext,     // includes the dot — e.g. ".epub"
            ["Series"]    = meta.GetValueOrDefault("series",    "Unknown"),
            ["Publisher"] = meta.GetValueOrDefault("publisher", "Unknown"),
            // ── Hub-First template tokens ────────────────────────────────────────
            ["Category"]  = ResolveCategoryFromMediaType(candidate.DetectedMediaType),
            ["HubName"]   = meta.GetValueOrDefault("title",   "Unknown"),
            ["Format"]    = candidate.DetectedMediaType?.ToString() ?? "Unknown",
            ["Edition"]   = meta.GetValueOrDefault("edition", string.Empty),
        };

        // Merge caller-supplied extras (allow overriding built-ins).
        if (extra is not null)
            foreach (var (k, v) in extra)
                tokens[k] = v;

        return tokens;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Removes characters that are illegal in a path segment.
    /// Collapses multiple spaces and trims the result.
    /// Returns empty string for empty/whitespace input (instead of "Unknown")
    /// so that conditional template groups can detect empty values.
    /// </summary>
    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (Array.IndexOf(InvalidPathChars, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }

        // Collapse sequences of spaces and trim.
        string result = MultiSpaceRegex.Replace(sb.ToString(), " ").Trim();
        return result;
    }

    /// <summary>
    /// Maps a <see cref="MediaType"/> to a broad human-readable category
    /// used as the top-level directory in the Hub-first organisation template.
    /// </summary>
    private static string ResolveCategoryFromMediaType(MediaType? mt) => mt switch
    {
        MediaType.Epub      => "Books",
        MediaType.Comic     => "Comics",
        MediaType.Movie     => "Videos",
        MediaType.TvShow    => "TV Shows",
        MediaType.Audiobook => "Audio",
        MediaType.Music     => "Music",
        MediaType.Podcast   => "Podcasts",
        _                   => "Other",  // Unknown, null — caught by upstream guard
    };

    /// <summary>
    /// If <paramref name="path"/> does not exist, returns it unchanged.
    /// Otherwise appends " (2)", " (3)", … until a free path is found.
    /// </summary>
    private static string ResolveCollision(string path)
    {
        if (!File.Exists(path)) return path;

        string dir  = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext  = Path.GetExtension(path);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        // Extremely unlikely: fall back to a GUID suffix.
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }
}
