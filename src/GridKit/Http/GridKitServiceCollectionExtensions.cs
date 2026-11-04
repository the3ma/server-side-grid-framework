namespace GridKit.Http;

using GridKit.Errors;
using GridKit.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

public static class GridKitServiceCollectionExtensions
{
    private static readonly HashSet<string> MemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "pageSize", "sort", "filters", "search", "timeZone", "includeTotal", "field",
    };

    /// <summary>
    /// Registers the executor, the ProblemDetails mapping for grid exceptions, and a value-free
    /// <c>grid-request-invalid</c> response for bodies the JSON binder cannot read.
    /// </summary>
    public static IServiceCollection AddGridKit(this IServiceCollection services)
    {
        services.AddSingleton<IGridExecutor, GridExecutor>();
        services.AddExceptionHandler<GridExceptionHandler>();
        services.AddProblemDetails();
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context => InvalidBody(context.HttpContext, context.ModelState));
        return services;
    }

    /// <summary>
    /// Model-binding errors can name a JSON path and the binder's message can quote the value, so neither is passed on:
    /// the key becomes the top-level request member (or <c>(invalid field)</c>) and the message is fixed.
    /// </summary>
    public static IActionResult InvalidBody(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var failed = modelState.Where(entry => entry.Value is { Errors.Count: > 0 }).Select(entry => entry.Key).ToList();

        // The binder also reports the whole parameter ("request", "") when a member fails; keep only the members then.
        var paths = failed.Where(key => key.StartsWith("$", StringComparison.Ordinal)).ToList();
        var keys = (paths.Count > 0 ? paths : failed)
            .Select(MemberKey)
            .Distinct(StringComparer.Ordinal)
            .DefaultIfEmpty(GridErrorCodes.InvalidFieldKey)
            .ToArray();

        var exception = new GridRequestInvalidException(
            keys.ToDictionary(k => k, _ => new[] { GridErrorCodes.Message(GridErrorCodes.InvalidValue) }),
            keys.ToDictionary(k => k, _ => new[] { GridErrorCodes.InvalidValue }));
        var body = GridExceptionHandler.Problem(exception, GridExceptionHandler.TraceId(httpContext));
        var result = new ObjectResult(body) { StatusCode = exception.StatusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static string MemberKey(string modelStateKey)
    {
        var key = modelStateKey.StartsWith("$.", StringComparison.Ordinal) ? modelStateKey[2..] : modelStateKey;
        var first = key.Split('.', '[')[0];
        if (!MemberNames.Contains(first))
        {
            return GridErrorCodes.InvalidFieldKey;
        }

        return MemberNames.First(name => string.Equals(name, first, StringComparison.OrdinalIgnoreCase));
    }
}
