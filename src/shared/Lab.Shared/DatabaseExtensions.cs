using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lab.Shared;

public static class DatabaseExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> with the provider named in <c>Database:Provider</c>
    /// (MySql, SqlServer or Postgres), so switching engines is a configuration change, not a code change.
    /// Pass <paramref name="schema"/> when several services share one database (e.g. SalesDb): each then
    /// keeps its own migrations history table and never sees the other's migrations.
    /// </summary>
    public static IServiceCollection AddLabDbContext<TContext>(
        this IServiceCollection services, IConfiguration config, string database, string? schema = null) where TContext : DbContext
    {
        const string history = "__EFMigrationsHistory";
        var db = config.GetSection("Database");
        var provider = db["Provider"] ?? "SqlServer";
        var host = db["Host"] ?? "localhost";
        var user = db["User"] ?? "sa";
        var password = db["Password"] ?? "";

        services.AddDbContext<TContext>(options =>
        {
            switch (provider)
            {
                case "MySql":
                    // MySQL has no schemas inside a database, so the schema goes into the table name instead
                    options.UseMySQL($"Server={host};Port={db["Port"] ?? "3306"};Database={database};User={user};Password={password}",
                        o => o.MigrationsHistoryTable(schema is null ? history : $"{history}_{schema}"));
                    break;
                case "SqlServer":
                    options.UseSqlServer($"Server={host},{db["Port"] ?? "1433"};Database={database};User Id={user};Password={password};TrustServerCertificate=True",
                        o => o.MigrationsHistoryTable(history, schema));
                    break;
                case "Postgres":
                    options.UseNpgsql($"Host={host};Port={db["Port"] ?? "5432"};Database={database};Username={user};Password={password}",
                        o => o.MigrationsHistoryTable(history, schema));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use MySql, SqlServer or Postgres.");
            }
        });
        return services;
    }

    /// <summary>
    /// SQL Server connection string for code that uses plain SQL instead of EF Core (e.g. notification-worker).
    /// </summary>
    public static string SqlServerConnectionString(IConfiguration config, string database)
    {
        var db = config.GetSection("Database");
        return $"Server={db["Host"] ?? "localhost"},{db["Port"] ?? "1433"};Database={database};User Id={db["User"] ?? "sa"};" +
               $"Password={db["Password"]};TrustServerCertificate=True";
    }

    /// <summary>
    /// Applies pending EF Core migrations, retrying while the database container is still starting.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this IHost host, int attempts = 20) where TContext : DbContext
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = host.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
                logger.LogInformation("Database migrations applied for {Context}", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning("Database not ready ({Attempt}/{Max}): {Message}", attempt, attempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
