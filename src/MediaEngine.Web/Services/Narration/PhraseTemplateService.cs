using System.Text.Json;
using System.Text.RegularExpressions;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Narration;

/// <summary>
/// Singleton service that resolves phrase templates from a config file
/// or compiled defaults. Selection is deterministic per entity per day.
/// </summary>
public sealed partial class PhraseTemplateService : IPhraseTemplateService
{
    private readonly Dictionary<string, string[]> _templates;

    public PhraseTemplateService(IWebHostEnvironment env)
    {
        _templates = LoadTemplates(env.ContentRootPath) ?? DefaultTemplates;
    }

    public DisplayPhrase Resolve(PhraseSlot slot, PhraseContext context)
    {
        var slotKey = slot.ToString();
        if (!_templates.TryGetValue(slotKey, out var pool) || pool.Length == 0)
            return new DisplayPhrase(string.Empty);

        // Build substitution dictionary from context
        var vars = BuildVars(context);

        // Filter out templates with unresolvable placeholders
        var candidates = pool
            .Where(t => AllPlaceholdersResolvable(t, vars))
            .ToArray();

        if (candidates.Length == 0)
        {
            // Fall back to standalone variants if all templates need missing fields
            var fallbackKey = slot switch
            {
                PhraseSlot.HeroJourneySeries => PhraseSlot.HeroJourneyStandalone.ToString(),
                PhraseSlot.HeroDiscoverSeries or PhraseSlot.HeroDiscoverAuthor
                    => PhraseSlot.HeroDiscoverStandalone.ToString(),
                _ => null
            };

            if (fallbackKey is not null &&
                _templates.TryGetValue(fallbackKey, out var fallbackPool) &&
                fallbackPool.Length > 0)
            {
                candidates = fallbackPool
                    .Where(t => AllPlaceholdersResolvable(t, vars))
                    .ToArray();
            }

            if (candidates.Length == 0)
                return new DisplayPhrase(string.Empty);
        }

        // Deterministic selection: same entity gets same phrase for the day
        var seed = DateTime.UtcNow.DayOfYear;
        if (context.EntityId.HasValue)
            seed += Math.Abs(context.EntityId.Value.GetHashCode());

        var index = Math.Abs(seed) % candidates.Length;
        var template = candidates[index];
        var resolved = SubstitutePlaceholders(template, vars);

        var intent = slot switch
        {
            PhraseSlot.HeroJourneySeries or PhraseSlot.HeroJourneyStandalone
                => PhraseIntent.Encouragement,
            PhraseSlot.HeroDiscoverSeries or PhraseSlot.HeroDiscoverStandalone or PhraseSlot.HeroDiscoverAuthor
                => PhraseIntent.Discovery,
            PhraseSlot.HeroCompleted => PhraseIntent.Milestone,
            _ => PhraseIntent.Neutral,
        };

        return new DisplayPhrase(resolved, Intent: intent);
    }

    // ── Placeholder handling ─────────────────────────────────────────────

    private static Dictionary<string, string> BuildVars(PhraseContext ctx) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Title"] = ctx.Title ?? string.Empty,
        ["Author"] = ctx.Author ?? string.Empty,
        ["Series"] = ctx.Series ?? string.Empty,
        ["Genre"] = ctx.Genre ?? string.Empty,
        ["MediaType"] = ctx.MediaType ?? string.Empty,
        ["Count"] = ctx.Count?.ToString() ?? string.Empty,
    };

    private static bool AllPlaceholdersResolvable(string template, Dictionary<string, string> vars)
    {
        foreach (var match in PlaceholderRegex().Matches(template).Cast<Match>())
        {
            var key = match.Groups[1].Value;
            if (!vars.TryGetValue(key, out var val) || string.IsNullOrEmpty(val))
                return false;
        }
        return true;
    }

    private static string SubstitutePlaceholders(string template, Dictionary<string, string> vars)
    {
        return PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return vars.TryGetValue(key, out var val) ? val : match.Value;
        });
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    // ── Config loading ───────────────────────────────────────────────────

    private static Dictionary<string, string[]>? LoadTemplates(string contentRoot)
    {
        var configPath = Path.Combine(contentRoot, "config", "narration", "phrases.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);

            if (!doc.TryGetProperty("slots", out var slots))
                return null;

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in slots.EnumerateObject())
            {
                var phrases = prop.Value.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Cast<string>()
                    .ToArray();

                if (phrases.Length > 0)
                    result[prop.Name] = phrases;
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Compiled defaults ────────────────────────────────────────────────

    private static readonly Dictionary<string, string[]> DefaultTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Hero — in-progress with series
        [nameof(PhraseSlot.HeroJourneySeries)] =
        [
            "Your journey through {Series} continues",
            "Welcome back to {Series}",
            "The {Series} saga awaits",
            "{Series} has more in store",
            "Return to the world of {Series}",
        ],

        // Hero — in-progress without series
        [nameof(PhraseSlot.HeroJourneyStandalone)] =
        [
            "Pick up where you left off",
            "Your story continues",
            "Welcome back",
            "Ready when you are",
            "Let's continue",
        ],

        // Hero — new item with series
        [nameof(PhraseSlot.HeroDiscoverSeries)] =
        [
            "Begin your {Series} journey",
            "Step into the world of {Series}",
            "Discover {Series}",
            "{Series} awaits",
            "A new chapter in {Series}",
        ],

        // Hero — new item without series or author
        [nameof(PhraseSlot.HeroDiscoverStandalone)] =
        [
            "Ready to discover something new?",
            "Your next favorite starts here",
            "Waiting to be discovered",
            "A fresh story awaits",
            "Something new for your collection",
        ],

        // Hero — new item with author
        [nameof(PhraseSlot.HeroDiscoverAuthor)] =
        [
            "A tale from {Author}",
            "From the mind of {Author}",
            "Discover {Author}",
            "New from {Author}",
            "{Author} invites you in",
        ],

        // Hero — completed item
        [nameof(PhraseSlot.HeroCompleted)] =
        [
            "A journey well traveled",
            "One for the collection",
            "Another chapter closed",
            "Mission accomplished",
            "A story complete",
        ],

        // Swimlane headings
        [nameof(PhraseSlot.SwimlaneRecentlyAdded)] =
        [
            "Fresh off the shelf",
            "Just arrived",
            "New in your library",
            "{Count} new arrivals",
        ],

        [nameof(PhraseSlot.SwimlaneContinue)] =
        [
            "Pick up where you left off",
            "Your stories await",
            "Continue your journey",
        ],

        [nameof(PhraseSlot.SwimlaneByPerson)] =
        [
            "More from {Author}",
            "The world of {Author}",
            "Also by {Author}",
        ],

        [nameof(PhraseSlot.SwimlaneByGenre)] =
        [
            "Dive into {Genre}",
            "Explore {Genre}",
            "Your {Genre} collection",
        ],

        [nameof(PhraseSlot.SwimlaneBySeries)] =
        [
            "Next in {Series}",
            "The {Series} saga",
            "More from {Series}",
        ],

        [nameof(PhraseSlot.WelcomeGreeting)] =
        [
            "Welcome back",
            "Good to see you",
            "Your library awaits",
        ],

        [nameof(PhraseSlot.EmptyState)] =
        [
            "Your library is waiting to be filled",
            "Drop some files in to get started",
            "An empty shelf is full of possibilities",
        ],
    };
}
