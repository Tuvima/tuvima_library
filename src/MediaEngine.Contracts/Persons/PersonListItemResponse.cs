namespace MediaEngine.Contracts.Persons;

/// <summary>
/// One row of <c>GET /persons</c>. Property names are byte-identical to the
/// anonymous type this record replaces — no <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record PersonListItemResponse(
    Guid id,
    string name,
    List<string> roles,
    string? wikidata_qid,
    string? headshot_url,
    bool has_local_headshot,
    bool is_pseudonym,
    bool is_group,
    string? biography,
    string? occupation);
