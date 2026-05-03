namespace MediaEngine.Web.Services.Branding;

public sealed class StreamingServiceLogoResolver
{
    private const string BasePath = "/images/streaming-services";

    private static readonly IReadOnlyDictionary<string, string> LogoPathsByAlias = BuildAliasMap();

    public string? ResolveLogoPath(string? label)
    {
        var key = NormalizeAlias(label);
        return key.Length == 0
            ? null
            : LogoPathsByAlias.GetValueOrDefault(key);
    }

    internal static string NormalizeAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                buffer[index++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..index]);
    }

    private static IReadOnlyDictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        Add(map, "netflix", "Netflix");
        Add(map, "disney-plus", "Disney+", "Disney Plus");
        Add(map, "hulu", "Hulu", "FX on Hulu", "Hulu on Disney+", "Hulu Original");
        Add(map, "max", "Max", "HBO Max", "HBO", "HBO Max Original");
        Add(map, "prime-video", "Prime Video", "Amazon Prime Video", "Prime");
        Add(map, "apple-tv-plus", "Apple TV+", "Apple TV Plus", "Apple TV", "Apple");
        Add(map, "paramount-plus", "Paramount+", "Paramount Plus", "Paramount");
        Add(map, "peacock", "Peacock", "Peacock TV");
        Add(map, "tubi", "Tubi", "Tubi TV");
        Add(map, "pluto-tv", "Pluto TV", "Pluto");
        Add(map, "roku-channel", "The Roku Channel", "Roku Channel", "Roku");
        Add(map, "crunchyroll", "Crunchyroll");
        Add(map, "youtube", "YouTube");
        Add(map, "youtube-tv", "YouTube TV", "Youtube TV");
        Add(map, "starz", "STARZ", "Starz");
        Add(map, "showtime", "Showtime", "SHOWTIME");
        Add(map, "amc-plus", "AMC+", "AMC Plus", "AMC");
        Add(map, "britbox", "BritBox", "Britbox");
        Add(map, "acorn-tv", "Acorn TV", "Acorn");
        Add(map, "shudder", "Shudder");
        Add(map, "mubi", "MUBI", "Mubi");
        Add(map, "criterion-channel", "Criterion Channel", "The Criterion Channel", "Criterion", "The Criterion Collection");
        Add(map, "fandango-at-home", "Fandango at Home", "Vudu", "Fandango", "FandangoNow");
        Add(map, "plex", "Plex");
        Add(map, "kanopy", "Kanopy");
        Add(map, "hoopla", "Hoopla", "Hoopla Digital");
        Add(map, "discovery-plus", "Discovery+", "Discovery Plus", "Discovery");
        Add(map, "mgm-plus", "MGM+", "MGM Plus", "Epix", "MGM");
        Add(map, "dazn", "DAZN");
        Add(map, "espn", "ESPN", "ESPN+", "ESPN Plus");

        return map;
    }

    private static void Add(Dictionary<string, string> map, string serviceId, params string[] aliases)
    {
        var path = $"{BasePath}/{serviceId}.png";
        foreach (var alias in aliases)
        {
            var key = NormalizeAlias(alias);
            if (key.Length > 0)
                map[key] = path;
        }
    }
}
