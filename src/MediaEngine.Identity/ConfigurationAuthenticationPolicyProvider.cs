using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Identity;

public sealed class ConfigurationAuthenticationPolicyProvider(IConfigurationLoader configuration)
    : IAuthenticationPolicyProvider
{
    public AuthSettings GetCurrent() => configuration.LoadCore().Auth;
}
