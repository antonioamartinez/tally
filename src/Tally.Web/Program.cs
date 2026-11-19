using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Tally.Web.Api;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;
using Tally.Web.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    // One JSON object per line, ready for a log shipper.
    builder.Logging.ClearProviders().AddJsonConsole();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTallyData(builder.Configuration);
builder.Services.AddTallyAuth(builder.Configuration);
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ClaimService>();

builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var loginPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Claims");
    o.Conventions.AuthorizeFolder("/Reviews", Policies.Approver);
});

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed JSON is the caller's fault: 400, not 500.
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireAuthorization(Policies.ApiUser);
api.MapAuthEndpoints();
api.MapClaimEndpoints();

app.MapOpenApi();
if (app.Configuration.GetValue("ApiDocs:Enabled", app.Environment.IsDevelopment()))
{
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapRazorPages();

app.Run();

/// <summary>Entry point, exposed so integration tests can host the app in memory.</summary>
public partial class Program;
