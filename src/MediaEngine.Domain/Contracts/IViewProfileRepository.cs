using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Contracts;

public interface IViewProfileRepository
{
    Task<ViewProfilePolicy> GetPolicyAsync(Guid profileId, CancellationToken ct = default);
    Task<bool> SavePolicyAsync(ViewProfilePolicy policy, CancellationToken ct = default);
    Task<ViewProfilePreferences> GetPreferencesAsync(Guid profileId, CancellationToken ct = default);
    Task<bool> SavePreferencesAsync(ViewProfilePreferences preferences, CancellationToken ct = default);
}
