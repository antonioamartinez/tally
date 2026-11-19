using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tally.Domain;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;

namespace Tally.Web.Pages.Reviews;

public sealed class IndexModel(ClaimService claims) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResponse<ClaimResponse> Queue { get; private set; } = new([], 1, 20, 0);

    public async Task OnGetAsync(CancellationToken ct) =>
        Queue = await claims.ReviewQueueAsync(User.ToCurrentUser(), new PageQuery(PageNumber), ct);

    public async Task<IActionResult> OnPostApproveAsync(Guid id, string? note, CancellationToken ct) =>
        Done(await claims.ApproveAsync(User.ToCurrentUser(), id, note, ct), "approved");

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string? reason, CancellationToken ct) =>
        Done(await claims.RejectAsync(User.ToCurrentUser(), id, reason ?? string.Empty, ct), "rejected");

    private RedirectToPageResult Done(Result<ClaimResponse> result, string verb)
    {
        TempData["Flash"] = result.IsSuccess
            ? $"{result.Value.SubmitterName}'s claim for {result.Value.Amount:N2} {result.Value.Currency} {verb}."
            : result.Error!.Message;
        return RedirectToPage();
    }
}
