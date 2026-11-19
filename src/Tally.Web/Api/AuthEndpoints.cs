using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;

namespace Tally.Web.Api;

public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/register", Register)
            .AllowAnonymous()
            .WithSummary("Create an employee account");

        auth.MapPost("/token", IssueToken)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimit)
            .WithSummary("Exchange email and password for a bearer token");

        auth.MapGet("/me", Me)
            .WithSummary("The signed-in user");

        return api;
    }

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> Register(
        RegisterRequest request, AuthService auth, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var user = result.Value;
        return TypedResults.Created("/api/auth/me", new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> IssueToken(
        LoginRequest request, AuthService auth, TokenIssuer tokens, CancellationToken ct)
    {
        var result = await auth.VerifyCredentialsAsync(request.Email, request.Password, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var token = tokens.Issue(result.Value);
        return TypedResults.Ok(new TokenResponse(token.Token, token.ExpiresAtUtc));
    }

    private static async Task<Results<Ok<UserResponse>, NotFound>> Me(
        ClaimsPrincipal principal, AuthService auth, CancellationToken ct)
    {
        var user = await auth.FindAsync(principal.ToCurrentUser().Id, ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }
}
