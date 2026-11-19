using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Tally.Domain;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;

namespace Tally.Web.Api;

public static class ClaimEndpoints
{
    public static RouteGroupBuilder MapClaimEndpoints(this RouteGroupBuilder api)
    {
        var claims = api.MapGroup("/claims").WithTags("Claims");

        claims.MapGet("/", ListMine).WithSummary("List my claims, newest first");
        claims.MapGet("/{id:guid}", Get).WithName("GetClaim").WithSummary("Get one claim");
        claims.MapPost("/", Submit).WithSummary("Submit a new claim");
        claims.MapPost("/{id:guid}/withdraw", Withdraw).WithSummary("Withdraw my own pending claim");

        claims.MapPost("/{id:guid}/approve", Approve)
            .RequireAuthorization(Policies.Approver)
            .WithSummary("Approve someone else's pending claim");
        claims.MapPost("/{id:guid}/reject", Reject)
            .RequireAuthorization(Policies.Approver)
            .WithSummary("Reject someone else's pending claim with a reason");

        api.MapGet("/reviews", ReviewQueue)
            .RequireAuthorization(Policies.Approver)
            .WithTags("Reviews")
            .WithSummary("Pending claims from other people, oldest first");

        api.MapGet("/reports/approved-totals", ApprovedTotals)
            .RequireAuthorization(Policies.Approver)
            .WithTags("Reports")
            .WithSummary("Approved spend by category and currency for a date range");

        return api;
    }

    private static Task<PagedResponse<ClaimResponse>> ListMine(
        ClaimsPrincipal principal, ClaimService service, ClaimStatus? status, [AsParameters] PageQuery page, CancellationToken ct) =>
        service.ListMineAsync(principal.ToCurrentUser(), status, page, ct);

    private static async Task<Results<Ok<ClaimResponse>, ProblemHttpResult>> Get(
        Guid id, ClaimsPrincipal principal, ClaimService service, CancellationToken ct) =>
        Respond(await service.GetAsync(principal.ToCurrentUser(), id, ct));

    private static async Task<Results<CreatedAtRoute<ClaimResponse>, ProblemHttpResult>> Submit(
        SubmitClaimRequest request, ClaimsPrincipal principal, ClaimService service, CancellationToken ct)
    {
        var result = await service.SubmitAsync(principal.ToCurrentUser(), request, ct);
        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, "GetClaim", new { id = result.Value.Id })
            : result.Error!.ToProblem();
    }

    private static async Task<Results<Ok<ClaimResponse>, ProblemHttpResult>> Withdraw(
        Guid id, ClaimsPrincipal principal, ClaimService service, CancellationToken ct) =>
        Respond(await service.WithdrawAsync(principal.ToCurrentUser(), id, ct));

    private static async Task<Results<Ok<ClaimResponse>, ProblemHttpResult>> Approve(
        Guid id, ApproveClaimRequest request, ClaimsPrincipal principal, ClaimService service, CancellationToken ct) =>
        Respond(await service.ApproveAsync(principal.ToCurrentUser(), id, request.Note, ct));

    private static async Task<Results<Ok<ClaimResponse>, ProblemHttpResult>> Reject(
        Guid id, RejectClaimRequest request, ClaimsPrincipal principal, ClaimService service, CancellationToken ct) =>
        Respond(await service.RejectAsync(principal.ToCurrentUser(), id, request.Reason, ct));

    private static Task<PagedResponse<ClaimResponse>> ReviewQueue(
        ClaimsPrincipal principal, ClaimService service, [AsParameters] PageQuery page, CancellationToken ct) =>
        service.ReviewQueueAsync(principal.ToCurrentUser(), page, ct);

    private static async Task<Results<Ok<IReadOnlyList<CategoryTotal>>, ProblemHttpResult>> ApprovedTotals(
        DateOnly from, DateOnly to, ClaimService service, CancellationToken ct)
    {
        if (from > to)
        {
            return Error.Validation("report.range_invalid", "'from' must be on or before 'to'.").ToProblem();
        }

        return TypedResults.Ok(await service.ApprovedTotalsAsync(from, to, ct));
    }

    private static Results<Ok<ClaimResponse>, ProblemHttpResult> Respond(Result<ClaimResponse> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
}
