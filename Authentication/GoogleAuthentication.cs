using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskApi.Configuration;

namespace TaskApi.Authentication;

public static class GoogleAuthentication
{
    public const string VerifiedEmailClaim = "urn:google:email_verified";
    public const string FailurePath = "/login.html?google=failed";

    public static bool MatchesOrigin(HttpRequest request, string origin) =>
        string.Equals($"{request.Scheme}://{request.Host}", origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static void AddTaskBoardGoogle(this IServiceCollection services, bool development)
    {
        services.AddOptions<GoogleLoginOptions>().BindConfiguration("Authentication:Google")
            .Validate(settings => settings.IsValid(), "Enabled Google login requires an OAuth web client ID and secret.")
            .ValidateOnStart();
        services.AddAuthentication().AddGoogle();
        services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
            .Configure<IOptions<GoogleLoginOptions>>((options, settings) =>
            {
                options.ClientId = settings.Value.Enabled ? settings.Value.ClientId : "disabled";
                options.ClientSecret = settings.Value.Enabled ? settings.Value.ClientSecret : "disabled";
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = false;
                options.UsePkce = true;
                options.AccessType = "online";
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
                options.ClaimActions.MapJsonKey(VerifiedEmailClaim, "email_verified");
                options.ClaimActions.MapJsonKey("urn:google:hd", "hd");
                // Callback uses a top-level GET, so Lax also supports local HTTP development.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Events.OnCreatingTicket = context =>
                {
                    if (!settings.Value.Enabled) throw new InvalidOperationException("Google login is disabled.");
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = async context =>
                {
                    await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                    context.Response.Redirect(FailurePath);
                    context.HandleResponse();
                };
            });
        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = development ? "TaskBoard.External" : "__Host-TaskBoard.External";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        });
    }
}
