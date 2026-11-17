using Asp.Versioning;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.DataAccess;
using CapitecAssessment.Middlewares;
using CapitecAssessment.Services;
using Microsoft.EntityFrameworkCore;

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

        services.AddHttpClient();

        return services;
    }

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
