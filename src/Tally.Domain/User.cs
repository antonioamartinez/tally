namespace Tally.Domain;

public enum UserRole
{
    Employee,
    Approver,
}

public sealed class User
{
    private User() { } // EF Core

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }

    public static User Create(string email, string displayName, UserRole role) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = NormalizeEmail(email),
        DisplayName = displayName.Trim(),
        Role = role,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
