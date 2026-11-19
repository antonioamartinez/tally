using Microsoft.EntityFrameworkCore;
using Tally.Domain;
using Tally.Web.Infrastructure.Auth;
using Tally.Web.Infrastructure.Data;

namespace Tally.Web.Application;

/// <summary>
/// Use cases for expense claims. Shared by the JSON API and the Razor Pages UI, so both
/// front doors get the same rules, logging and concurrency handling.
/// </summary>
public sealed partial class ClaimService(TallyDbContext db, TimeProvider clock, ILogger<ClaimService> logger)
{
    private static readonly Error ClaimNotFound = Error.NotFound("claim.not_found", "Claim not found.");

    public async Task<Result<ClaimResponse>> SubmitAsync(CurrentUser user, SubmitClaimRequest request, CancellationToken ct)
    {
        var money = Money.Create(request.Amount, request.Currency);
        if (!money.IsSuccess)
        {
            return money.Error!;
        }

        var claim = ExpenseClaim.Submit(
            user.Id,
            request.Description,
            request.Category ?? ExpenseCategory.Other,
            money.Value,
            request.IncurredOn ?? default,
            clock);
        if (!claim.IsSuccess)
        {
            return claim.Error!;
        }

        db.Claims.Add(claim.Value);
        await db.SaveChangesAsync(ct);
        LogSubmitted(logger, claim.Value.Id, user.Id, claim.Value.Amount.ToString());

        return await GetAsync(user, claim.Value.Id, ct);
    }

    public async Task<Result<ClaimResponse>> GetAsync(CurrentUser user, Guid id, CancellationToken ct)
    {
        var row = await Rows(db.Claims.Where(c => c.Id == id)).SingleOrDefaultAsync(ct);

        // Someone else's claim reads as "not found" to non-approvers, so ids can't be probed.
        if (row is null || (row.Claim.SubmitterId != user.Id && !user.IsApprover))
        {
            return ClaimNotFound;
        }

        return ToResponse(row);
    }

    public Task<PagedResponse<ClaimResponse>> ListMineAsync(
        CurrentUser user, ClaimStatus? status, PageQuery page, CancellationToken ct)
    {
        var query = db.Claims.Where(c => c.SubmitterId == user.Id);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        return PageAsync(query.OrderByDescending(c => c.SubmittedAtUtc), page, ct);
    }

    public Task<PagedResponse<ClaimResponse>> ReviewQueueAsync(CurrentUser reviewer, PageQuery page, CancellationToken ct)
    {
        var query = db.Claims
            .Where(c => c.Status == ClaimStatus.Submitted && c.SubmitterId != reviewer.Id)
            .OrderBy(c => c.SubmittedAtUtc);

        return PageAsync(query, page, ct);
    }

    public Task<Result<ClaimResponse>> ApproveAsync(CurrentUser reviewer, Guid id, string? note, CancellationToken ct) =>
        TransitionAsync(reviewer, id, claim => claim.Approve(reviewer.Id, note, clock), "approved", ct);

    public Task<Result<ClaimResponse>> RejectAsync(CurrentUser reviewer, Guid id, string reason, CancellationToken ct) =>
        TransitionAsync(reviewer, id, claim => claim.Reject(reviewer.Id, reason, clock), "rejected", ct);

    public Task<Result<ClaimResponse>> WithdrawAsync(CurrentUser user, Guid id, CancellationToken ct) =>
        TransitionAsync(user, id, claim => claim.Withdraw(user.Id), "withdrawn", ct);

    public async Task<IReadOnlyList<CategoryTotal>> ApprovedTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var totals = await db.Claims
            .Where(c => c.Status == ClaimStatus.Approved && c.IncurredOn >= from && c.IncurredOn <= to)
            .GroupBy(c => new { c.Category, c.Amount.Currency })
            .Select(g => new
            {
                g.Key.Category,
                g.Key.Currency,
                MinorUnits = g.Sum(c => c.Amount.MinorUnits),
                Count = g.Count(),
            })
            .ToListAsync(ct);

        return totals
            .OrderBy(t => t.Category).ThenBy(t => t.Currency)
            .Select(t => new CategoryTotal(t.Category, t.Currency, Money.ToAmount(t.MinorUnits, t.Currency), t.Count))
            .ToList();
    }

    private async Task<Result<ClaimResponse>> TransitionAsync(
        CurrentUser user, Guid id, Func<ExpenseClaim, Result> transition, string verb, CancellationToken ct)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || (claim.SubmitterId != user.Id && !user.IsApprover))
        {
            return ClaimNotFound;
        }

        var result = transition(claim);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request changed this claim between our read and our write.
            LogConcurrencyConflict(logger, id);
            return Error.Conflict(
                "claim.concurrent_update",
                "Someone else changed this claim at the same time. Reload it and try again.");
        }

        LogTransitioned(logger, id, verb, user.Id);
        return await GetAsync(user, id, ct);
    }

    private async Task<PagedResponse<ClaimResponse>> PageAsync(
        IQueryable<ExpenseClaim> ordered, PageQuery page, CancellationToken ct)
    {
        var total = await ordered.CountAsync(ct);
        var rows = await Rows(ordered)
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(ct);

        return new PagedResponse<ClaimResponse>(
            rows.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total);
    }

    private IQueryable<ClaimRow> Rows(IQueryable<ExpenseClaim> claims) =>
        from claim in claims.AsNoTracking()
        join submitter in db.Users on claim.SubmitterId equals submitter.Id
        select new ClaimRow(claim, submitter.DisplayName);

    private static ClaimResponse ToResponse(ClaimRow row)
    {
        var c = row.Claim;
        return new ClaimResponse(
            c.Id, c.Description, c.Category, c.Amount.Amount, c.Amount.Currency, c.IncurredOn, c.Status,
            c.SubmitterId, row.SubmitterName, c.SubmittedAtUtc, c.ReviewerId, c.ReviewedAtUtc, c.ReviewNote);
    }

    private sealed record ClaimRow(ExpenseClaim Claim, string SubmitterName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claim {ClaimId} submitted by {UserId} for {Amount}")]
    private static partial void LogSubmitted(ILogger logger, Guid claimId, Guid userId, string amount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claim {ClaimId} {Transition} by {UserId}")]
    private static partial void LogTransitioned(ILogger logger, Guid claimId, string transition, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrent update rejected for claim {ClaimId}")]
    private static partial void LogConcurrencyConflict(ILogger logger, Guid claimId);
}
