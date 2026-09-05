namespace MediaEngine.Web.Components.Shared.Providers;

public enum ProviderOnboardingOperation
{
    Connect,
    Replace,
    Test,
}

public sealed record ProviderOnboardingDialogResult(
    bool Success,
    string Status,
    string Message);
