namespace MediaEngine.Contracts.Items;

/// <summary>
/// Response for <c>DELETE /library/items/{entityId}</c>. Property names are byte-identical to
/// the anonymous object this record replaced (Stage 5A wave 2 response-shape promotion).
/// </summary>
public sealed record DeleteLibraryItemResponse
{
    public Guid EntityId { get; init; }
    public int FilesDeleted { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Response for <c>POST /library/items/{entityId}/reject</c> (single-item rejection).
/// </summary>
public sealed record RejectLibraryItemResponse
{
    public Guid EntityId { get; init; }
    public string NewFilePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Response for <c>POST /library/items/{entityId}/recover</c>. The lowercase <c>message</c>
/// property name is intentional: it matches the anonymous object's member spelling exactly so
/// the wire shape stays byte-identical regardless of any JSON naming policy in effect.
/// </summary>
public sealed record RecoverLibraryItemResponse
{
    public string message { get; init; } = string.Empty;
}

/// <summary>
/// Response for <c>POST /library/items/{entityId}/provisional</c>.
/// </summary>
public sealed record MarkProvisionalResponse
{
    public Guid EntityId { get; init; }
    public string State { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
