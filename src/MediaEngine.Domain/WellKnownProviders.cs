namespace MediaEngine.Domain;

/// <summary>
/// Single source of truth for all well-known provider GUIDs.
/// Every provider identity reference in the codebase must use these constants
/// instead of inline GUID strings or local static fields.
/// </summary>
public static class WellKnownProviders
{
    /// <summary>File metadata extractor — reads embedded tags from media files.</summary>
    public static readonly Guid LocalProcessor = Guid.Parse("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f");

    /// <summary>Filesystem scanner — discovers files during library import.</summary>
    public static readonly Guid LibraryScanner = Guid.Parse("c9d8e7f6-a5b4-4321-fedc-0102030405c9");

    /// <summary>User manual match — claims created by user edits.</summary>
    public static readonly Guid UserManual = Guid.Parse("d0000000-0000-4000-8000-000000000001");

    /// <summary>Apple API (Books + Audiobooks) retail provider.</summary>
    public static readonly Guid AppleApi = Guid.Parse("b1000001-e000-4000-8000-000000000001");

    /// <summary>Wikidata Reconciliation — canonical authority for identity resolution.</summary>
    public static readonly Guid Wikidata = Guid.Parse("b3000003-d000-4000-8000-000000000004");

    /// <summary>Wikipedia — description extracts via Wikidata sitelinks.</summary>
    public static readonly Guid Wikipedia = Guid.Parse("b4000004-d000-4000-8000-000000000005");

    /// <summary>Open Library — book metadata and cover art.</summary>
    public static readonly Guid OpenLibrary = Guid.Parse("b4000004-0000-4000-8000-000000000005");

    /// <summary>Google Books — book search and metadata.</summary>
    public static readonly Guid GoogleBooks = Guid.Parse("b5000005-0000-4000-8000-000000000006");

    /// <summary>MusicBrainz — music metadata and release groups.</summary>
    public static readonly Guid MusicBrainz = Guid.Parse("b6000006-0000-4000-8000-000000000007");

    /// <summary>TMDB — movie and TV metadata, images.</summary>
    public static readonly Guid Tmdb = Guid.Parse("b7000007-0000-4000-8000-000000000008");

    /// <summary>Metron — comic book metadata.</summary>
    public static readonly Guid Metron = Guid.Parse("b8000008-0000-4000-8000-000000000009");

    /// <summary>AI-generated claims (Description Intelligence, TL;DR, Vibe Tags, etc.).</summary>
    public static readonly Guid AiProvider = Guid.Parse("bb00000b-0000-4000-8000-000000000012");

    /// <summary>Pseudonym provider — synthetic claims linking pen names to real authors.</summary>
    public static readonly Guid Pseudonym = Guid.Parse("ffa00001-0000-4000-8000-000000000099");

    /// <summary>Fanart.tv — rich imagery (backdrops, logos, banners, character art).</summary>
    public static readonly Guid FanartTv = Guid.Parse("bc00000c-0000-4000-8000-000000000013");

    /// <summary>Comic Vine — comic book metadata and cover art.</summary>
    public static readonly Guid ComicVine = Guid.Parse("b9000009-0000-4000-8000-000000000014");

    /// <summary>Returns true if the provider is a file/local source (LocalProcessor or LibraryScanner).</summary>
    public static bool IsFileSource(Guid providerId) =>
        providerId == LocalProcessor || providerId == LibraryScanner;

    /// <summary>Returns true if the provider represents a user manual match.</summary>
    public static bool IsUserSource(Guid providerId) =>
        providerId == UserManual;
}
