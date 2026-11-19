using Microsoft.Extensions.Time.Testing;
using Tally.Domain;

namespace Tally.UnitTests;

public sealed class ExpenseClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _approver = Guid.NewGuid();

    [Fact]
    public void Submit_creates_a_pending_claim()
    {
        var claim = NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(_employee, claim.SubmitterId);
        Assert.Equal(Now.UtcDateTime, claim.SubmittedAtUtc);
        Assert.Null(claim.ReviewerId);
    }

    [Fact]
    public void Submit_trims_the_description()
    {
        Assert.Equal("Taxi", NewClaim(description: "  Taxi  ").Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_requires_a_description(string description)
    {
        var result = Submit(description: description);

        Assert.Equal("claim.description_invalid", result.Error?.Code);
    }

    [Fact]
    public void Submit_rejects_an_expense_dated_tomorrow()
    {
        var result = Submit(incurredOn: Today.AddDays(1));

        Assert.Equal("claim.incurred_in_future", result.Error?.Code);
    }

    [Fact]
    public void Submit_accepts_the_last_day_of_the_window_and_rejects_the_day_after()
    {
        Assert.True(Submit(incurredOn: Today.AddDays(-90)).IsSuccess);
        Assert.Equal("claim.too_old", Submit(incurredOn: Today.AddDays(-91)).Error?.Code);
    }

    [Fact]
    public void Approve_records_the_reviewer_and_time()
    {
        var claim = NewClaim();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = claim.Approve(_approver, "  Looks good ", _clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(_approver, claim.ReviewerId);
        Assert.Equal(Now.AddHours(2).UtcDateTime, claim.ReviewedAtUtc);
        Assert.Equal("Looks good", claim.ReviewNote);
    }

    [Fact]
    public void Nobody_can_approve_their_own_claim()
    {
        var claim = NewClaim();

        var result = claim.Approve(_employee, null, _clock);

        Assert.Equal(ErrorKind.Forbidden, result.Error?.Kind);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
    }

    [Fact]
    public void A_reviewed_claim_cannot_be_reviewed_again()
    {
        var claim = NewClaim();
        claim.Approve(_approver, null, _clock);

        var result = claim.Reject(Guid.NewGuid(), "Changed my mind", _clock);

        Assert.Equal("claim.invalid_transition", result.Error?.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal(ClaimStatus.Approved, claim.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Reject_requires_a_reason(string reason)
    {
        var claim = NewClaim();

        var result = claim.Reject(_approver, reason, _clock);

        Assert.Equal("claim.reason_required", result.Error?.Code);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
    }

    [Fact]
    public void The_submitter_can_withdraw_a_pending_claim()
    {
        var claim = NewClaim();

        Assert.True(claim.Withdraw(_employee).IsSuccess);
        Assert.Equal(ClaimStatus.Withdrawn, claim.Status);
    }

    [Fact]
    public void Only_the_submitter_can_withdraw()
    {
        var result = NewClaim().Withdraw(_approver);

        Assert.Equal("claim.not_owner", result.Error?.Code);
    }

    [Fact]
    public void An_approved_claim_cannot_be_withdrawn()
    {
        var claim = NewClaim();
        claim.Approve(_approver, null, _clock);

        Assert.Equal("claim.invalid_transition", claim.Withdraw(_employee).Error?.Code);
    }

    [Fact]
    public void Every_transition_changes_the_concurrency_version()
    {
        var claim = NewClaim();
        var before = claim.Version;

        claim.Approve(_approver, null, _clock);

        Assert.NotEqual(before, claim.Version);
    }

    private ExpenseClaim NewClaim(string description = "Taxi to airport") => Submit(description: description).Value;

    private Result<ExpenseClaim> Submit(string description = "Taxi to airport", DateOnly? incurredOn = null) =>
        ExpenseClaim.Submit(
            _employee,
            description,
            ExpenseCategory.Travel,
            Money.Create(42.50m, "USD").Value,
            incurredOn ?? Today,
            _clock);
}
