using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tally.Domain;
using Tally.Web.Application;

namespace Tally.IntegrationTests;

public sealed class ClaimWorkflowTests(TallyAppFactory app) : IClassFixture<TallyAppFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submitted_claim_can_be_read_back_from_its_location()
    {
        var client = await app.ClientForAsync(await app.CreateUserAsync());

        var created = await client.PostAsJsonAsync("/api/claims", NewClaim(amount: 19.99m), Ct);
        var claim = await created.ReadAsync<ClaimResponse>(HttpStatusCode.Created);

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(19.99m, claim.Amount);
        var fetched = await (await client.GetAsync(created.Headers.Location, Ct)).ReadAsync<ClaimResponse>(HttpStatusCode.OK);
        Assert.Equal(claim, fetched);
    }

    [Fact]
    public async Task Missing_fields_return_field_level_validation_errors()
    {
        var client = await app.ClientForAsync(await app.CreateUserAsync());

        var response = await client.PostAsJsonAsync("/api/claims", new { description = "No category or date", amount = 5 }, Ct);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Category", body);
        Assert.Contains("IncurredOn", body);
    }

    [Fact]
    public async Task Domain_rules_are_enforced_through_the_api()
    {
        var client = await app.ClientForAsync(await app.CreateUserAsync());

        var response = await client.PostAsJsonAsync("/api/claims", NewClaim(incurredOn: Today.AddDays(5)), Ct);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "claim.incurred_in_future");
    }

    [Fact]
    public async Task Employees_cannot_open_the_review_queue()
    {
        var client = await app.ClientForAsync(await app.CreateUserAsync());

        var response = await client.GetAsync("/api/reviews", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employees_cannot_see_each_others_claims()
    {
        var owner = await app.ClientForAsync(await app.CreateUserAsync());
        var stranger = await app.ClientForAsync(await app.CreateUserAsync());
        var claim = await Submit(owner);

        var response = await stranger.GetAsync($"/api/claims/{claim.Id}", Ct);

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "claim.not_found");
    }

    [Fact]
    public async Task Approver_approves_a_claim_once()
    {
        var employee = await app.ClientForAsync(await app.CreateUserAsync());
        var approverUser = await app.CreateUserAsync(UserRole.Approver);
        var approver = await app.ClientForAsync(approverUser);
        var claim = await Submit(employee);

        var queue = await (await approver.GetAsync("/api/reviews?pageSize=100", Ct)).ReadAsync<PagedResponse<ClaimResponse>>(HttpStatusCode.OK);
        Assert.Contains(queue.Items, c => c.Id == claim.Id);

        var approved = await (await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new { note = "Thanks" }, Ct))
            .ReadAsync<ClaimResponse>(HttpStatusCode.OK);
        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Equal(approverUser.Id, approved.ReviewerId);

        var again = await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new { }, Ct);
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "claim.invalid_transition");
    }

    [Fact]
    public async Task Approvers_cannot_approve_their_own_claims()
    {
        var approver = await app.ClientForAsync(await app.CreateUserAsync(UserRole.Approver));
        var own = await Submit(approver);

        var response = await approver.PostAsJsonAsync($"/api/claims/{own.Id}/approve", new { }, Ct);

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "claim.self_review");
    }

    [Fact]
    public async Task Rejection_needs_a_reason()
    {
        var employee = await app.ClientForAsync(await app.CreateUserAsync());
        var approver = await app.ClientForAsync(await app.CreateUserAsync(UserRole.Approver));
        var claim = await Submit(employee);

        var noReason = await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/reject", new { reason = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await (await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/reject", new { reason = "No receipt" }, Ct))
            .ReadAsync<ClaimResponse>(HttpStatusCode.OK);
        Assert.Equal(ClaimStatus.Rejected, rejected.Status);
        Assert.Equal("No receipt", rejected.ReviewNote);
    }

    [Fact]
    public async Task Submitter_can_withdraw_and_filter_by_status()
    {
        var employee = await app.ClientForAsync(await app.CreateUserAsync());
        var keep = await Submit(employee);
        var drop = await Submit(employee);

        await (await employee.PostAsync($"/api/claims/{drop.Id}/withdraw", null, Ct)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        var pending = await (await employee.GetAsync("/api/claims?status=Submitted", Ct)).ReadAsync<PagedResponse<ClaimResponse>>(HttpStatusCode.OK);
        Assert.Equal([keep.Id], pending.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task Paging_returns_newest_first_and_reports_the_total()
    {
        var employee = await app.ClientForAsync(await app.CreateUserAsync());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await Submit(employee, $"Claim {i}")).Id);
        }

        var page = await (await employee.GetAsync("/api/claims?page=1&pageSize=2", Ct)).ReadAsync<PagedResponse<ClaimResponse>>(HttpStatusCode.OK);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([ids[2], ids[1]], page.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task Report_totals_only_approved_claims_in_range()
    {
        await using var isolated = new TallyAppFactory();
        var employee = await isolated.ClientForAsync(await isolated.CreateUserAsync());
        var approver = await isolated.ClientForAsync(await isolated.CreateUserAsync(UserRole.Approver));

        foreach (var amount in new[] { 10.25m, 4.75m })
        {
            var claim = await Submit(employee, amount: amount, category: ExpenseCategory.Meals);
            await (await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new { }, Ct)).ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        await Submit(employee, amount: 999m, category: ExpenseCategory.Meals); // still pending, not counted

        var url = $"/api/reports/approved-totals?from={Today.AddDays(-30):yyyy-MM-dd}&to={Today:yyyy-MM-dd}";
        var totals = await (await approver.GetAsync(url, Ct)).ReadAsync<List<CategoryTotal>>(HttpStatusCode.OK);

        Assert.Equal([new CategoryTotal(ExpenseCategory.Meals, "USD", 15.00m, 2)], totals);
    }

    [Fact]
    public async Task Concurrent_edits_are_detected_by_the_version_token()
    {
        var employee = await app.ClientForAsync(await app.CreateUserAsync());
        var approver = await app.ClientForAsync(await app.CreateUserAsync(UserRole.Approver));
        var claim = await Submit(employee);

        // Load the claim in one unit of work, change it through the API in another, then save the stale copy.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tally.Web.Infrastructure.Data.TallyDbContext>();
        var stale = await db.Claims.SingleAsync(c => c.Id == claim.Id, Ct);

        await (await approver.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new { }, Ct)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        Assert.True(stale.Reject(Guid.NewGuid(), "Too late", TimeProvider.System).IsSuccess);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Ct));
    }

    private async Task<ClaimResponse> Submit(
        HttpClient client, string description = "Taxi", decimal amount = 25m, ExpenseCategory category = ExpenseCategory.Travel)
    {
        var response = await client.PostAsJsonAsync("/api/claims", NewClaim(description, amount, category), Ct);
        return await response.ReadAsync<ClaimResponse>(HttpStatusCode.Created);
    }

    private static object NewClaim(
        string description = "Taxi",
        decimal amount = 25m,
        ExpenseCategory category = ExpenseCategory.Travel,
        DateOnly? incurredOn = null) => new
        {
            description,
            category = category.ToString(),
            amount,
            currency = "USD",
            incurredOn = incurredOn ?? Today,
        };
}
