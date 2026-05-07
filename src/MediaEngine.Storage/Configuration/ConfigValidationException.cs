namespace MediaEngine.Storage.Configuration;

/// <summary>
/// Raised when an existing configuration file is present but cannot be safely loaded.
/// </summary>
public sealed class ConfigValidationException : Exception
{
    public ConfigValidationException(string filePath, string schemaName, IReadOnlyList<string> validationMessages)
        : base(BuildMessage(filePath, schemaName, validationMessages))
    {
        FilePath = filePath;
        SchemaName = schemaName;
        ValidationMessages = validationMessages;
    }

    public string FilePath { get; }
    public string SchemaName { get; }
    public IReadOnlyList<string> ValidationMessages { get; }

    private static string BuildMessage(string filePath, string schemaName, IReadOnlyList<string> validationMessages)
    {
        var safeMessages = validationMessages.Count == 0
            ? "The file is not valid JSON or does not match the expected shape."
            : string.Join("; ", validationMessages.Select(Sanitize));

        return $"Configuration file '{filePath}' failed validation against '{schemaName}': {safeMessages}";
    }

    private static string Sanitize(string message) =>
        message
            .Replace("api_key", "[secret-field]", StringComparison.OrdinalIgnoreCase)
            .Replace("password", "[secret-field]", StringComparison.OrdinalIgnoreCase)
            .Replace("client_secret", "[secret-field]", StringComparison.OrdinalIgnoreCase);
}
