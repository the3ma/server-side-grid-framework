namespace GridKit.Errors;

/// <summary>Base type of every grid failure. Messages never contain request or stored values.</summary>
public abstract class GridException : Exception
{
    protected GridException(string message)
        : base(message)
    {
    }

    /// <summary>The ProblemDetails <c>type</c>.</summary>
    public abstract string ProblemType { get; }

    public abstract int StatusCode { get; }
}

/// <summary>400 <c>grid-request-invalid</c>: messages and machine codes per key.</summary>
public sealed class GridRequestInvalidException : GridException
{
    public GridRequestInvalidException(
        IReadOnlyDictionary<string, string[]> errors,
        IReadOnlyDictionary<string, string[]> errorCodes)
        : base("The grid request is invalid.")
    {
        this.Errors = errors;
        this.ErrorCodes = errorCodes;
    }

    /// <summary>Key: field id, request member name or <c>(invalid field)</c>.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public IReadOnlyDictionary<string, string[]> ErrorCodes { get; }

    public override string ProblemType => GridProblemTypes.RequestInvalid;

    public override int StatusCode => 400;
}

/// <summary>400 <c>grid-filter-too-broad</c>: a scan filter or search on a large grid without a narrowing filter.</summary>
public sealed class GridFilterTooBroadException : GridException
{
    public GridFilterTooBroadException(IReadOnlyList<string> fields, int threshold)
        : base("The filter is too broad for this grid. Add a filter on an indexed column first.")
    {
        this.Fields = fields;
        this.Threshold = threshold;
    }

    /// <summary>Refused field ids; <c>search</c> for the quick search.</summary>
    public IReadOnlyList<string> Fields { get; }

    public int Threshold { get; }

    public override string ProblemType => GridProblemTypes.FilterTooBroad;

    public override int StatusCode => 400;
}

/// <summary>504 <c>grid-query-timeout</c>: the query ran past the grid timeout while the caller was still waiting.</summary>
public sealed class GridQueryTimeoutException : GridException
{
    public GridQueryTimeoutException(int timeoutSeconds)
        : base("The grid query timed out.")
    {
        this.TimeoutSeconds = timeoutSeconds;
    }

    public int TimeoutSeconds { get; }

    public override string ProblemType => GridProblemTypes.QueryTimeout;

    public override int StatusCode => 504;
}

/// <summary>
/// 500 <c>grid-query-failed</c>: a value-free replacement for a driver error whose message could echo a value.
/// The driver exception is deliberately not chained.
/// </summary>
public sealed class GridQueryFailedException : GridException
{
    public GridQueryFailedException()
        : base("The grid query failed.")
    {
    }

    public override string ProblemType => GridProblemTypes.QueryFailed;

    public override int StatusCode => 500;
}

public static class GridProblemTypes
{
    public const string RequestInvalid = "grid-request-invalid";
    public const string FilterTooBroad = "grid-filter-too-broad";
    public const string QueryTimeout = "grid-query-timeout";
    public const string QueryFailed = "grid-query-failed";
}
