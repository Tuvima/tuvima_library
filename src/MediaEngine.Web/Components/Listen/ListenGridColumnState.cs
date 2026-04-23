using System.Text.Json;

namespace MediaEngine.Web.Components.Listen;

public static class ListenGridColumnState
{
    public static HashSet<string> ResolveHiddenColumns(
        string? storedVisibleColumnsJson,
        IReadOnlyCollection<string> knownColumns,
        IReadOnlyCollection<string> defaultHiddenColumns)
    {
        ArgumentNullException.ThrowIfNull(knownColumns);
        ArgumentNullException.ThrowIfNull(defaultHiddenColumns);

        var hiddenColumns = new HashSet<string>(defaultHiddenColumns, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(storedVisibleColumnsJson))
        {
            return hiddenColumns;
        }

        try
        {
            var storedVisibleColumns = JsonSerializer.Deserialize<List<string>>(storedVisibleColumnsJson) ?? [];
            var recognizedVisibleColumns = storedVisibleColumns
                .Where(column => knownColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Ignore stale or incompatible saved state rather than hiding every data column.
            if (recognizedVisibleColumns.Count == 0)
            {
                return hiddenColumns;
            }

            hiddenColumns.Clear();
            foreach (var column in knownColumns)
            {
                if (!recognizedVisibleColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    hiddenColumns.Add(column);
                }
            }
        }
        catch (JsonException)
        {
            return hiddenColumns;
        }

        return hiddenColumns;
    }
}
