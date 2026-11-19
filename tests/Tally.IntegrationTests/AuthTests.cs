using System.Net;
using System.Net.Http.Json;
using Tally.Domain;
using Tally.Web.Application;

namespace Tally.IntegrationTests;

public sealed class AuthTests(TallyAppFactory app) : IClassFixture<TallyAppFactory>
{
    [Fact]
    public async Task Api_requires_a_bearer_token()
    {
        var response = await app.CreateClient().GetAsync("/api/claims", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_then_sign_in_returns_a_working_token()
    {
        var client = app.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var ct = TestContext.Current.CancellationToken;

        var registered = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, displayName = "New Person", password = "a-long-enough-password" }, ct);
        var user = await registered.ReadAsync<UserResponse>(HttpStatusCode.Created);
        Assert.Equal(UserRole.Employee, user.Role);

        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new { email, password = "a-long-enough-password" }, ct);
        var token = await tokenResponse.ReadAsync<TokenResponse>(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        var me = await (await client.GetAsync("/api/auth/me", ct)).ReadAsync<UserResponse>(HttpStatusCode.OK);
        Assert.Equal(user.Id, me.Id);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_a_conflict()
    {
        var client = app.CreateClient();
        var body = new { email = $"{Guid.NewGuid():N}@example.test", displayName = "Twice", password = "a-long-enough-password" };
        var ct = TestContext.Current.CancellationToken;

        await (await client.PostAsJsonAsync("/api/auth/register", body, ct)).ShouldHaveStatusAsync(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/auth/register", body with { email = body.email.ToUpperInvariant() }, ct);

        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, "auth.email_taken");
    }

    [Fact]
    public async Task Short_passwords_fail_validation()
    {
        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/register",
            new { email = "short@example.test", displayName = "Short", password = "short" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Password", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Wrong_password_is_unauthorized()
    {
        var user = await app.CreateUserAsync();

        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/token", new { user.Email, password = "not-the-password" }, TestContext.Current.CancellationToken);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Token_endpoint_is_rate_limited()
    {
        await using var limited = new TallyAppFactory { LoginPermitsPerMinute = 2 };
        var client = limited.CreateClient();
        var body = new { email = "nobody@example.test", password = "whatever" };
        var ct = TestContext.Current.CancellationToken;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/auth/token", body, ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}
