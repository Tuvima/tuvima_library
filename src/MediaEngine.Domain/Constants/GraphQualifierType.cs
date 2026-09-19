namespace MediaEngine.Domain.Constants;

/// <summary>
/// Stable qualifier names stored alongside graph facts. Values stay normalized in
/// <c>entity_relationship_qualifiers</c>, rather than being flattened into edge text.
/// </summary>
public static class GraphQualifierType
{
    public const string AppliesToWork = "applies_to_work";
    public const string StartTime = "start_time";
    public const string EndTime = "end_time";
    public const string TimeIndex = "time_index";
    public const string SpoilerForWork = "spoiler_for_work";
    public const string StatementNature = "statement_nature";
    public const string SourceReference = "source_reference";
    public const string Confidence = "confidence";
}
