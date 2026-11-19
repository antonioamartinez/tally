using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tally.IntegrationTests;

public sealed class PlatformTests(TallyAppFactory app) : IClassFixture<TallyAppFactory>
{
    [Fact]
    public async Task Health_check_reports_healthy()
    {
        var response = await app.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenApi_document_describes_the_bearer_scheme()
    {
        var json = await app.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains("\"bearer\"", json);
        Assert.Contains("/api/claims/{id}/approve", json);
    }

    [Fact]
    public async Task Pages_redirect_anonymous_users_to_sign_in()
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Claims", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Malformed_json_is_a_bad_request_not_a_server_error()
    {
        var client = app.CreateClient();
        using var content = new StringContent("{\"email\": ", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/auth/token", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
