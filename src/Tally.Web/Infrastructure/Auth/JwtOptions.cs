using System.ComponentModel.DataAnnotations;

namespace Tally.Web.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key. Never committed for real environments; supply via Jwt__SigningKey.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(1);
}
