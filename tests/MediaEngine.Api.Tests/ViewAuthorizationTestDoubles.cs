using MediaEngine.Api.Security;
using MediaEngine.Api.Services.View;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Tests;

internal sealed class TestViewAuthorityResolver(RequestAuthority authority) : IRequestAuthorityResolver
{
    public RequestAuthority Authority => authority;
    public ValueTask<RequestAuthority> ResolveAsync(HttpContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(authority);

    public static TestViewAuthorityResolver Anonymous { get; } =
        new(new RequestAuthority(PrincipalKind.Anonymous, false));

    public static TestViewAuthorityResolver Human(Guid profileId) =>
        new(new RequestAuthority(PrincipalKind.Human, true, Guid.NewGuid(), profileId,
            AccountEnabled: true, GrantEnabled: true));
}

internal sealed class TestAllowAuthorizationEvaluator : IAuthorizationEvaluator
{
    public AuthorizationRequirement? LastRequirement { get; private set; }

    public ValueTask<AuthorizationDecision> EvaluateAsync(RequestAuthority authority,
        AuthorizationRequirement requirement, ResourceAuthorizationContext? resource,
        CancellationToken cancellationToken = default)
    {
        LastRequirement = requirement;
        return ValueTask.FromResult(
            authority.IsAuthenticated
                && (!authority.HasHumanContext || authority.AccountEnabled && authority.GrantEnabled)
                && (!authority.HasApplicationContext || authority.ApplicationEnabled)
                ? AuthorizationDecision.Allow()
                : AuthorizationDecision.Deny(AuthorizationDenialReason.DisabledPrincipal));
    }
}
