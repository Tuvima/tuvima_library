using System.Xml.Linq;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Reads and writes sidecar XML files for editions and persons using
/// <see cref="System.Xml.Linq.XDocument"/> (BCL — no extra NuGet dependency).
///
/// <para>
/// Two XML schemas are produced:
/// <list type="bullet">
///   <item>Edition-level: <c>&lt;library-edition version="1.2"&gt;</c></item>
///   <item>Person-level: <c>&lt;library-person version="1.0"&gt;</c></item>
/// </list>
/// </para>
///
/// <para>
/// Hub-level sidecars have been dropped — edition + universe + person sidecars
/// contain all recoverable data.  Hubs are reconstructed from edition sidecars
/// during Great Inhale.
/// </para>
///
/// Sidecar files are always placed directly inside the folder they describe.
/// </summary>
public sealed class SidecarWriter : ISidecarWriter
{
    private const string FileName       = "library.xml";
    private const string PersonFileName = "person.xml";
    private const string EdRootName     = "library-edition";
    private const string PersonRootName = "library-person";
    private const string Version        = "2.0";


    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task WriteEditionSidecarAsync(
        string             editionFolderPath,
        EditionSidecarData data,
        CancellationToken  ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(editionFolderPath);

        var userLockElements = data.UserLocks.Select(ul =>
            new XElement("claim",
                new XAttribute("key",       ul.Key),
                new XAttribute("value",     ul.Value),
                new XAttribute("locked-at", ul.LockedAt.ToString("O"))
            ));

        var canonicalElements = new List<XElement>();

        // Single-valued canonicals (skip keys that have multi-valued entries).
        var multiKeys = new HashSet<string>(
            data.MultiValuedCanonicals.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in data.CanonicalValues
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (multiKeys.Contains(kv.Key))
                continue; // Written as multi-valued below.

            canonicalElements.Add(new XElement("value",
                new XAttribute("key",   kv.Key),
                new XAttribute("value", kv.Value)));
        }

        // Multi-valued canonicals with values/qids attributes.
        foreach (var mv in data.MultiValuedCanonicals
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var el = new XElement("value",
                new XAttribute("key",    mv.Key),
                new XAttribute("values", string.Join(";", mv.Value.Values)));

            if (mv.Value.Qids.Length > 0)
                el.Add(new XAttribute("qids", string.Join(";", mv.Value.Qids)));

            canonicalElements.Add(el);
        }

        // Build identity element with QID attributes.
        var titleEl = new XElement("title", data.Title ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(data.TitleQid))
            titleEl.Add(new XAttribute("qid", data.TitleQid));

        var authorEl = new XElement("author", data.Author ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(data.AuthorQid))
            authorEl.Add(new XAttribute("qid", data.AuthorQid));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(EdRootName,
                new XAttribute("version", Version),
                new XElement("identity",
                    titleEl,
                    authorEl,
                    new XElement("media-type",   data.MediaType   ?? string.Empty),
                    new XElement("isbn",         data.Isbn        ?? string.Empty),
                    new XElement("asin",         data.Asin        ?? string.Empty),
                    new XElement("wikidata-qid", data.WikidataQid ?? string.Empty)
                ),
                new XElement("content-hash",     data.ContentHash),
                new XElement("cover-path",       data.CoverPath),
                new XElement("user-locks",       userLockElements),
                new XElement("canonical-values", canonicalElements),
                new XElement("last-organized",   data.LastOrganized.ToString("O"))
            )
        );

        doc.Save(Path.Combine(editionFolderPath, FileName));
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<EditionSidecarData?> ReadEditionSidecarAsync(
        string xmlPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var doc  = XDocument.Load(xmlPath);
            var root = doc.Root;
            if (root?.Name.LocalName != EdRootName)
                return Task.FromResult<EditionSidecarData?>(null);

            var identity  = root.Element("identity");
            var userLocks = root.Element("user-locks")
                ?.Elements("claim")
                .Select(e => new UserLockedClaim
                {
                    Key      = e.Attribute("key")?.Value       ?? string.Empty,
                    Value    = e.Attribute("value")?.Value     ?? string.Empty,
                    LockedAt = ParseDateOffset(e.Attribute("locked-at")?.Value),
                })
                .ToList()
                ?? [];

            var (canonicalValues, multiValuedCanonicals) =
                ParseCanonicalValuesV2(root.Element("canonical-values"));

            var result = new EditionSidecarData
            {
                Title                  = NullIfEmpty(identity?.Element("title")?.Value),
                TitleQid               = NullIfEmpty(identity?.Element("title")?.Attribute("qid")?.Value),
                Author                 = NullIfEmpty(identity?.Element("author")?.Value),
                AuthorQid              = NullIfEmpty(identity?.Element("author")?.Attribute("qid")?.Value),
                MediaType              = NullIfEmpty(identity?.Element("media-type")?.Value),
                Isbn                   = NullIfEmpty(identity?.Element("isbn")?.Value),
                Asin                   = NullIfEmpty(identity?.Element("asin")?.Value),
                WikidataQid            = NullIfEmpty(identity?.Element("wikidata-qid")?.Value),
                ContentHash            = root.Element("content-hash")?.Value   ?? string.Empty,
                CoverPath              = root.Element("cover-path")?.Value      ?? "cover.jpg",
                UserLocks              = userLocks,
                CanonicalValues        = canonicalValues,
                MultiValuedCanonicals  = multiValuedCanonicals,
                LastOrganized          = ParseDateOffset(root.Element("last-organized")?.Value),
            };

            return Task.FromResult<EditionSidecarData?>(result);
        }
        catch
        {
            return Task.FromResult<EditionSidecarData?>(null);
        }
    }

    /// <inheritdoc/>
    public Task WritePersonSidecarAsync(
        string personFolderPath,
        PersonSidecarData data,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(personFolderPath);

        var pseudonymElements = data.Pseudonyms.Select(p =>
            new XElement("person",
                new XAttribute("qid",  p.Qid),
                new XAttribute("name", p.Name)));

        var realIdentityElements = data.RealIdentities.Select(r =>
            new XElement("person",
                new XAttribute("qid",  r.Qid),
                new XAttribute("name", r.Name)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(PersonRootName,
                new XAttribute("version", "1.1"),
                new XElement("identity",
                    new XElement("name",          data.Name),
                    new XElement("role",          data.Role),
                    new XElement("wikidata-qid",  data.WikidataQid ?? string.Empty),
                    new XElement("occupation",    data.Occupation  ?? string.Empty),
                    new XElement("is-pseudonym",  data.IsPseudonym ? "true" : "false")
                ),
                new XElement("biography", data.Biography ?? string.Empty),
                new XElement("social",
                    new XElement("instagram", data.Instagram ?? string.Empty),
                    new XElement("twitter",   data.Twitter   ?? string.Empty),
                    new XElement("tiktok",    data.TikTok    ?? string.Empty),
                    new XElement("mastodon",  data.Mastodon  ?? string.Empty),
                    new XElement("website",   data.Website   ?? string.Empty)
                ),
                new XElement("pseudonyms",      pseudonymElements),
                new XElement("real-identities", realIdentityElements)
            )
        );

        doc.Save(Path.Combine(personFolderPath, PersonFileName));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<PersonSidecarData?> ReadPersonSidecarAsync(
        string xmlPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var doc  = XDocument.Load(xmlPath);
            var root = doc.Root;
            if (root?.Name.LocalName != PersonRootName)
                return Task.FromResult<PersonSidecarData?>(null);

            var identity = root.Element("identity");
            var social   = root.Element("social");

            var isPseudonymStr = identity?.Element("is-pseudonym")?.Value;

            var pseudonyms = root.Element("pseudonyms")?.Elements("person")
                .Select(e => new PersonSidecarRef
                {
                    Qid  = e.Attribute("qid")?.Value  ?? string.Empty,
                    Name = e.Attribute("name")?.Value ?? string.Empty,
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Qid))
                .ToList()
                ?? [];

            var realIdentities = root.Element("real-identities")?.Elements("person")
                .Select(e => new PersonSidecarRef
                {
                    Qid  = e.Attribute("qid")?.Value  ?? string.Empty,
                    Name = e.Attribute("name")?.Value ?? string.Empty,
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Qid))
                .ToList()
                ?? [];

            var result = new PersonSidecarData
            {
                Name           = identity?.Element("name")?.Value         ?? string.Empty,
                Role           = identity?.Element("role")?.Value         ?? string.Empty,
                WikidataQid    = NullIfEmpty(identity?.Element("wikidata-qid")?.Value),
                Occupation     = NullIfEmpty(identity?.Element("occupation")?.Value),
                IsPseudonym    = string.Equals(isPseudonymStr, "true", StringComparison.OrdinalIgnoreCase),
                Biography      = NullIfEmpty(root.Element("biography")?.Value),
                Instagram      = NullIfEmpty(social?.Element("instagram")?.Value),
                Twitter        = NullIfEmpty(social?.Element("twitter")?.Value),
                TikTok         = NullIfEmpty(social?.Element("tiktok")?.Value),
                Mastodon       = NullIfEmpty(social?.Element("mastodon")?.Value),
                Website        = NullIfEmpty(social?.Element("website")?.Value),
                Pseudonyms     = pseudonyms,
                RealIdentities = realIdentities,
            };

            return Task.FromResult<PersonSidecarData?>(result);
        }
        catch
        {
            return Task.FromResult<PersonSidecarData?>(null);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the v2.0 <c>&lt;canonical-values&gt;</c> section.
    /// Single-valued entries have a <c>value</c> attribute.
    /// Multi-valued entries have <c>values</c> and optional <c>qids</c> attributes
    /// (semicolon-separated).
    /// </summary>
    private static (IReadOnlyDictionary<string, string> Single,
                    IReadOnlyDictionary<string, MultiValuedCanonical> Multi)
        ParseCanonicalValuesV2(XElement? element)
    {
        var single = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var multi  = new Dictionary<string, MultiValuedCanonical>(StringComparer.OrdinalIgnoreCase);

        if (element is null)
            return (single, multi);

        foreach (var v in element.Elements("value"))
        {
            var key = v.Attribute("key")?.Value;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var valuesAttr = v.Attribute("values")?.Value;
            if (valuesAttr is not null)
            {
                // Multi-valued entry.
                var values = valuesAttr.Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var qidsAttr = v.Attribute("qids")?.Value;
                var qids = qidsAttr?.Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? [];

                multi[key] = new MultiValuedCanonical { Values = values, Qids = qids };
            }
            else
            {
                // Single-valued entry.
                var value = v.Attribute("value")?.Value;
                if (value is not null)
                    single[key] = value;
            }
        }

        return (single, multi);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset ParseDateOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.UtcNow;

        return DateTimeOffset.TryParse(value, out var result) ? result : DateTimeOffset.UtcNow;
    }
}
