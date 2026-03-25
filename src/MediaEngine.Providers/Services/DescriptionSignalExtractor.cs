using System.Text.RegularExpressions;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Extracts person names from retail provider description text and file metadata
/// using config-driven regex patterns. Runs inline during hydration with zero
/// API calls — records pending signals in <c>pending_person_signals</c> for
/// later batch Wikidata verification.
///
/// <para>
/// Behaviour is entirely config-driven via <c>config/signal_extraction.json</c>.
/// Each media category defines extraction rules with regex patterns, target source
/// fields (description, copyright), and file metadata keys to check.
/// </para>
///
/// Spec: §3.13 (Two-Stage Hydration Pipeline) — signal extraction runs as part of
/// the inline Stage 1 pass before any Wikidata API calls are made.
/// </summary>
public sealed class DescriptionSignalExtractor : IDescriptionSignalExtractor
{
    private readonly IConfigurationLoader _configLoader;
    private readonly IPendingPersonSignalRepository _pendingRepo;
    private readonly ILogger<DescriptionSignalExtractor> _logger;

    // Cache compiled regexes per pattern string to avoid repeated compilation.
    private readonly Dictionary<string, Regex> _regexCache = new();

    public DescriptionSignalExtractor(
        IConfigurationLoader configLoader,
        IPendingPersonSignalRepository pendingRepo,
        ILogger<DescriptionSignalExtractor> logger)
    {
        _configLoader = configLoader;
        _pendingRepo  = pendingRepo;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExtractedPersonSignal>> ExtractAndDepositAsync(
        Guid entityId,
        string mediaType,
        IReadOnlyList<MetadataClaim> rawClaims,
        IReadOnlyList<CanonicalValue> canonicals,
        IReadOnlyDictionary<string, string>? fileHints = null,
        CancellationToken ct = default)
    {
        // 1. Load config — fall back to empty result if config is missing or disabled.
        SignalExtractionSettings? settings = null;
        try
        {
            settings = _configLoader.LoadConfig<SignalExtractionSettings>("", "signal_extraction");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DescriptionSignalExtractor: failed to load config — skipping extraction.");
        }

        if (settings is null || !settings.GlobalSettings.Enabled)
            return [];

        // 2. Find the matching category for this media type.
        var categoryKey = MapMediaTypeToCategory(mediaType);
        if (categoryKey is null || !settings.Categories.TryGetValue(categoryKey, out var category))
        {
            _logger.LogDebug(
                "DescriptionSignalExtractor: no category config for media type '{MediaType}' — skipping.",
                mediaType);
            return [];
        }

        var globals  = settings.GlobalSettings;
        var signals  = new List<ExtractedPersonSignal>();

        // 3. Process each extraction rule in the category.
        foreach (var rule in category.ExtractionRules)
        {
            // 3a. Extract from description/copyright source fields.
            foreach (var sourceField in rule.SourceFields)
            {
                var text = GetFieldValue(sourceField, rawClaims, canonicals);
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Truncate to configured max length before running regex.
                if (text.Length > globals.DescriptionMaxChars)
                    text = text[..globals.DescriptionMaxChars];

                foreach (var pattern in rule.Patterns)
                {
                    Regex regex;
                    try
                    {
                        regex = GetOrCompileRegex(pattern);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "DescriptionSignalExtractor: invalid regex pattern '{Pattern}' — skipping.",
                            pattern);
                        continue;
                    }

                    MatchCollection matches;
                    try
                    {
                        matches = regex.Matches(text);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        _logger.LogDebug(
                            "DescriptionSignalExtractor: regex timeout on pattern '{Pattern}' for entity {EntityId}.",
                            pattern, entityId);
                        continue;
                    }

                    foreach (Match match in matches)
                    {
                        var nameGroup = match.Groups["name"];
                        if (!nameGroup.Success) continue;

                        var rawNames = SplitNames(nameGroup.Value.Trim(), globals.NameSeparators);
                        foreach (var name in rawNames)
                        {
                            if (!ValidateName(name, globals)) continue;

                            // Avoid duplicates within this extraction pass.
                            if (signals.Any(s =>
                                    string.Equals(s.Role, rule.Role, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            signals.Add(new ExtractedPersonSignal(
                                name,
                                rule.Role,
                                sourceField,
                                pattern,
                                globals.ConfidenceFromDescription));
                        }
                    }
                }
            }

            // 3b. Extract from file metadata hints.
            if (fileHints is not null)
            {
                foreach (var key in rule.FileMetadataKeys)
                {
                    if (!fileHints.TryGetValue(key, out var hintValue) ||
                        string.IsNullOrWhiteSpace(hintValue))
                        continue;

                    var names = SplitNames(hintValue.Trim(), globals.NameSeparators);
                    foreach (var name in names)
                    {
                        if (!ValidateName(name, globals)) continue;

                        // Skip if already extracted from description fields.
                        if (signals.Any(s =>
                                string.Equals(s.Role, rule.Role, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        signals.Add(new ExtractedPersonSignal(
                            name,
                            rule.Role,
                            "file_metadata",
                            null,
                            globals.ConfidenceFromFileMetadata));
                    }
                }
            }

            // Cap total extractions per entity across all rules.
            if (signals.Count >= globals.MaxExtractionsPerEntity)
            {
                signals = [.. signals.Take(globals.MaxExtractionsPerEntity)];
                break;
            }
        }

        if (signals.Count == 0) return signals;

        // 4. Record all extracted signals as pending for batch Wikidata verification.
        //    Claims are NOT deposited here — that happens during batch verification
        //    once the QID is confirmed.
        var pendingSignals = signals.Select(s => new PendingPersonSignal
        {
            Id        = Guid.NewGuid(),
            EntityId  = entityId,
            Name      = s.Name,
            Role      = s.Role,
            Source    = s.Source,
            Pattern   = s.Pattern,
            MediaType = mediaType,
            CreatedAt = DateTime.UtcNow.ToString("o"),
        }).ToList();

        await _pendingRepo.InsertBatchAsync(pendingSignals, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "DescriptionSignalExtractor: deposited {Count} pending person signal(s) " +
            "from {MediaType} entity {EntityId}.",
            signals.Count, mediaType, entityId);

        return signals;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a media type string to the category key used in the config file.
    /// </summary>
    private static string? MapMediaTypeToCategory(string mediaType) =>
        mediaType switch
        {
            "Audiobook"             => "Audiobooks",
            "Epub"                  => "Books",
            "Movies" or "Movie"     => "Movies",
            "TV"                    => "TV",
            "Comics" or "Comic"     => "Comics",
            "Podcasts" or "Podcast" => "Podcasts",
            "Music"                 => "Music",
            _                       => null,
        };

    /// <summary>
    /// Reads a field value from canonicals first (resolved values), then raw claims.
    /// </summary>
    private static string? GetFieldValue(
        string fieldName,
        IReadOnlyList<MetadataClaim> claims,
        IReadOnlyList<CanonicalValue> canonicals)
    {
        // CanonicalValue uses .Key and .Value (as confirmed from CanonicalValue.cs).
        var canonical = canonicals.FirstOrDefault(c =>
            string.Equals(c.Key, fieldName, StringComparison.OrdinalIgnoreCase));
        if (canonical is not null && !string.IsNullOrWhiteSpace(canonical.Value))
            return canonical.Value;

        // MetadataClaim uses .ClaimKey and .ClaimValue (as confirmed from MetadataClaim.cs).
        var claim = claims.FirstOrDefault(c =>
            string.Equals(c.ClaimKey, fieldName, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(claim?.ClaimValue) ? null : claim.ClaimValue;
    }

    /// <summary>
    /// Returns a compiled <see cref="Regex"/> for the given pattern, creating and
    /// caching it on first use.
    /// </summary>
    private Regex GetOrCompileRegex(string pattern)
    {
        if (!_regexCache.TryGetValue(pattern, out var regex))
        {
            regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
            _regexCache[pattern] = regex;
        }
        return regex;
    }

    /// <summary>
    /// Splits a raw name string on configured separators, returning distinct
    /// trimmed parts.
    /// </summary>
    private static IReadOnlyList<string> SplitNames(
        string rawName,
        IReadOnlyList<string> separators)
    {
        var names = new List<string> { rawName };

        foreach (var sep in separators)
        {
            var expanded = new List<string>();
            foreach (var n in names)
            {
                var parts = n.Split(sep,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                expanded.AddRange(parts);
            }
            names = expanded;
        }

        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="name"/> passes all
    /// validation rules defined in <paramref name="globals"/>.
    /// </summary>
    private static bool ValidateName(string name, SignalExtractionGlobalSettings globals)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Length cap.
        if (name.Length > globals.MaxNameLength) return false;

        // Minimum word count (filters single-word blobs like "Audible").
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < globals.MinNameWords) return false;

        // Must start with an uppercase letter (filters sentence fragments).
        if (!char.IsUpper(name[0])) return false;

        // No digits (filters strings like "Season 3", "Volume 2", "Act 1").
        if (name.Any(char.IsDigit)) return false;

        // Not in the stop-name list.
        if (globals.StopNames.Any(s =>
                string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
