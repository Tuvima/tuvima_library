using System.Xml.Linq;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Ingestion;

/// <summary>
/// Reads and writes <c>universe.xml</c> files under <c>.universe/{Label} ({Qid})/</c>.
///
/// <para>
/// <b>Write path:</b> Called after fictional entity enrichment. Builds a
/// <see cref="UniverseSnapshot"/> containing all entities, relationships, and the
/// narrative hierarchy, then writes the full XML snapshot.
/// </para>
///
/// <para>
/// <b>Read path:</b> Called during Great Inhale to restore the universe graph
/// from the filesystem without network access.
/// </para>
///
/// Uses <see cref="System.Xml.Linq"/> — same BCL dependency as
/// <see cref="Contracts.ISidecarWriter"/>, no new NuGet packages.
/// </summary>
public sealed class UniverseSidecarWriter : IUniverseSidecarWriter
{
    private const string Version = "1.0";
    private readonly ILogger<UniverseSidecarWriter> _logger;

    public UniverseSidecarWriter(ILogger<UniverseSidecarWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task WriteUniverseXmlAsync(
        string universeFolderPath,
        UniverseSnapshot data,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(universeFolderPath);
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(universeFolderPath);
        var xmlPath = Path.Combine(universeFolderPath, "universe.xml");

        var doc = BuildXml(data);

        await using var stream = new FileStream(
            xmlPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        await doc.SaveAsync(stream, SaveOptions.None, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Universe XML written: {Path} ({EntityCount} entities, {EdgeCount} relationships)",
            xmlPath, data.Entities.Count, data.Relationships.Count);
    }

    /// <inheritdoc/>
    public Task<UniverseSnapshot?> ReadUniverseXmlAsync(
        string xmlPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        if (!File.Exists(xmlPath))
        {
            _logger.LogDebug("Universe XML not found: {Path}", xmlPath);
            return Task.FromResult<UniverseSnapshot?>(null);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var doc = XDocument.Load(xmlPath);
            var snapshot = ParseXml(doc);
            return Task.FromResult<UniverseSnapshot?>(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read universe XML: {Path}", xmlPath);
            return Task.FromResult<UniverseSnapshot?>(null);
        }
    }

    // ── XML Builder ─────────────────────────────────────────────────────────

    private static XDocument BuildXml(UniverseSnapshot data)
    {
        var root = new XElement("universe",
            new XAttribute("version", Version),
            new XAttribute("root-qid", data.Root.Qid),
            new XAttribute("label", data.Root.Label));

        // Narrative hierarchy
        if (data.Hierarchy.Count > 0)
        {
            var hierarchyEl = new XElement("narrative-hierarchy");
            foreach (var node in data.Hierarchy)
                hierarchyEl.Add(BuildHierarchyNode(node));
            root.Add(hierarchyEl);
        }

        // Entities
        var entitiesEl = new XElement("entities");
        foreach (var entitySnapshot in data.Entities)
        {
            var entityEl = new XElement("entity",
                new XAttribute("qid", entitySnapshot.Entity.WikidataQid),
                new XAttribute("type", entitySnapshot.Entity.EntitySubType),
                new XAttribute("label", entitySnapshot.Entity.Label));

            if (!string.IsNullOrWhiteSpace(entitySnapshot.Entity.Description))
                entityEl.Add(new XElement("description", entitySnapshot.Entity.Description));

            // Properties
            if (entitySnapshot.Properties.Count > 0)
            {
                var propsEl = new XElement("properties");
                foreach (var (key, value) in entitySnapshot.Properties)
                    propsEl.Add(new XElement("prop", new XAttribute("key", key), new XAttribute("value", value)));
                entityEl.Add(propsEl);
            }

            // Performer reference (character → person link)
            if (!string.IsNullOrWhiteSpace(entitySnapshot.PerformerPersonQid))
            {
                var performerEl = new XElement("performer",
                    new XAttribute("person-qid", entitySnapshot.PerformerPersonQid));
                if (!string.IsNullOrWhiteSpace(entitySnapshot.PerformerWorkQid))
                    performerEl.Add(new XAttribute("work-qid", entitySnapshot.PerformerWorkQid));
                entityEl.Add(performerEl);
            }

            // Work links
            if (entitySnapshot.WorkLinks.Count > 0)
            {
                var workLinksEl = new XElement("work-links");
                foreach (var wl in entitySnapshot.WorkLinks)
                {
                    var workEl = new XElement("work", new XAttribute("qid", wl.WorkQid));
                    if (!string.IsNullOrWhiteSpace(wl.WorkLabel))
                        workEl.Add(new XAttribute("label", wl.WorkLabel));
                    workLinksEl.Add(workEl);
                }
                entityEl.Add(workLinksEl);
            }

            entitiesEl.Add(entityEl);
        }
        root.Add(entitiesEl);

        // Relationships
        if (data.Relationships.Count > 0)
        {
            var relsEl = new XElement("relationships");
            foreach (var rel in data.Relationships)
            {
                var relEl = new XElement("rel",
                    new XAttribute("subject", rel.SubjectQid),
                    new XAttribute("type", rel.RelationshipTypeValue),
                    new XAttribute("object", rel.ObjectQid),
                    new XAttribute("confidence", rel.Confidence.ToString("F1")));
                if (!string.IsNullOrWhiteSpace(rel.ContextWorkQid))
                    relEl.Add(new XAttribute("context-work", rel.ContextWorkQid));
                relsEl.Add(relEl);
            }
            root.Add(relsEl);
        }

        root.Add(new XElement("last-updated", data.LastUpdated.ToString("o")));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement BuildHierarchyNode(NarrativeHierarchyNode node)
    {
        var tagName = node.Level.ToLowerInvariant() switch
        {
            "franchise" => "franchise",
            "series" => "series",
            "work" => "work",
            _ => "universe",
        };

        var el = new XElement(tagName,
            new XAttribute("qid", node.Qid),
            new XAttribute("label", node.Label));

        foreach (var child in node.Children)
            el.Add(BuildHierarchyNode(child));

        return el;
    }

    // ── XML Parser ──────────────────────────────────────────────────────────

    private static UniverseSnapshot ParseXml(XDocument doc)
    {
        var rootEl = doc.Root ?? throw new InvalidOperationException("Missing root element");

        var rootQid = rootEl.Attribute("root-qid")?.Value ?? string.Empty;
        var rootLabel = rootEl.Attribute("label")?.Value ?? rootQid;

        // Parse narrative hierarchy
        var hierarchy = new List<NarrativeHierarchyNode>();
        var hierarchyEl = rootEl.Element("narrative-hierarchy");
        if (hierarchyEl is not null)
        {
            foreach (var child in hierarchyEl.Elements())
                hierarchy.Add(ParseHierarchyNode(child));
        }

        // Parse entities
        var entities = new List<FictionalEntitySnapshot>();
        var entitiesEl = rootEl.Element("entities");
        if (entitiesEl is not null)
        {
            foreach (var entityEl in entitiesEl.Elements("entity"))
            {
                var entity = new FictionalEntity
                {
                    Id = Guid.NewGuid(), // Will be reconciled during Great Inhale
                    WikidataQid = entityEl.Attribute("qid")?.Value ?? string.Empty,
                    Label = entityEl.Attribute("label")?.Value ?? string.Empty,
                    EntitySubType = entityEl.Attribute("type")?.Value ?? "Character",
                    Description = entityEl.Element("description")?.Value,
                    FictionalUniverseQid = rootQid,
                    FictionalUniverseLabel = rootLabel,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                // Parse properties
                var properties = new Dictionary<string, string>();
                var propsEl = entityEl.Element("properties");
                if (propsEl is not null)
                {
                    foreach (var propEl in propsEl.Elements("prop"))
                    {
                        var key = propEl.Attribute("key")?.Value;
                        var value = propEl.Attribute("value")?.Value;
                        if (key is not null && value is not null)
                            properties[key] = value;
                    }
                }

                // Parse work links
                var workLinks = new List<WorkLinkSnapshot>();
                var workLinksEl = entityEl.Element("work-links");
                if (workLinksEl is not null)
                {
                    foreach (var workEl in workLinksEl.Elements("work"))
                    {
                        workLinks.Add(new WorkLinkSnapshot(
                            workEl.Attribute("qid")?.Value ?? string.Empty,
                            workEl.Attribute("label")?.Value));
                    }
                }

                // Parse performer
                var performerEl = entityEl.Element("performer");

                entities.Add(new FictionalEntitySnapshot
                {
                    Entity = entity,
                    Properties = properties,
                    WorkLinks = workLinks,
                    PerformerPersonQid = performerEl?.Attribute("person-qid")?.Value,
                    PerformerWorkQid = performerEl?.Attribute("work-qid")?.Value,
                });
            }
        }

        // Parse relationships
        var relationships = new List<EntityRelationship>();
        var relsEl = rootEl.Element("relationships");
        if (relsEl is not null)
        {
            foreach (var relEl in relsEl.Elements("rel"))
            {
                relationships.Add(new EntityRelationship
                {
                    SubjectQid = relEl.Attribute("subject")?.Value ?? string.Empty,
                    RelationshipTypeValue = relEl.Attribute("type")?.Value ?? string.Empty,
                    ObjectQid = relEl.Attribute("object")?.Value ?? string.Empty,
                    Confidence = double.TryParse(relEl.Attribute("confidence")?.Value, out var c) ? c : 0.5,
                    ContextWorkQid = relEl.Attribute("context-work")?.Value,
                    DiscoveredAt = DateTimeOffset.UtcNow,
                });
            }
        }

        var lastUpdated = DateTimeOffset.TryParse(
            rootEl.Element("last-updated")?.Value, out var lu)
            ? lu : DateTimeOffset.UtcNow;

        return new UniverseSnapshot
        {
            Root = new NarrativeRoot
            {
                Qid = rootQid,
                Label = rootLabel,
                Level = "Universe",
                CreatedAt = lastUpdated,
            },
            Entities = entities,
            Relationships = relationships,
            Hierarchy = hierarchy,
            LastUpdated = lastUpdated,
        };
    }

    private static NarrativeHierarchyNode ParseHierarchyNode(XElement el)
    {
        var level = el.Name.LocalName switch
        {
            "franchise" => "Franchise",
            "series" => "Series",
            "work" => "Work",
            _ => "Universe",
        };

        var children = new List<NarrativeHierarchyNode>();
        foreach (var child in el.Elements())
            children.Add(ParseHierarchyNode(child));

        return new NarrativeHierarchyNode
        {
            Qid = el.Attribute("qid")?.Value ?? string.Empty,
            Label = el.Attribute("label")?.Value ?? string.Empty,
            Level = level,
            Children = children,
        };
    }
}
