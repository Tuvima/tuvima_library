namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Exposes the active profile selected by the current Dashboard circuit to
/// server-side integration services. This state is never serialized to the browser.
/// </summary>
public interface IActiveProfileAccessor
{
    Guid? ProfileId { get; }
}

public sealed class ActiveProfileAccessor : IActiveProfileAccessor
{
    public Guid? ProfileId { get; private set; }

    public void SetProfile(Guid? profileId) => ProfileId = profileId;
}
