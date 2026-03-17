using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using VDS.RDF;
using VDS.RDF.Query;
using VDS.RDF.Query.Datasets;
using VDS.RDF.Parsing;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Local in-memory SPARQL query service using dotNetRDF.
/// Loads universe data from SQLite into an in-memory RDF graph,
/// then runs SPARQL queries locally with zero network calls.
/// </summary>
public sealed class UniverseGraphQueryService : IUniverseGraphQueryService
{
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IEntityRelationshipRepository _relRepo;
    private readonly ILogger<UniverseGraphQueryService> _logger;

    private readonly ConcurrentDictionary<string, IGraph> _graphCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks = new(StringComparer.OrdinalIgnoreCase);

    private const string TuvimaNamespace     = "https://tuvima.io/entity/";
    private const string TuvimaRelNamespace  = "https://tuvima.io/rel/";
    private const string TuvimaTypeNamespace = "https://tuvima.io/type/";
    private const string TuvimaWorkNamespace = "https://tuvima.io/work/";

    public UniverseGraphQueryService(
        IFictionalEntityRepository entityRepo,
        IEntityRelationshipRepository relRepo,
        ILogger<UniverseGraphQueryService> logger)
    {
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(relRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _entityRepo = entityRepo;
        _relRepo    = relRepo;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IReadOnlyList<string>>> FindPathsAsync(
        string universeQid, string fromQid, string toQid,
        int maxHops = 4, CancellationToken ct = default)
    {
        var graph = await GetOrLoadGraphAsync(universeQid, ct).ConfigureAwait(false);

        // Use BFS on the graph to find all paths up to maxHops.
        // SPARQL property paths have limited path enumeration support,
        // so we use direct graph traversal for reliability.
        var results = new List<IReadOnlyList<string>>();
        var queue = new Queue<List<string>>();
        queue.Enqueue(new List<string> { fromQid });

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var path = queue.Dequeue();

            if (path.Count > maxHops + 1) continue;

            var current = path[^1];
            if (string.Equals(current, toQid, StringComparison.OrdinalIgnoreCase) && path.Count > 1)
            {
                results.Add(path);
                continue;
            }

            if (path.Count > maxHops) continue;

            // Find all neighbors (both outgoing and incoming edges).
            var currentNode = graph.CreateUriNode(new Uri(TuvimaNamespace + current));
            var outgoing = graph.GetTriplesWithSubject(currentNode);
            var incoming = graph.GetTriplesWithObject(currentNode);

            var neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var triple in outgoing)
            {
                if (triple.Object is IUriNode obj && obj.Uri.AbsoluteUri.StartsWith(TuvimaNamespace, StringComparison.Ordinal))
                {
                    var neighbor = obj.Uri.AbsoluteUri[TuvimaNamespace.Length..];
                    if (!path.Contains(neighbor, StringComparer.OrdinalIgnoreCase))
                        neighbors.Add(neighbor);
                }
            }

            foreach (var triple in incoming)
            {
                if (triple.Subject is IUriNode subj && subj.Uri.AbsoluteUri.StartsWith(TuvimaNamespace, StringComparison.Ordinal))
                {
                    var neighbor = subj.Uri.AbsoluteUri[TuvimaNamespace.Length..];
                    if (!path.Contains(neighbor, StringComparer.OrdinalIgnoreCase))
                        neighbors.Add(neighbor);
                }
            }

            foreach (var neighbor in neighbors)
            {
                var newPath = new List<string>(path) { neighbor };
                queue.Enqueue(newPath);
            }
        }

        _logger.LogDebug("FindPaths {From}→{To}: found {Count} paths (max {MaxHops} hops)",
            fromQid, toQid, results.Count, maxHops);
        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetFamilyTreeAsync(
        string universeQid, string characterQid,
        int generations = 3, CancellationToken ct = default)
    {
        var graph = await GetOrLoadGraphAsync(universeQid, ct).ConfigureAwait(false);
        var result = new Dictionary<int, IReadOnlyList<string>>();

        // BFS traversal following parent/child edges.
        // Negative generations = ancestors, positive = descendants.
        var parentPredicates = new[]
        {
            new Uri(TuvimaRelNamespace + "father"),
            new Uri(TuvimaRelNamespace + "mother"),
        };
        var childPredicate = new Uri(TuvimaRelNamespace + "child");

        // Generation 0 = the character itself.
        result[0] = new List<string> { characterQid };

        // Traverse ancestors (follow father/mother edges as subject→object,
        // meaning the subject HAS a father/mother of the object).
        var currentGen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { characterQid };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { characterQid };

        for (int gen = 1; gen <= generations; gen++)
        {
            ct.ThrowIfCancellationRequested();
            var nextGen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var qid in currentGen)
            {
                var node = graph.CreateUriNode(new Uri(TuvimaNamespace + qid));

                // Find parents: subject=qid, predicate=father/mother, object=parent
                foreach (var parentPred in parentPredicates)
                {
                    var predNode = graph.CreateUriNode(parentPred);
                    foreach (var triple in graph.GetTriplesWithSubjectPredicate(node, predNode))
                    {
                        if (triple.Object is IUriNode obj && obj.Uri.AbsoluteUri.StartsWith(TuvimaNamespace, StringComparison.Ordinal))
                        {
                            var parentQid = obj.Uri.AbsoluteUri[TuvimaNamespace.Length..];
                            if (visited.Add(parentQid))
                                nextGen.Add(parentQid);
                        }
                    }
                }
            }

            if (nextGen.Count > 0)
                result[-gen] = nextGen.ToList();

            currentGen = nextGen;
            if (currentGen.Count == 0) break;
        }

        // Traverse descendants (follow child edges).
        currentGen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { characterQid };

        for (int gen = 1; gen <= generations; gen++)
        {
            ct.ThrowIfCancellationRequested();
            var nextGen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var qid in currentGen)
            {
                var node = graph.CreateUriNode(new Uri(TuvimaNamespace + qid));
                var childNode = graph.CreateUriNode(childPredicate);

                foreach (var triple in graph.GetTriplesWithSubjectPredicate(node, childNode))
                {
                    if (triple.Object is IUriNode obj && obj.Uri.AbsoluteUri.StartsWith(TuvimaNamespace, StringComparison.Ordinal))
                    {
                        var childQid = obj.Uri.AbsoluteUri[TuvimaNamespace.Length..];
                        if (visited.Add(childQid))
                            nextGen.Add(childQid);
                    }
                }
            }

            if (nextGen.Count > 0)
                result[gen] = nextGen.ToList();

            currentGen = nextGen;
            if (currentGen.Count == 0) break;
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> FindCrossMediaEntitiesAsync(
        string universeQid, CancellationToken ct = default)
    {
        var graph = await GetOrLoadGraphAsync(universeQid, ct).ConfigureAwait(false);

        // Use SPARQL to find entities linked to 2+ works.
        const string sparqlText = """
            PREFIX te: <https://tuvima.io/entity/>
            PREFIX tw: <https://tuvima.io/work/>

            SELECT ?entity (COUNT(DISTINCT ?work) AS ?workCount)
            WHERE {
                ?entity tw:appears_in ?work .
            }
            GROUP BY ?entity
            HAVING (COUNT(DISTINCT ?work) >= 2)
            """;

        var dataset = new InMemoryDataset(graph);
        var processor = new LeviathanQueryProcessor(dataset);
        var parser = new SparqlQueryParser();
        var query = parser.ParseFromString(sparqlText);
        var resultSet = processor.ProcessQuery(query) as SparqlResultSet;

        var crossMedia = new List<string>();
        if (resultSet is not null)
        {
            foreach (var sparqlResult in resultSet)
            {
                if (sparqlResult["entity"] is IUriNode entityNode &&
                    entityNode.Uri.AbsoluteUri.StartsWith(TuvimaNamespace, StringComparison.Ordinal))
                {
                    crossMedia.Add(entityNode.Uri.AbsoluteUri[TuvimaNamespace.Length..]);
                }
            }
        }

        return crossMedia;
    }

    /// <inheritdoc/>
    public void InvalidateCache(string universeQid)
    {
        if (_graphCache.TryRemove(universeQid, out var removed))
        {
            removed.Dispose();
            _logger.LogDebug("Invalidated graph cache for universe {Qid}", universeQid);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<IGraph> GetOrLoadGraphAsync(string universeQid, CancellationToken ct)
    {
        if (_graphCache.TryGetValue(universeQid, out var cached))
            return cached;

        var loadLock = _loadLocks.GetOrAdd(universeQid, _ => new SemaphoreSlim(1, 1));
        await loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock.
            if (_graphCache.TryGetValue(universeQid, out cached))
                return cached;

            var graph = await BuildGraphFromSqliteAsync(universeQid, ct).ConfigureAwait(false);
            _graphCache[universeQid] = graph;

            _logger.LogInformation(
                "Built in-memory graph for universe {Qid}: {TripleCount} triples",
                universeQid, graph.Triples.Count);

            return graph;
        }
        finally
        {
            loadLock.Release();
        }
    }

    private async Task<IGraph> BuildGraphFromSqliteAsync(string universeQid, CancellationToken ct)
    {
        var graph = new Graph();
        graph.NamespaceMap.AddNamespace("te", new Uri(TuvimaNamespace));
        graph.NamespaceMap.AddNamespace("tr", new Uri(TuvimaRelNamespace));
        graph.NamespaceMap.AddNamespace("tt", new Uri(TuvimaTypeNamespace));
        graph.NamespaceMap.AddNamespace("tw", new Uri(TuvimaWorkNamespace));

        var rdfType  = graph.CreateUriNode(new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"));
        var rdfsLabel = graph.CreateUriNode(new Uri("http://www.w3.org/2000/01/rdf-schema#label"));

        // Load all entities in this universe.
        var entities   = await _entityRepo.GetByUniverseAsync(universeQid, ct).ConfigureAwait(false);
        var entityQids = entities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            ct.ThrowIfCancellationRequested();

            var entityNode = graph.CreateUriNode(new Uri(TuvimaNamespace + entity.WikidataQid));
            var typeNode   = graph.CreateUriNode(new Uri(TuvimaTypeNamespace + entity.EntitySubType));

            graph.Assert(entityNode, rdfType, typeNode);

            if (!string.IsNullOrWhiteSpace(entity.Label))
            {
                graph.Assert(entityNode, rdfsLabel, graph.CreateLiteralNode(entity.Label));
            }

            // Add work links as triples.
            var workLinks  = await _entityRepo.GetWorkLinksAsync(entity.Id, ct).ConfigureAwait(false);
            var appearsIn  = graph.CreateUriNode(new Uri(TuvimaWorkNamespace + "appears_in"));
            foreach (var link in workLinks)
            {
                var workNode = graph.CreateUriNode(new Uri(TuvimaWorkNamespace + link.WorkQid));
                graph.Assert(entityNode, appearsIn, workNode);
            }
        }

        // Load relationships between entities in this universe.
        var relationships = await _relRepo.GetByUniverseAsync(entityQids, ct).ConfigureAwait(false);

        foreach (var rel in relationships)
        {
            ct.ThrowIfCancellationRequested();

            var subjectNode = graph.CreateUriNode(new Uri(TuvimaNamespace + rel.SubjectQid));
            var objectNode  = graph.CreateUriNode(new Uri(TuvimaNamespace + rel.ObjectQid));
            var predNode    = graph.CreateUriNode(new Uri(TuvimaRelNamespace + rel.RelationshipTypeValue));

            graph.Assert(subjectNode, predNode, objectNode);
        }

        return graph;
    }
}
