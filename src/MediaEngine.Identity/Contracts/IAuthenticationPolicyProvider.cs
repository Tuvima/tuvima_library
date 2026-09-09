using MediaEngine.Domain.Configuration;

namespace MediaEngine.Identity.Contracts;

public interface IAuthenticationPolicyProvider
{
    AuthSettings GetCurrent();
}
