using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tally.Domain;

namespace Tally.Web.Infrastructure.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Two schemes in one app: cookies for the server-rendered pages (default) and JWT bearer
    /// tokens for the /api endpoints. API endpoints only accept bearer tokens, so a browser
    /// cookie can never be used to call the API, which removes a whole class of CSRF problems.
    /// </summary>
    public static IServiceCollection AddTallyAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<TokenIssuer>();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.LoginPath = "/Account/Login";
                cookie.AccessDeniedPath = "/Account/AccessDenied";
                cookie.Cookie.Name = "tally.auth";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.SlidingExpiration = true;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            })
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var settings = jwt.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = TokenIssuer.SigningKey(settings),
                    NameClaimType = TallyClaimTypes.Name,
                    RoleClaimType = TallyClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ApiUser, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(Policies.Approver, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Approver)));

        return services;
    }
}
