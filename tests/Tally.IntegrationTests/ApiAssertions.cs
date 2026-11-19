using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tally.IntegrationTests;

internal static class ApiAssertions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        await response.ShouldHaveStatusAsync(expected);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode}. Body: {body}");
        }
    }

    /// <summary>Asserts a problem response with the given status and stable error code.</summary>
    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        await response.ShouldHaveStatusAsync(expected);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }
}
