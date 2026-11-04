namespace GridKit.Http;

using System.Diagnostics;
using System.Text.Json;
using GridKit.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Turns grid exceptions into RFC 7807 ProblemDetails (<c>application/problem+json</c>). Error dictionaries are
/// written with their keys unchanged; nothing in the body comes from the request.
/// </summary>
public sealed class GridExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not GridException grid)
        {
            return false;
        }

        var problem = Problem(grid, TraceId(httpContext));
        httpContext.Response.StatusCode = grid.StatusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, Json, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The ProblemDetails body for a grid exception, as an ordered dictionary.</summary>
    public static Dictionary<string, object?> Problem(GridException exception, string traceId)
    {
        var problem = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = exception.ProblemType,
            ["title"] = exception.Message,
            ["status"] = exception.StatusCode,
        };

        switch (exception)
        {
            case GridRequestInvalidException invalid:
                problem["errors"] = invalid.Errors;
                problem["errorCodes"] = invalid.ErrorCodes;
                break;
            case GridFilterTooBroadException tooBroad:
                problem["fields"] = tooBroad.Fields;
                problem["threshold"] = tooBroad.Threshold;
                break;
            case GridQueryTimeoutException timeout:
                problem["timeoutSeconds"] = timeout.TimeoutSeconds;
                break;
        }

        problem["traceId"] = traceId;
        return problem;
    }

    public static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}
