using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tally.Domain;

namespace Tally.Web.Infrastructure.Data;

/// <summary>Applies migrations and seeds demo data at startup when configured to.</summary>
internal sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public const string DemoPassword = "tally-demo-password";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.MigrateOnStartup && !settings.SeedDemoData)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TallyDbContext>();

        if (settings.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger, settings.Provider);
        }

        if (settings.SeedDemoData && !await db.Users.AnyAsync(cancellationToken))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            Seed(db, hasher);
            await db.SaveChangesAsync(cancellationToken);
            LogSeeded(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Seed(TallyDbContext db, IPasswordHasher<User> hasher)
    {
        var approver = User.Create("approver@tally.local", "Avery Approver", UserRole.Approver);
        var employee = User.Create("employee@tally.local", "Eli Employee", UserRole.Employee);
        foreach (var user in new[] { approver, employee })
        {
            user.SetPasswordHash(hasher.HashPassword(user, DemoPassword));
        }

        db.Users.AddRange(approver, employee);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        (string Description, ExpenseCategory Category, decimal Amount, int DaysAgo)[] samples =
        [
            ("Train to client workshop", ExpenseCategory.Travel, 86.40m, 12),
            ("Hotel, two nights", ExpenseCategory.Lodging, 412.00m, 11),
            ("Team lunch after release", ExpenseCategory.Meals, 138.75m, 3),
        ];

        foreach (var (description, category, amount, daysAgo) in samples)
        {
            var money = Money.Create(amount, "USD").Value;
            db.Claims.Add(ExpenseClaim.Submit(employee.Id, description, category, money, today.AddDays(-daysAgo), clock).Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrated ({Provider})")]
    private static partial void LogMigrated(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded demo users and claims")]
    private static partial void LogSeeded(ILogger logger);
}
