using System.Text.Json;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Seeds default managed hubs (System Lists, Personalized Mixes, and sample Smart Hubs)
/// on first run when no non-Universe hubs exist. Called during application startup.
/// Also seeds system view hubs (idempotent) that drive all Vault views.
/// </summary>
public static class HubSeeder
{
    public static async Task SeedManagedHubsAsync(IHubRepository hubRepo, CancellationToken ct = default)
    {
        // Always seed system view hubs (idempotent — checks by rule_hash)
        await SeedSystemViewHubsAsync(hubRepo, ct);

        var counts = await hubRepo.GetCountsByTypeAsync(ct);
        // System hubs now always exist; check for non-System types
        var nonSystemCount = counts.Where(c => c.Key != "System").Sum(c => c.Value);
        if (nonSystemCount > 0)
            return; // Already seeded

        var now = DateTimeOffset.UtcNow;

        // ── System Lists ────────────────────────────────────────────────────
        var systemLists = new[]
        {
            ("Reading List",      "Books, Comics — per user, progress tracked",    "MenuBook",    "Books, Comics"),
            ("Watchlist",         "Movies — per user, progress tracked",            "Visibility",  "Movies"),
            ("Currently Watching","TV — per user, progress tracked",                "LiveTv",      "TV"),
            ("Listening Queue",   "Audiobooks, Music, Podcasts — per user, progress tracked", "Headphones", "Audiobooks, Music, Podcasts"),
            ("Favorites",         "Any media — per user",                           "Favorite",    "Any"),
        };

        foreach (var (name, desc, icon, _) in systemLists)
        {
            await hubRepo.UpsertAsync(new Hub
            {
                Id          = Guid.NewGuid(),
                DisplayName = name,
                HubType     = "System",
                Description = desc,
                IconName    = icon,
                Scope       = "library",
                IsEnabled   = true,
                MinItems    = 0,
                CreatedAt   = now,
            }, ct);
        }

        // ── Personalized Mixes ──────────────────────────────────────────────
        var mixes = new[]
        {
            ("Continue",              "In-progress items across all lists",                  "PlayCircle",   "Real-time"),
            ("Heavy Rotation",        "Most consumed in last 30 days",                      "Fire",         "Daily"),
            ("Discovery Queue",       "Matches your taste, untouched",                      "Explore",      "Daily"),
            ("New For You",           "Recently added, matches taste profile",              "AutoAwesome",  "On ingestion"),
            ("Because You Liked...",  "Similar to highly-rated items",                      "Link",         "On rating change"),
            ("Taste Mix",             "Per taste cluster",                                   "Palette",      "Weekly"),
            ("On Repeat",             "High consumption recently",                          "Repeat",       "Daily"),
            ("Rediscover",            "Loved 6+ months ago, untouched since",               "History",      "Weekly"),
        };

        foreach (var (name, desc, icon, schedule) in mixes)
        {
            await hubRepo.UpsertAsync(new Hub
            {
                Id              = Guid.NewGuid(),
                DisplayName     = name,
                HubType         = "Mix",
                Description     = desc,
                IconName        = icon,
                Scope           = "user",
                IsEnabled       = true,
                MinItems        = 0,
                RefreshSchedule = schedule,
                CreatedAt       = now,
            }, ct);
        }

        // ── Sample Smart Hubs ───────────────────────────────────────────────
        var smartHubs = new[]
        {
            ("By Genre: Science Fiction", "Genre: Science Fiction, Min 3 items, Any media", "Label",    128, """[{"field":"genre","op":"eq","value":"Science Fiction"}]"""),
            ("By Genre: Mystery",         "Genre: Mystery, Min 3 items, Any media",         "Label",     64, """[{"field":"genre","op":"eq","value":"Mystery"}]"""),
            ("By Genre: Biography",       "Genre: Biography, Min 3 items, Any media",       "Label",     22, """[{"field":"genre","op":"eq","value":"Biography"}]"""),
            ("By Vibe: Atmospheric",      "Vibe: atmospheric, Min 5 items, Any media",      "Waves",     43, """[{"field":"vibe","op":"eq","value":"atmospheric"}]"""),
            ("By Vibe: Cozy",             "Vibe: cozy, Min 5 items, Any media",             "Waves",     18, """[{"field":"vibe","op":"eq","value":"cozy"}]"""),
            ("By Vibe: Cerebral",         "Vibe: cerebral, Min 5 items, Any media",         "Waves",     31, """[{"field":"vibe","op":"eq","value":"cerebral"}]"""),
            ("By Author: Frank Herbert",  "Author role, Min 3 works",                       "Person",    12, """[{"field":"author","op":"eq","value":"Frank Herbert"}]"""),
            ("By Director: Denis Villeneuve", "Director role, Min 3 works",                 "Person",     7, """[{"field":"director","op":"eq","value":"Denis Villeneuve"}]"""),
            ("By Narrator: Steven Pacey", "Narrator role, Min 3 works",                     "Person",     9, """[{"field":"narrator","op":"eq","value":"Steven Pacey"}]"""),
            ("By Decade: 1980s",          "Published/released 1980-1989, Min 5 items",      "Calendar",  56, """[{"field":"decade","op":"eq","value":"1980s"}]"""),
            ("By Decade: 2010s",          "Published/released 2010-2019, Min 5 items",      "Calendar", 203, """[{"field":"decade","op":"eq","value":"2010s"}]"""),
            ("Recently Added",            "Added in last 30 days",                           "Clock",     34, """[{"field":"added_within_days","op":"lte","value":"30"}]"""),
            ("Highest Rated",             "Rating 4+ from providers",                        "Star",      89, """[{"field":"provider_rating","op":"gte","value":"4"}]"""),
            ("Unrated",                   "No user rating",                                  "RadioButton",412, """[{"field":"user_rating","op":"eq","value":"unrated"}]"""),
        };

        foreach (var (name, desc, icon, mockCount, ruleJson) in smartHubs)
        {
            await hubRepo.UpsertAsync(new Hub
            {
                Id          = Guid.NewGuid(),
                DisplayName = name,
                HubType     = "Smart",
                Description = desc,
                IconName    = icon,
                Scope       = "library",
                IsEnabled   = true,
                MinItems    = name.Contains("Vibe") ? 5 : 3,
                RuleJson    = ruleJson,
                CreatedAt   = now,
            }, ct);
        }

        // ── Sample Playlists ────────────────────────────────────────────────
        var playlists = new[]
        {
            ("Workout Mix",      "User-created playlist", "QueueMusic", 24),
            ("Movie Marathon",   "User-created playlist", "QueueMusic",  8),
            ("Commute Rotation", "User-created playlist", "QueueMusic", 16),
        };

        foreach (var (name, desc, icon, _) in playlists)
        {
            await hubRepo.UpsertAsync(new Hub
            {
                Id          = Guid.NewGuid(),
                DisplayName = name,
                HubType     = "Playlist",
                Description = desc,
                IconName    = icon,
                Scope       = "user",
                IsEnabled   = true,
                MinItems    = 0,
                CreatedAt   = now,
            }, ct);
        }
    }

    /// <summary>
    /// Seeds permanent, non-editable System view hubs that drive all Vault views.
    /// Idempotent — checks by rule_hash before creating. These hubs are query-resolved
    /// (evaluated at display time via HubRuleEvaluator) and cannot be deleted or edited.
    /// </summary>
    private static async Task SeedSystemViewHubsAsync(IHubRepository hubRepo, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Each entry: (display_name, description, icon, rule predicates, group_by_field, sort_field)
        var viewHubs = new (string Name, string Desc, string Icon, HubRulePredicate[] Rules, string? GroupBy, string? Sort)[]
        {
            // ── Media views ──────────────────────────────────────────
            ("Recently Added", "Items added in the last 30 days",
                "NewReleases",
                [new() { Field = "added_within_days", Op = "lte", Value = "30" }],
                null, "newest"),

            ("All Movies", "Every movie in your library",
                "VideoLibrary",
                [new() { Field = "media_type", Op = "eq", Value = "Movies" }],
                null, "title"),

            ("TV by Show", "TV episodes grouped by show",
                "LiveTv",
                [new() { Field = "media_type", Op = "eq", Value = "TV" }],
                "show_name", "newest"),

            ("Music by Artist", "Music grouped by artist",
                "MusicNote",
                [new() { Field = "media_type", Op = "eq", Value = "Music" }],
                "artist", "title"),

            ("Music by Album", "Music grouped by album",
                "Album",
                [new() { Field = "media_type", Op = "eq", Value = "Music" }],
                "album", "title"),

            ("All Songs", "Every song in your library",
                "QueueMusic",
                [new() { Field = "media_type", Op = "eq", Value = "Music" }],
                null, "title"),

            ("All Books", "Every book in your library",
                "MenuBook",
                [new() { Field = "media_type", Op = "eq", Value = "Books" }],
                null, "title"),

            ("All Audiobooks", "Every audiobook in your library",
                "Headphones",
                [new() { Field = "media_type", Op = "eq", Value = "Audiobooks" }],
                null, "title"),

            ("Podcasts by Show", "Podcast episodes grouped by show",
                "Mic",
                [new() { Field = "media_type", Op = "eq", Value = "Podcasts" }],
                "show_name", "newest"),

            ("All Comics", "Every comic in your library",
                "AutoStories",
                [new() { Field = "media_type", Op = "eq", Value = "Comics" }],
                null, "title"),
        };

        foreach (var (name, desc, icon, rules, groupBy, sort) in viewHubs)
        {
            var ruleJson = JsonSerializer.Serialize(rules);
            var ruleHash = HubRuleEvaluator.ComputeRuleHash(rules);

            // Skip if a hub with the same rule_hash already exists
            var existing = await hubRepo.FindByRuleHashAsync(ruleHash, ct);
            if (existing is not null) continue;

            await hubRepo.UpsertAsync(new Hub
            {
                Id            = Guid.NewGuid(),
                DisplayName   = name,
                HubType       = "System",
                Description   = desc,
                IconName      = icon,
                Scope         = "library",
                IsEnabled     = true,
                MinItems      = 0,
                RuleJson      = ruleJson,
                RuleHash      = ruleHash,
                Resolution    = "query",
                GroupByField  = groupBy,
                SortField     = sort,
                SortDirection = sort == "newest" ? "desc" : "asc",
                LiveUpdating  = true,
                CreatedAt     = now,
            }, ct);
        }
    }
}
