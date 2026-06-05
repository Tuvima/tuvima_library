namespace MediaEngine.Domain.Services;

public static class TextEncodingRepair
{
    private static readonly (string Broken, string Fixed)[] Replacements =
    [
        ("\u0102\u20AC", "\u00C0"),
        ("\u0102\u0081", "\u00C1"),
        ("\u0102\u201A", "\u00C2"),
        ("\u0102\u0192", "\u00C3"),
        ("\u0102\u201E", "\u00C4"),
        ("\u0102\u2026", "\u00C5"),
        ("\u0102\u2020", "\u00C6"),
        ("\u0102\u2021", "\u00C7"),
        ("\u0102\u02C6", "\u00C8"),
        ("\u0102\u2030", "\u00C9"),
        ("\u0102\u0160", "\u00CA"),
        ("\u0102\u2039", "\u00CB"),
        ("\u0102\u015A", "\u00CC"),
        ("\u0102\u0164", "\u00CD"),
        ("\u0102\u017D", "\u00CE"),
        ("\u0102\u0179", "\u00CF"),
        ("\u0102\u00A0", "\u00E0"),
        ("\u0102\u00A1", "\u00E1"),
        ("\u0102\u00A2", "\u00E2"),
        ("\u0102\u00A3", "\u00E3"),
        ("\u0102\u00A4", "\u00E4"),
        ("\u0102\u00A5", "\u00E5"),
        ("\u0102\u00A6", "\u00E6"),
        ("\u0102\u00A7", "\u00E7"),
        ("\u0102\u00A8", "\u00E8"),
        ("\u0102\u00A9", "\u00E9"),
        ("\u0102\u00AA", "\u00EA"),
        ("\u0102\u00AB", "\u00EB"),
        ("\u0102\u00AC", "\u00EC"),
        ("\u0102\u00AD", "\u00ED"),
        ("\u0102\u00AE", "\u00EE"),
        ("\u0102\u00AF", "\u00EF"),
        ("\u0102\u00B1", "\u00F1"),
        ("\u0102\u00B2", "\u00F2"),
        ("\u0102\u00B3", "\u00F3"),
        ("\u0102\u00B4", "\u00F4"),
        ("\u0102\u00B5", "\u00F5"),
        ("\u0102\u00B6", "\u00F6"),
        ("\u0102\u00B9", "\u00F9"),
        ("\u0102\u00BA", "\u00FA"),
        ("\u0102\u00BB", "\u00FB"),
        ("\u0102\u00BC", "\u00FC"),
        ("\u0102\u00BD", "\u00FD"),
        ("\u0102\u00BF", "\u00FF"),
        ("\u00C3\u20AC", "\u00C0"),
        ("\u00C3\u0081", "\u00C1"),
        ("\u00C3\u201A", "\u00C2"),
        ("\u00C3\u0192", "\u00C3"),
        ("\u00C3\u201E", "\u00C4"),
        ("\u00C3\u2026", "\u00C5"),
        ("\u00C3\u2020", "\u00C6"),
        ("\u00C3\u2021", "\u00C7"),
        ("\u00C3\u02C6", "\u00C8"),
        ("\u00C3\u2030", "\u00C9"),
        ("\u00C3\u0160", "\u00CA"),
        ("\u00C3\u2039", "\u00CB"),
        ("\u00C3\u00A0", "\u00E0"),
        ("\u00C3\u00A1", "\u00E1"),
        ("\u00C3\u00A2", "\u00E2"),
        ("\u00C3\u00A3", "\u00E3"),
        ("\u00C3\u00A4", "\u00E4"),
        ("\u00C3\u00A5", "\u00E5"),
        ("\u00C3\u00A6", "\u00E6"),
        ("\u00C3\u00A7", "\u00E7"),
        ("\u00C3\u00A8", "\u00E8"),
        ("\u00C3\u00A9", "\u00E9"),
        ("\u00C3\u00AA", "\u00EA"),
        ("\u00C3\u00AB", "\u00EB"),
        ("\u00C3\u00AC", "\u00EC"),
        ("\u00C3\u00AD", "\u00ED"),
        ("\u00C3\u00AE", "\u00EE"),
        ("\u00C3\u00AF", "\u00EF"),
        ("\u00C3\u00B1", "\u00F1"),
        ("\u00C3\u00B2", "\u00F2"),
        ("\u00C3\u00B3", "\u00F3"),
        ("\u00C3\u00B4", "\u00F4"),
        ("\u00C3\u00B5", "\u00F5"),
        ("\u00C3\u00B6", "\u00F6"),
        ("\u00C3\u00B9", "\u00F9"),
        ("\u00C3\u00BA", "\u00FA"),
        ("\u00C3\u00BB", "\u00FB"),
        ("\u00C3\u00BC", "\u00FC"),
        ("\u00C3\u00BD", "\u00FD"),
        ("\u00C3\u00BF", "\u00FF"),
        ("\u00E2\u20AC\u2122", "'"),
        ("\u00E2\u20AC\u0153", "\""),
        ("\u00E2\u20AC\u009D", "\""),
        ("\u00E2\u20AC\u0093", "-"),
        ("\u00E2\u20AC\u0094", "-"),
        ("\u00E2\u20AC\u00A6", "..."),
    ];

    public static string RepairMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var repaired = value;
        foreach (var (broken, fixedValue) in Replacements)
        {
            repaired = repaired.Replace(broken, fixedValue, StringComparison.Ordinal);
        }

        return repaired;
    }
}
