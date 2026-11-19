namespace Tally.Domain;

public enum ClaimStatus
{
    Submitted,
    Approved,
    Rejected,
    Withdrawn,
}

public enum ExpenseCategory
{
    Travel,
    Lodging,
    Meals,
    Software,
    Equipment,
    Training,
    Other,
}

/// <summary>
/// An employee's request to be reimbursed for one expense. All state changes go through
/// methods on this type, so the approval rules live in one place and are unit tested here.
/// </summary>
public sealed class ExpenseClaim
{
    public const int MaxDescriptionLength = 200;
    public const int MaxNoteLength = 500;
    public static readonly TimeSpan SubmissionWindow = TimeSpan.FromDays(90);

    private ExpenseClaim() { } // EF Core

    public Guid Id { get; private set; }
    public Guid SubmitterId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ExpenseCategory Category { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateOnly IncurredOn { get; private set; }
    public ClaimStatus Status { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewNote { get; private set; }

    /// <summary>Optimistic concurrency token. Changes on every state transition.</summary>
    public Guid Version { get; private set; }

    public static Result<ExpenseClaim> Submit(
        Guid submitterId,
        string description,
        ExpenseCategory category,
        Money amount,
        DateOnly incurredOn,
        TimeProvider clock)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxDescriptionLength)
        {
            return Error.Validation(
                "claim.description_invalid",
                $"Description is required and must be at most {MaxDescriptionLength} characters.");
        }

        if (!Enum.IsDefined(category))
        {
            return Error.Validation("claim.category_invalid", "Unknown expense category.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        if (incurredOn > today)
        {
            return Error.Validation("claim.incurred_in_future", "An expense cannot be incurred in the future.");
        }

        if (incurredOn < today.AddDays(-SubmissionWindow.Days))
        {
            return Error.Validation(
                "claim.too_old",
                $"Expenses must be submitted within {SubmissionWindow.Days} days.");
        }

        return new ExpenseClaim
        {
            Id = Guid.CreateVersion7(clock.GetUtcNow()),
            SubmitterId = submitterId,
            Description = trimmed,
            Category = category,
            Amount = amount,
            IncurredOn = incurredOn,
            Status = ClaimStatus.Submitted,
            SubmittedAtUtc = now,
            Version = Guid.NewGuid(),
        };
    }

    public Result Approve(Guid reviewerId, string? note, TimeProvider clock)
    {
        var check = EnsureReviewable(reviewerId);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (note is { Length: > MaxNoteLength })
        {
            return Error.Validation("claim.note_too_long", $"Note must be at most {MaxNoteLength} characters.");
        }

        Complete(ClaimStatus.Approved, reviewerId, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), clock);
        return Result.Success();
    }

    public Result Reject(Guid reviewerId, string reason, TimeProvider clock)
    {
        var check = EnsureReviewable(reviewerId);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxNoteLength)
        {
            return Error.Validation(
                "claim.reason_required",
                $"A rejection needs a reason of at most {MaxNoteLength} characters.");
        }

        Complete(ClaimStatus.Rejected, reviewerId, reason.Trim(), clock);
        return Result.Success();
    }

    public Result Withdraw(Guid userId)
    {
        if (userId != SubmitterId)
        {
            return Error.Forbidden("claim.not_owner", "Only the person who submitted a claim can withdraw it.");
        }

        if (Status != ClaimStatus.Submitted)
        {
            return InvalidTransition(ClaimStatus.Withdrawn);
        }

        Status = ClaimStatus.Withdrawn;
        Version = Guid.NewGuid();
        return Result.Success();
    }

    private Result EnsureReviewable(Guid reviewerId)
    {
        if (reviewerId == SubmitterId)
        {
            return Error.Forbidden("claim.self_review", "You cannot review your own claim.");
        }

        return Status == ClaimStatus.Submitted
            ? Result.Success()
            : InvalidTransition(ClaimStatus.Approved);
    }

    private void Complete(ClaimStatus status, Guid reviewerId, string? note, TimeProvider clock)
    {
        Status = status;
        ReviewerId = reviewerId;
        ReviewNote = note;
        ReviewedAtUtc = clock.GetUtcNow().UtcDateTime;
        Version = Guid.NewGuid();
    }

    private Error InvalidTransition(ClaimStatus target) => Error.Conflict(
        "claim.invalid_transition",
        $"A claim that is {Status} cannot become {target}.");
}
