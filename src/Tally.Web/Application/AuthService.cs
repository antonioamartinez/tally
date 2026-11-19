using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tally.Domain;
using Tally.Web.Infrastructure.Data;

namespace Tally.Web.Application;

public sealed partial class AuthService(
    TallyDbContext db,
    IPasswordHasher<User> hasher,
    ILogger<AuthService> logger)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    public async Task<Result<User>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Error.Conflict("auth.email_taken", "An account with that email already exists.");
        }

        // Self-registration always creates an employee. Approvers are granted, not claimed.
        var user = User.Create(email, request.DisplayName, UserRole.Employee);
        user.SetPasswordHash(hasher.HashPassword(user, request.Password));
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent registration; the unique index is the real guard.
            return Error.Conflict("auth.email_taken", "An account with that email already exists.");
        }

        LogRegistered(logger, user.Id);
        return user;
    }

    public async Task<Result<User>> VerifyCredentialsAsync(string email, string password, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            // Hash anyway so an unknown email takes about as long as a wrong password.
            hasher.HashPassword(User.Create(normalized, "unknown", UserRole.Employee), password);
            LogFailedSignIn(logger);
            return InvalidCredentials;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            LogFailedSignIn(logger);
            return InvalidCredentials;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, password));
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    public Task<User?> FindAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in attempt")]
    private static partial void LogFailedSignIn(ILogger logger);
}
