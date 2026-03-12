namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Identifies where a phrase will appear in the UI.
/// Each slot has its own pool of templates in the narration config.
/// </summary>
public enum PhraseSlot
{
    // ── Hero subtitles ───────────────────────────────────────────────
    HeroJourneySeries,
    HeroJourneyStandalone,
    HeroDiscoverSeries,
    HeroDiscoverStandalone,
    HeroDiscoverAuthor,
    HeroCompleted,

    // ── Swimlane headings ────────────────────────────────────────────
    SwimlaneRecentlyAdded,
    SwimlaneContinue,
    SwimlaneByPerson,
    SwimlaneByGenre,
    SwimlaneBySeries,

    // ── Page-level ───────────────────────────────────────────────────
    WelcomeGreeting,
    EmptyState
}
