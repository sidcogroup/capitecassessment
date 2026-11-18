using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Azure.Communication.Email;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Configuration;
using CapitecAssessment.DataAccess;
using CapitecAssessment.Middlewares;
using CapitecAssessment.Services;
using CapitecAssessment.Services.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CapitecAssessment.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers application services (and, as they are added, their clients).
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

        services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IMessagingService, MessagingService>();

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITransactionService, TransactionService>();

        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IDisputeService, DisputeService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsersService, UsersService>();

        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// OTP settings and hashing. Otp:HashKey is required outside Development; in Development a
    /// random per-process key is used when it is not set (codes then do not survive a restart).
    /// </summary>
    public static IServiceCollection AddOtp(this IServiceCollection services)
    {
        var ephemeralDevelopmentKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        services.AddOptions<OtpOptions>()
            .BindConfiguration(OtpOptions.SectionName)
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.HashKey))
                {
                    options.HashKey = ephemeralDevelopmentKey;
                }
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OtpOptions>, OtpOptionsValidator>();
        services.AddSingleton<OtpHasher>();

        return services;
    }

    /// <summary>
    /// Email transport: Azure Communication Services when AzureCommunicationServices:Email is
    /// configured; otherwise (Development only) the email is written to the log.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddOptions<AzureCommunicationEmailOptions>()
            .BindConfiguration(AzureCommunicationEmailOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AzureCommunicationEmailOptions>, AzureCommunicationEmailOptionsValidator>();

        // EmailClient is thread-safe and meant to be reused.
        services.AddSingleton(sp =>
            new EmailClient(sp.GetRequiredService<IOptions<AzureCommunicationEmailOptions>>().Value.ConnectionString));

        services.AddSingleton<IEmailSender>(sp =>
            sp.GetRequiredService<IOptions<AzureCommunicationEmailOptions>>().Value.IsConfigured
                ? ActivatorUtilities.CreateInstance<AzureCommunicationEmailSender>(sp)
                : ActivatorUtilities.CreateInstance<LoggingEmailSender>(sp)); // the validator only allows this in Development

        return services;
    }

    /// <summary>
    /// Cookie session issued after OTP verification, plus a per-IP rate limit on the sign-in endpoints.
    /// </summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // __Host- prefix: the browser only accepts it with Secure, Path=/ and no Domain,
                // so it cannot be set or overwritten by a sibling subdomain.
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;                           // not readable by page scripts (XSS)
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only
                options.Cookie.SameSite = SameSiteMode.Strict;            // not sent on cross-site requests (CSRF)
                options.Cookie.Path = "/";

                // Banking-style idle timeout: the session ends after 15 minutes without a request.
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                options.SlidingExpiration = true;

                // This is an API: answer with status codes instead of redirecting to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();

        services.AddOptions<AuthenticationRateLimitOptions>().BindConfiguration(AuthenticationRateLimitOptions.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthenticationRateLimitOptions.PolicyName, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<AuthenticationRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }

    public const string SessionCookieName = "__Host-session";

    /// <summary>
    /// Registers EF Core against PostgreSQL (local, or Neon). The connection string comes from
    /// ConnectionStrings:DefaultConnection - set it via user-secrets or the
    /// ConnectionStrings__DefaultConnection environment variable, never in a committed file.
    /// </summary>
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Neon suspends idle compute; the first connection after a pause can fail transiently.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            }));

        return services;
    }

    /// <summary>
    /// Registers the IMiddleware-based middlewares so they can be resolved from DI.
    /// </summary>
    public static IServiceCollection AddApiMiddlewares(this IServiceCollection services)
    {
        services.AddTransient<ExceptionHandlingMiddleware>();
        services.AddTransient<RequestLoggingMiddleware>();

        return services;
    }

    public static IServiceCollection AddApiVersioningConfiguration(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ReportApiVersions = true;
        });

        return services;
    }
}
