using System.Security.Claims;
using Tally.Domain;

namespace Tally.Web.Infrastructure.Auth;

/// <summary>Short JWT-style claim names, used for both bearer tokens and the cookie.</summary>
public static class TallyClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";

    public static IEnumerable<Claim> For(User user) =>
    [
        new(Subject, user.Id.ToString()),
        new(Email, user.Email),
        new(Name, user.DisplayName),
        new(Role, user.Role.ToString()),
    ];
}

public static class Policies
{
    /// <summary>Any signed-in API caller, authenticated by bearer token only.</summary>
    public const string ApiUser = "api-user";

    /// <summary>Can review other people's claims and see reports.</summary>
    public const string Approver = "approver";
}

/// <summary>The caller, as the application layer sees it. Keeps HTTP types out of services.</summary>
public sealed record CurrentUser(Guid Id, UserRole Role)
{
    public bool IsApprover => Role == UserRole.Approver;
}

public static class ClaimsPrincipalExtensions
{
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(TallyClaimTypes.Subject);
        var role = principal.FindFirstValue(TallyClaimTypes.Role);
        if (!Guid.TryParse(id, out var userId) || !Enum.TryParse<UserRole>(role, out var userRole))
        {
            throw new InvalidOperationException("The authenticated principal is missing the sub or role claim.");
        }

        return new CurrentUser(userId, userRole);
    }
}
