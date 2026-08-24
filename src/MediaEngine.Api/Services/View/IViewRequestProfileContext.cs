using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Services.View;

public interface IViewRequestProfileContext
{
    ViewRequestProfile? Current { get; }
}

/// <summary>
/// Reads identity only from server-populated request state. Query-string,
/// route, and browser-selected profile identifiers are intentionally ignored.
/// Authentication middleware should call <see cref="SetTrustedProfile"/> after
/// it has associated a credential/session with a profile.
/// </summary>
public sealed class HttpViewRequestProfileContext(IHttpContextAccessor accessor)
    : IViewRequestProfileContext
{
    private static readonly object ItemKey = new();

    public ViewRequestProfile? Current =>
        accessor.HttpContext?.Items.TryGetValue(ItemKey, out var value) == true
            ? value as ViewRequestProfile
            : null;

    public static void SetTrustedProfile(HttpContext context, ViewRequestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        context.Items[ItemKey] = profile;
    }
}

