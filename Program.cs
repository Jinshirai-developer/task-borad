using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using TaskApi.Configuration;
using TaskApi.Data;
using TaskApi.Services;
using TaskApi.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using TaskApi.Models;
using AuthSettings = TaskApi.Configuration.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<RequireAuthenticationAttribute>();
        options.Filters.Add<ValidateApiAntiforgeryAttribute>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var shouldLimit = context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path == "/signin-google"
            || context.Request.Path.StartsWithSegments("/health/ready");

        if (!shouldLimit)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"api:{clientAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"auth:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };
});
// Static frontend and API intentionally share one origin; no credentialed CORS is exposed.
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<TaskTagService>();
builder.Services.AddScoped<WeeklyReviewService>();
builder.Services.AddHostedService<TaskUndoCleanupWorker>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<TeamBillingService>();
builder.Services.AddScoped<IStripeTestGateway, StripeTestGateway>();
builder.Services.AddHostedService<BillingReconciliationWorker>();
builder.Services.AddScoped<PetService>();
builder.Services.AddScoped<PetCollectionService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<DesktopSignInService>();
builder.Services.AddScoped<EmailOutboxService>();
builder.Services.AddScoped<EmailOutboxDispatcher>();
builder.Services.AddScoped<ITransactionalEmailSender, SmtpTransactionalEmailSender>();
builder.Services.AddHostedService<EmailOutboxWorker>();

var development = builder.Environment.IsDevelopment();
var publicEnvironment = !development && !builder.Environment.IsEnvironment("Testing");
builder.Services.AddOptions<BillingOptions>().BindConfiguration("Billing")
    .Validate(settings => settings.IsValid(), "Billing only supports sandbox keys, a test monthly JPY price and a webhook secret. Live keys are rejected even when disabled.")
    .ValidateOnStart();
builder.Services.AddOptions<AuthSettings>().BindConfiguration("Authentication")
    .Validate(settings => Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == "/"
        && (uri.Scheme == "https" || (development && uri.Scheme == "http" && uri.IsLoopback)),
        "Authentication:PublicBaseUrl must be a trusted HTTPS origin (HTTP loopback is allowed only in Development).")
    .Validate(settings => settings.PublicEntryPath.Length <= 200
        && System.Text.RegularExpressions.Regex.IsMatch(settings.PublicEntryPath, @"^/(?:[A-Za-z0-9_-]+/)*$"),
        "Authentication:PublicEntryPath must be an absolute local path ending in a slash.")
    .Validate(settings => !publicEnvironment || !string.IsNullOrWhiteSpace(settings.KeyRingPath),
        "Authentication:KeyRingPath must point to a persistent protected volume in production.")
    .ValidateOnStart();
builder.Services.AddOptions<PublicProxyOptions>().BindConfiguration("PublicProxy")
    .Validate(settings => settings.Secret.Length == 0 || settings.Secret.Length is >= 32 and <= 128,
        "PublicProxy:Secret must be empty or contain 32 to 128 characters.")
    .ValidateOnStart();
builder.Services.AddOptions<EmailOptions>().BindConfiguration("Email")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Host)
        && settings.Port is > 0 and <= 65535
        && System.Net.Mail.MailAddress.TryCreate(settings.FromAddress, out _)
        && settings.Security is "StartTls" or "SslOnConnect" or "None",
        "Email SMTP host, port, sender and explicit TLS mode are required.")
    .Validate(settings => !publicEnvironment || settings.Security != "None",
        "Unencrypted SMTP is allowed only for local development/testing.")
    .ValidateOnStart();
builder.Services.AddOptions<LegalOptions>().BindConfiguration("Legal")
    .Validate(settings => !publicEnvironment || (settings.PublicReleaseReady
        && System.Net.Mail.MailAddress.TryCreate(settings.ContactEmail, out _)),
        "Configure Legal operator, contact, providers and retention before public deployment.")
    .ValidateOnStart();

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("TaskBoard.Identity.v1");
var keyRingPath = builder.Configuration["Authentication:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

builder.Services.AddIdentityCore<UserProfile>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
        // Legacy users have no email yet. Unique non-null email is enforced by the database and registration service.
        options.User.RequireUniqueEmail = false;
        // Correct credentials grant a restricted setup session. The global authorization filter gates app data.
        options.SignIn.RequireConfirmedEmail = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Tokens.EmailConfirmationTokenProvider = "TaskBoardEmailConfirmation";
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<SessionClaimsPrincipalFactory>()
    .AddDefaultTokenProviders()
    .AddTokenProvider<EmailConfirmationTokenProvider>("TaskBoardEmailConfirmation");
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 600_000);
builder.Services.AddScoped<IPasswordHasher<UserProfile>, LegacyPasswordHasher>();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(30));
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    }).AddIdentityCookies();
builder.Services.AddTaskBoardGoogle(development);
builder.Services.AddAuthorization();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = development ? "TaskBoard.Auth" : "__Host-TaskBoard.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<UserProfile>>();
        var user = context.Principal == null ? null : await users.GetUserAsync(context.Principal);
        if (user == null || context.Principal?.FindFirst(SessionClaimsPrincipalFactory.SessionClaim)?.Value != user.SessionVersion)
        {
            context.RejectPrincipal();
            return;
        }
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    };
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = development ? "TaskBoard.Csrf" : "__Host-TaskBoard.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DefaultConnection),
        "ConnectionStrings:DefaultConnection is required. Set ConnectionStrings__DefaultConnection in production.")
    .ValidateOnStart();

builder.Services.AddOptions<RegistrationOptions>()
    .Bind(builder.Configuration.GetSection(RegistrationOptions.SectionName))
    .Validate(
        options => options.MaxUsers is >= 1 and <= 10_000,
        "Registration:MaxUsers must be between 1 and 10,000.")
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var databaseOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    options.UseNpgsql(databaseOptions.DefaultConnection);
});

var app = builder.Build();
var frontendPath = Path.Combine(app.Environment.ContentRootPath, "frontend");
app.UseTaskBoardPublicProxy();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; "
        + "object-src 'none'; img-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'";

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});

app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/signin-google")
    {
        context.Response.Headers.CacheControl = "no-store";
        var origin = context.RequestServices.GetRequiredService<IOptions<AuthSettings>>().Value.PublicBaseUrl;
        if (!GoogleAuthentication.MatchesOrigin(context.Request, origin))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();

if (Directory.Exists(frontendPath))
{
    var frontendFileProvider = new PhysicalFileProvider(frontendPath);

    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = frontendFileProvider
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = frontendFileProvider
    });
}

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/health/ready", async (
    AppDbContext database,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await database.Database.CanConnectAsync(cancellationToken);

        if (!canConnect)
        {
            return Results.Problem(
                title: "Database unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (database.Database.IsRelational())
        {
            var pendingMigrations = await database.Database.GetPendingMigrationsAsync(cancellationToken);

            if (pendingMigrations.Any())
            {
                return Results.Problem(
                    title: "Database migration required",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        return Results.Ok(new { status = "ready" });
    }
    catch (Exception error)
    {
        loggerFactory.CreateLogger("Readiness").LogWarning(error, "Database readiness check failed.");

        return Results.Problem(
            title: "Database unavailable",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program;
