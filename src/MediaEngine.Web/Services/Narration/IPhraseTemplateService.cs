using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Narration;

/// <summary>
/// Resolves phrase templates by substituting context variables.
/// Stateless, config-driven, deterministic. Registered as Singleton.
/// </summary>
public interface IPhraseTemplateService
{
    /// <summary>
    /// Resolves a phrase for the given slot, picking deterministically
    /// from available templates and substituting context variables.
    /// </summary>
    DisplayPhrase Resolve(PhraseSlot slot, PhraseContext context);
}
