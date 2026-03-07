using System.Xml.Linq;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Reads and writes <c>library.xml</c> sidecar files using
/// <see cref="System.Xml.Linq.XDocument"/> (BCL — no extra NuGet dependency).
///
/// <para>
/// Two XML schemas are produced:
/// <list type="bullet">
///   <item>Hub-level: <c>&lt;library-hub version="1.1"&gt;</c></item>
///   <item>Edition-level: <c>&lt;library-edition version="1.1"&gt;</c></item>
/// </list>
/// </para>
///
/// Sidecar files are always named <c>library.xml</c> and are placed directly
/// inside the folder they describe.
/// </summary>
public sealed class SidecarWriter : ISidecarWriter
{
    private const string FileName        = "library.xml";
    private const string PersonFileName = "person.xml";
    private const string HubRootName    = "library-hub";
    private const string EdRootName     = "library-edition";
    private const string PersonRootName = "library-person";
    private const string Version        = "1.1";


    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task WriteHubSidecarAsync(
        string         hubFolderPath,
        HubSidecarData data,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(hubFolderPath);

        var bridgeElements = data.Bridges
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new XElement("bridge",
                new XAttribute("key",   kv.Key),
                new XAttribute("value", kv.Value)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(HubRootName,
                new XAttribute("version", Version),
                new XElement("identity",
                    new XElement("display-name", data.DisplayName),
                    new XElement("year",         data.Year         ?? string.Empty),
                    new XElement("wikidata-qid", data.WikidataQid  ?? string.Empty),
                    new XElement("franchise",    data.Franchise    ?? string.Empty)
                ),
                new XElement("bridges", bridgeElements),
                new XElement("universe-status", data.UniverseStatus ?? "Unknown"),
                new XElement("last-organized", data.LastOrganized.ToString("O"))
            )
        );

        doc.Save(Path.Combine(hubFolderPath, FileName));
        return Task.CompletedTask;
    }

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

        var bridgeElements = data.Bridges
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new XElement("bridge",
                new XAttribute("key",   kv.Key),
                new XAttribute("value", kv.Value)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(EdRootName,
                new XAttribute("version", Version),
                new XElement("identity",
                    new XElement("title",      data.Title      ?? string.Empty),
                    new XElement("author",     data.Author     ?? string.Empty),
                    new XElement("media-type", data.MediaType  ?? string.Empty),
                    new XElement("isbn",       data.Isbn       ?? string.Empty),
                    new XElement("asin",       data.Asin       ?? string.Empty)
                ),
                new XElement("content-hash",   data.ContentHash),
                new XElement("cover-path",     data.CoverPath),
                new XElement("user-locks",     userLockElements),
                new XElement("bridges",        bridgeElements),
                new XElement("last-organized", data.LastOrganized.ToString("O"))
            )
        );

        doc.Save(Path.Combine(editionFolderPath, FileName));
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<HubSidecarData?> ReadHubSidecarAsync(
        string xmlPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var doc  = XDocument.Load(xmlPath);
            var root = doc.Root;
            if (root?.Name.LocalName != HubRootName)
                return Task.FromResult<HubSidecarData?>(null);

            var identity = root.Element("identity");
            var bridges  = ParseBridges(root.Element("bridges"));
            var result   = new HubSidecarData
            {
                DisplayName   = identity?.Element("display-name")?.Value ?? string.Empty,
                Year          = NullIfEmpty(identity?.Element("year")?.Value),
                WikidataQid   = NullIfEmpty(identity?.Element("wikidata-qid")?.Value),
                Franchise      = NullIfEmpty(identity?.Element("franchise")?.Value),
                UniverseStatus = root.Element("universe-status")?.Value ?? "Unknown",
                Bridges        = bridges,
                LastOrganized  = ParseDateOffset(root.Element("last-organized")?.Value),
            };

            return Task.FromResult<HubSidecarData?>(result);
        }
        catch
        {
            return Task.FromResult<HubSidecarData?>(null);
        }
    }

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

            var bridges = ParseBridges(root.Element("bridges"));
            var result  = new EditionSidecarData
            {
                Title         = NullIfEmpty(identity?.Element("title")?.Value),
                Author        = NullIfEmpty(identity?.Element("author")?.Value),
                MediaType     = NullIfEmpty(identity?.Element("media-type")?.Value),
                Isbn          = NullIfEmpty(identity?.Element("isbn")?.Value),
                Asin          = NullIfEmpty(identity?.Element("asin")?.Value),
                ContentHash   = root.Element("content-hash")?.Value   ?? string.Empty,
                CoverPath     = root.Element("cover-path")?.Value      ?? "cover.jpg",
                UserLocks     = userLocks,
                Bridges       = bridges,
                LastOrganized = ParseDateOffset(root.Element("last-organized")?.Value),
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

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(PersonRootName,
                new XAttribute("version", "1.0"),
                new XElement("identity",
                    new XElement("name",         data.Name),
                    new XElement("role",         data.Role),
                    new XElement("wikidata-qid", data.WikidataQid ?? string.Empty),
                    new XElement("occupation",   data.Occupation  ?? string.Empty)
                ),
                new XElement("biography", data.Biography ?? string.Empty),
                new XElement("social",
                    new XElement("instagram", data.Instagram ?? string.Empty),
                    new XElement("twitter",   data.Twitter   ?? string.Empty),
                    new XElement("tiktok",    data.TikTok    ?? string.Empty),
                    new XElement("mastodon",  data.Mastodon  ?? string.Empty),
                    new XElement("website",   data.Website   ?? string.Empty)
                )
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
            var result   = new PersonSidecarData
            {
                Name        = identity?.Element("name")?.Value         ?? string.Empty,
                Role        = identity?.Element("role")?.Value         ?? string.Empty,
                WikidataQid = NullIfEmpty(identity?.Element("wikidata-qid")?.Value),
                Occupation  = NullIfEmpty(identity?.Element("occupation")?.Value),
                Biography   = NullIfEmpty(root.Element("biography")?.Value),
                Instagram   = NullIfEmpty(social?.Element("instagram")?.Value),
                Twitter     = NullIfEmpty(social?.Element("twitter")?.Value),
                TikTok      = NullIfEmpty(social?.Element("tiktok")?.Value),
                Mastodon    = NullIfEmpty(social?.Element("mastodon")?.Value),
                Website     = NullIfEmpty(social?.Element("website")?.Value),
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
    /// Parses the <c>&lt;bridges&gt;</c> section of a library.xml sidecar.
    /// Returns an empty dictionary if the section is missing (backward-compatible
    /// with v1.0 sidecars that lack bridges).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseBridges(XElement? bridgesElement)
    {
        if (bridgesElement is null)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bridge in bridgesElement.Elements("bridge"))
        {
            var key   = bridge.Attribute("key")?.Value;
            var value = bridge.Attribute("value")?.Value;
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                dict[key] = value;
        }

        return dict;
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
