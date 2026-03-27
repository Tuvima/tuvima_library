using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Seeds default managed hubs (System Lists, Personalized Mixes, and sample Smart Hubs)
/// on first run when no non-Universe hubs exist. Called during application startup.
/// </summary>
public static class HubSeeder
{
    public static async Task SeedManagedHubsAsync(IHubRepository hubRepo, CancellationToken ct = default)
    {
        var counts = await hubRepo.GetCountsByTypeAsync(ct);
        if (counts.Count > 0)
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
            ("By Genre: Science Fiction", "Genre: Science Fiction, Min 3 items, Any media", "Label",    128, """{"genre":"Science Fiction","min":3,"media":"Any"}"""),
            ("By Genre: Mystery",         "Genre: Mystery, Min 3 items, Any media",         "Label",     64, """{"genre":"Mystery","min":3,"media":"Any"}"""),
            ("By Genre: Biography",       "Genre: Biography, Min 3 items, Any media",       "Label",     22, """{"genre":"Biography","min":3,"media":"Any"}"""),
            ("By Vibe: Atmospheric",      "Vibe: atmospheric, Min 5 items, Any media",      "Waves",     43, """{"vibe":"atmospheric","min":5,"media":"Any"}"""),
            ("By Vibe: Cozy",             "Vibe: cozy, Min 5 items, Any media",             "Waves",     18, """{"vibe":"cozy","min":5,"media":"Any"}"""),
            ("By Vibe: Cerebral",         "Vibe: cerebral, Min 5 items, Any media",         "Waves",     31, """{"vibe":"cerebral","min":5,"media":"Any"}"""),
            ("By Author: Frank Herbert",  "Author role, Min 3 works",                       "Person",    12, """{"person":"Frank Herbert","role":"Author","min":3}"""),
            ("By Director: Denis Villeneuve", "Director role, Min 3 works",                 "Person",     7, """{"person":"Denis Villeneuve","role":"Director","min":3}"""),
            ("By Narrator: Steven Pacey", "Narrator role, Min 3 works",                     "Person",     9, """{"person":"Steven Pacey","role":"Narrator","min":3}"""),
            ("By Decade: 1980s",          "Published/released 1980-1989, Min 5 items",      "Calendar",  56, """{"decade":"1980s","min":5}"""),
            ("By Decade: 2010s",          "Published/released 2010-2019, Min 5 items",      "Calendar", 203, """{"decade":"2010s","min":5}"""),
            ("Recently Added",            "Added in last 30 days",                           "Clock",     34, """{"recency_days":30}"""),
            ("Highest Rated",             "Rating 4+ from providers",                        "Star",      89, """{"min_rating":4}"""),
            ("Unrated",                   "No user rating",                                  "RadioButton",412, """{"unrated":true}"""),
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
}
