using MediaEngine.Api.Security;
using MediaEngine.Domain.Authorization;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Services.View;

public interface IViewRequestProfileContext
{
    ValueTask<RequestAuthority> ResolveAuthorityAsync(CancellationToken ct = default);
}

/// <summary>
/// Resolves View identity only through the common authenticated request authority.
/// Query-string, route, and browser-selected profile identifiers are never authority.
/// </summary>
public sealed class HttpViewRequestProfileContext : IViewRequestProfileContext
{
    private readonly IHttpContextAccessor accessor;
    private readonly IRequestAuthorityResolver authorityResolver;

    public HttpViewRequestProfileContext(IHttpContextAccessor accessor, IRequestAuthorityResolver authorityResolver)
    {
        this.accessor = accessor;
        this.authorityResolver = authorityResolver;
    }

    public ValueTask<RequestAuthority> ResolveAuthorityAsync(CancellationToken ct = default)
    {
        var context = accessor.HttpContext;
        if (context is null)
        {
            return ValueTask.FromResult(new RequestAuthority(PrincipalKind.Anonymous, false));
        }

        return authorityResolver.ResolveAsync(context, ct);
    }

}

