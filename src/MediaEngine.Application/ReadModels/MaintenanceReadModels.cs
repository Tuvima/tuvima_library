namespace MediaEngine.Application.ReadModels;

public sealed record OrphanImageReferenceSet(
    HashSet<string> KnownWorkQids,
    HashSet<string> KnownWorkId12,
    HashSet<string> KnownPersonQids,
    HashSet<string> KnownUniverseQids);

public sealed record ReviewReasonCount(string? Trigger, string? Detail, int Count);
