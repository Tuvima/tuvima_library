namespace MediaEngine.Web.Services.Navigation;

public static class ListenNavigation
{
    public const string MusicHomeRoute = "/listen/music";
    public const string AlbumsRoute = "/listen/music/albums";
    public const string ArtistsRoute = "/listen/music/artists";
    public const string SongsRoute = "/listen/music/songs";
    public const string PlaylistsRoute = "/listen/music/playlists";
    public const string AudiobooksRoute = "/listen/audiobooks";

    public static string ResolveEntryRoute(string? savedMode, string? savedMusicRoute)
        => string.Equals(savedMode, "audiobooks", StringComparison.OrdinalIgnoreCase)
            ? AudiobooksRoute
            : MusicHomeRoute;

    public static string NormalizeMusicSurfaceRoute(string? route)
    {
        var path = NormalizeRoute(route);
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/listen/music", StringComparison.OrdinalIgnoreCase))
        {
            return MusicHomeRoute;
        }

        if (string.Equals(path, MusicHomeRoute, StringComparison.OrdinalIgnoreCase))
        {
            return MusicHomeRoute;
        }

        if (path.StartsWith(ArtistsRoute, StringComparison.OrdinalIgnoreCase))
        {
            return ArtistsRoute;
        }

        if (path.StartsWith(SongsRoute, StringComparison.OrdinalIgnoreCase))
        {
            return SongsRoute;
        }

        if (path.StartsWith(PlaylistsRoute, StringComparison.OrdinalIgnoreCase))
        {
            return PlaylistsRoute;
        }

        return AlbumsRoute;
    }

    public static bool IsMusicRoute(string? route)
    {
        var path = NormalizeRoute(route);
        return !string.IsNullOrWhiteSpace(path)
               && path.StartsWith("/listen/music", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        return Uri.TryCreate(route, UriKind.Absolute, out var absoluteRoute)
            ? absoluteRoute.PathAndQuery
            : route;
    }
}
