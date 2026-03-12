namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A resolved piece of display text produced by the narration system.
/// Consumed by hero banners, swimlane headers, and insight cards.
/// </summary>
public sealed record DisplayPhrase(
    string Text,
    string? Icon = null,
    PhraseIntent Intent = PhraseIntent.Neutral);

/// <summary>Semantic intent of a phrase — drives styling in future insight cards.</summary>
public enum PhraseIntent
{
    Neutral,
    Encouragement,
    Discovery,
    Streak,
    Milestone
}
