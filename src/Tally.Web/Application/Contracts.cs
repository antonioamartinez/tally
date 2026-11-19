using System.ComponentModel.DataAnnotations;
using Tally.Domain;

namespace Tally.Web.Application;

// Request types carry transport-level validation (shape, length, required). The domain model
// still enforces the business rules, so a caller that skips these checks gains nothing.

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed record TokenResponse(string AccessToken, DateTime ExpiresAtUtc, string TokenType = "Bearer");

public sealed record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role);

public sealed class SubmitClaimRequest
{
    [Required, StringLength(ExpenseClaim.MaxDescriptionLength, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public ExpenseCategory? Category { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal Amount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateOnly? IncurredOn { get; set; }
}

public sealed class ApproveClaimRequest
{
    [MaxLength(ExpenseClaim.MaxNoteLength)]
    public string? Note { get; set; }
}

public sealed class RejectClaimRequest
{
    [Required, StringLength(ExpenseClaim.MaxNoteLength, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record ClaimResponse(
    Guid Id,
    string Description,
    ExpenseCategory Category,
    decimal Amount,
    string Currency,
    DateOnly IncurredOn,
    ClaimStatus Status,
    Guid SubmitterId,
    string SubmitterName,
    DateTime SubmittedAtUtc,
    Guid? ReviewerId,
    DateTime? ReviewedAtUtc,
    string? ReviewNote);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record CategoryTotal(ExpenseCategory Category, string Currency, decimal Total, int ClaimCount);

public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
}
