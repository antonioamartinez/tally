using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tally.Domain;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Data;

namespace Tally.IntegrationTests;

/// <summary>
/// Hosts the real app in memory against a real database. SQLite in-memory by default; set
/// TALLY_TEST_POSTGRES to a server connection string to run the same tests on Postgres.
/// Migrations run at startup, so every test run also proves the migrations apply cleanly.
/// </summary>
public sealed class TallyAppFactory : WebApplicationFactory<Program>
{
    public const string Password = "integration-test-password";

    private static readonly string? PostgresServer = Environment.GetEnvironmentVariable("TALLY_TEST_POSTGRES");

    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;

    public TallyAppFactory()
    {
        if (PostgresServer is not null)
        {
            _connectionString = $"{PostgresServer};Database=tally_test_{Guid.NewGuid():N}";
        }
        else
        {
            // A named shared-cache in-memory database lives as long as one connection is open.
            _connectionString = $"Data Source=file:tally-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
        }
    }

    public int LoginPermitsPerMinute { get; init; } = 10_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Tally", _connectionString);
        builder.UseSetting("Database:Provider", PostgresServer is null ? "Sqlite" : "Postgres");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedDemoData", "false");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", LoginPermitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Creates a user directly in the database. Approvers can't self-register, by design.</summary>
    public async Task<TestUser> CreateUserAsync(UserRole role = UserRole.Employee)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TallyDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var name = $"{role} {Guid.NewGuid():N}"[..20];
        var user = User.Create($"{Guid.NewGuid():N}@example.test", name, role);
        user.SetPasswordHash(hasher.HashPassword(user, Password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new TestUser(user.Id, user.Email, name, role);
    }

    /// <summary>An HttpClient that sends a bearer token for the given user.</summary>
    public async Task<HttpClient> ClientForAsync(TestUser user)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { user.Email, Password });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(Json.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<TallyDbContext, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TallyDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        if (PostgresServer is not null)
        {
            await WithDbAsync(db => db.Database.EnsureDeletedAsync());
        }

        await base.DisposeAsync();
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }
}

public sealed record TestUser(Guid Id, string Email, string DisplayName, UserRole Role);
