using Microsoft.AspNetCore.Components;

namespace MediaEngine.Web.Services.Integration;

public sealed class AdministratorElevationNavigationService(
    DashboardIdentityClient identity,
    NavigationManager navigation)
{
    public async Task<bool> EnsureElevatedAsync(CancellationToken ct = default)
    {
        var elevation = await identity.GetElevationAsync(ct);
        if (elevation?.Elevated == true && elevation.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        var current = new Uri(navigation.Uri).PathAndQuery;
        navigation.NavigateTo($"/account/elevate?returnUrl={Uri.EscapeDataString(current)}", forceLoad: true);
        return false;
    }
}
