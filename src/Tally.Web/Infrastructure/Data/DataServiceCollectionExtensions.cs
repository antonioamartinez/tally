using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Tally.Web.Infrastructure.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddTallyData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var connectionString = configuration.GetConnectionString("Tally")
            ?? throw new InvalidOperationException("Connection string 'Tally' is not configured.");

        // Services depend on the abstract TallyDbContext; the provider picks the concrete type.
        _ = options.Provider switch
        {
            DatabaseProvider.Sqlite => services.AddDbContext<TallyDbContext, SqliteTallyDbContext>(
                o => o.UseSqlite(connectionString)),
            DatabaseProvider.Postgres => services.AddDbContext<TallyDbContext, PostgresTallyDbContext>(
                o => o.UseNpgsql(connectionString)),
            _ => throw new InvalidOperationException($"Unsupported database provider '{options.Provider}'."),
        };

        // Registered before data protection so migrations run before the key ring is first read.
        services.AddHostedService<DatabaseInitializer>();

        services.AddDataProtection()
            .SetApplicationName("Tally")
            .PersistKeysToDbContext<TallyDbContext>();

        services.AddHealthChecks().AddDbContextCheck<TallyDbContext>("database");
        return services;
    }
}
