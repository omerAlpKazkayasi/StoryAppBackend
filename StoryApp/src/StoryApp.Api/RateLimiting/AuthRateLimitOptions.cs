namespace StoryApp.Api.RateLimiting;

public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    public int GuestPermitLimit { get; set; } = 30;
    public int RefreshPermitLimit { get; set; } = 60;
    public int GoogleLoginPermitLimit { get; set; } = 30;
    public int GoogleUpgradePermitLimit { get; set; } = 30;
    public int WindowMinutes { get; set; } = 1;
}

public static class RateLimitPolicies
{
    public const string Guest = "auth-guest";
    public const string Refresh = "auth-refresh";
    public const string GoogleLogin = "auth-google-login";
    public const string GoogleUpgrade = "auth-google-upgrade";
}
