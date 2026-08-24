using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.PersonalMedia;

/// <summary>
/// Parses and validates the versioned rule document used by Smart Galleries.
/// This is the only accepted rule vocabulary for personal media. Keeping the
/// vocabulary here prevents UI labels or arbitrary client values from becoming
/// query identifiers.
/// </summary>
public static class ViewSmartGalleryRules
{
    public const int CurrentVersion = 1;
    public const int MaximumDocumentLength = 64 * 1024;
    public const int MaximumGroups = 20;
    public const int MaximumConditionsPerGroup = 30;
    public const int MaximumValuesPerCondition = 50;
    public const int MaximumValueLength = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Operators =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["media_type"] = Set("eq", "neq", "in", "known", "unknown"),
            ["file_type"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["orientation"] = Set("eq", "neq", "in", "known", "unknown"),
            ["duration"] = Set("eq", "neq", "known", "unknown", "gt", "lt", "gte", "lte", "between"),
            ["captured_date"] = Set("eq", "neq", "known", "unknown", "gt", "lt", "gte", "lte", "between"),
            ["people"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["place"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["device"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["tags"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["favorite"] = Set("eq", "neq", "known", "unknown"),
            ["owner"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
            ["source"] = Set("eq", "neq", "in", "contains", "known", "unknown"),
        };

    public static CollectionRuleDefinition Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > MaximumDocumentLength)
            throw new ArgumentException($"Smart Gallery rules cannot exceed {MaximumDocumentLength} characters.", nameof(json));

        CollectionRuleDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<CollectionRuleDefinition>(json, JsonOptions)
                ?? throw new ArgumentException("Smart Gallery rule JSON must contain a rule definition.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Smart Gallery rule JSON is malformed.", nameof(json), exception);
        }

        Validate(definition);
        return definition;
    }

    public static string Normalize(string json) =>
        JsonSerializer.Serialize(Parse(json), JsonOptions);

    public static void Validate(CollectionRuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Version != CurrentVersion)
            throw new ArgumentException($"Smart Gallery rule version {definition.Version} is unsupported; expected version {CurrentVersion}.", nameof(definition));
        if (definition.Groups is null || definition.Groups.Count is < 1 or > MaximumGroups)
            throw new ArgumentException($"Smart Gallery rules require between 1 and {MaximumGroups} groups.", nameof(definition));

        for (var groupIndex = 0; groupIndex < definition.Groups.Count; groupIndex++)
        {
            var group = definition.Groups[groupIndex]
                ?? throw new ArgumentException($"Smart Gallery rule group {groupIndex + 1} is missing.", nameof(definition));
            group.MatchMode = NormalizeChoice(group.MatchMode, "all", "any", $"group {groupIndex + 1} match mode");
            group.JoinWithPrevious = NormalizeChoice(group.JoinWithPrevious, "and", "or", $"group {groupIndex + 1} join");
            if (group.Conditions is null || group.Conditions.Count is < 1 or > MaximumConditionsPerGroup)
                throw new ArgumentException($"Smart Gallery rule group {groupIndex + 1} requires between 1 and {MaximumConditionsPerGroup} conditions.", nameof(definition));

            for (var conditionIndex = 0; conditionIndex < group.Conditions.Count; conditionIndex++)
                ValidateCondition(group.Conditions[conditionIndex], groupIndex, conditionIndex);
        }
    }

    private static void ValidateCondition(CollectionRulePredicate? condition, int groupIndex, int conditionIndex)
    {
        var label = $"condition {conditionIndex + 1} in group {groupIndex + 1}";
        if (condition is null) throw new ArgumentException($"Smart Gallery rule {label} is missing.");
        if (string.IsNullOrWhiteSpace(condition.Field) || string.IsNullOrWhiteSpace(condition.Op))
            throw new ArgumentException($"Smart Gallery rule {label} requires a field and operator.");
        condition.Field = condition.Field.Trim().ToLowerInvariant();
        condition.Op = condition.Op.Trim().ToLowerInvariant();
        if (!Operators.TryGetValue(condition.Field, out var supported))
            throw new ArgumentException($"Smart Gallery field '{condition.Field}' is unsupported.");
        if (!supported.Contains(condition.Op))
            throw new ArgumentException($"Operator '{condition.Op}' is unsupported for Smart Gallery field '{condition.Field}'.");

        var values = condition.GetEffectiveValues();
        if (condition.Op is "known" or "unknown")
        {
            if (values.Length != 0)
                throw new ArgumentException($"Smart Gallery {label} must not include values for '{condition.Op}'.");
            return;
        }

        var requiredCount = condition.Op == "between" ? 2 : 1;
        if (values.Length < requiredCount || values.Length > MaximumValuesPerCondition
            || (condition.Op != "in" && values.Length != requiredCount))
            throw new ArgumentException(condition.Op == "between"
                ? $"Smart Gallery {label} requires exactly two values."
                : condition.Op == "in"
                    ? $"Smart Gallery {label} requires between 1 and {MaximumValuesPerCondition} values."
                    : $"Smart Gallery {label} requires exactly one value.");
        if (values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueLength))
            throw new ArgumentException($"Smart Gallery {label} contains an empty or overlong value.");

        if (condition.Field == "media_type" && values.Any(value => !LocalMediaKinds.Contains(value)))
            throw new ArgumentException("Smart Gallery media_type values must be image, video, document, audio, or other.");
        if (condition.Field == "orientation" && values.Any(value => !OrientationValues.Contains(value)))
            throw new ArgumentException("Smart Gallery orientation values must be landscape, portrait, or square.");
        if (condition.Field == "favorite" && values.Any(value => !bool.TryParse(value, out _)))
            throw new ArgumentException("Smart Gallery favorite values must be true or false.");
        if (condition.Field == "duration")
        {
            var parsed = new List<double>(values.Length);
            foreach (var value in values)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number) || number < 0)
                    throw new ArgumentException("Smart Gallery duration values must be finite non-negative numbers.");
                parsed.Add(number);
            }
            if (condition.Op == "between" && parsed[0] > parsed[1])
                throw new ArgumentException("Smart Gallery duration between values must be in ascending order.");
        }
        if (condition.Field == "captured_date")
        {
            var parsed = new List<DateOnly>(values.Length);
            foreach (var value in values)
            {
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                    throw new ArgumentException("Smart Gallery captured_date values must use yyyy-MM-dd.");
                parsed.Add(date);
            }
            if (condition.Op == "between" && parsed[0] > parsed[1])
                throw new ArgumentException("Smart Gallery captured_date between values must be in ascending order.");
        }
    }

    private static string NormalizeChoice(string? value, string first, string second, string label)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized == first || normalized == second
            ? normalized
            : throw new ArgumentException($"Smart Gallery {label} must be '{first}' or '{second}'.");
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> LocalMediaKinds = Set("image", "video", "document", "audio", "other");
    private static readonly IReadOnlySet<string> OrientationValues = Set("landscape", "portrait", "square");
}
