using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Central authorization boundary for filesystem mutations. This gate is pure:
/// it never changes source configuration or touches the filesystem.
/// </summary>
public interface ISourceMutationPolicyGate
{
    SourceMutationDecision Evaluate(SourceMutationRequest request);
}
