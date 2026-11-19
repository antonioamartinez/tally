using Microsoft.AspNetCore.Http.HttpResults;
using Tally.Domain;

namespace Tally.Web.Api;

internal static class ErrorResults
{
    /// <summary>Maps a domain error to an RFC 9457 problem response with a stable error code.</summary>
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        title: error.Message,
        statusCode: error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
