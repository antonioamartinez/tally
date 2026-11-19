using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;

namespace Tally.Web.Pages.Claims;

public sealed class IndexModel(ClaimService claims, TimeProvider clock) : PageModel
{
    [BindProperty]
    public SubmitClaimRequest Input { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResponse<ClaimResponse> Claims { get; private set; } = new([], 1, 20, 0);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Input.IncurredOn = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await claims.SubmitAsync(User.ToCurrentUser(), Input, ct);
            if (result.IsSuccess)
            {
                TempData["Flash"] = $"Submitted \"{result.Value.Description}\".";
                return RedirectToPage();
            }

            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, CancellationToken ct)
    {
        var result = await claims.WithdrawAsync(User.ToCurrentUser(), id, ct);
        TempData["Flash"] = result.IsSuccess ? "Claim withdrawn." : result.Error!.Message;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct) =>
        Claims = await claims.ListMineAsync(User.ToCurrentUser(), null, new PageQuery(PageNumber), ct);
}
