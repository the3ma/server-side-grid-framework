namespace GridKit.Contract;

using System.Text.Json;

/// <summary>
/// One page request for a server-side grid. Every member is validated by
/// <see cref="Guard.GridGuard"/> against the grid's column map before any SQL is built.
/// Values stay loosely typed (<see cref="JsonElement"/>) so the guard, not the JSON binder,
/// decides what is valid and can report it without echoing the value.
/// </summary>
public sealed class GridRequest
{
    /// <summary>0-based page index.</summary>
    public int Page { get; set; }

    /// <summary>Rows per page, 1..<see cref="GridLimits.MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = GridLimits.DefaultPageSize;

    /// <summary>Requested sort keys (max 3). The server always appends the unique tiebreaker.</summary>
    public List<GridSort>? Sort { get; set; }

    /// <summary>One condition per field id.</summary>
    public Dictionary<string, GridFilter>? Filters { get; set; }

    /// <summary>Quick search: OR of <c>contains</c> across the searchable text columns.</summary>
    public string? Search { get; set; }

    /// <summary>IANA time zone id; required only when a <c>local</c> date column is filtered.</summary>
    public string? TimeZone { get; set; }

    /// <summary><c>false</c> skips the count query and returns <c>totalCount: null</c>.</summary>
    public bool IncludeTotal { get; set; } = true;
}

/// <summary>One sort key.</summary>
public sealed class GridSort
{
    public string? Field { get; set; }

    /// <summary><c>asc</c> or <c>desc</c>.</summary>
    public string? Dir { get; set; }
}

/// <summary>One filter condition for one field.</summary>
public sealed class GridFilter
{
    /// <summary><c>text</c>, <c>number</c>, <c>date</c>, <c>set</c> or <c>boolean</c>; must match the column.</summary>
    public string? Type { get; set; }

    public string? Op { get; set; }

    public JsonElement? Value { get; set; }

    /// <summary>Upper bound for <c>inRange</c>.</summary>
    public JsonElement? ValueTo { get; set; }

    /// <summary>Values for a <c>set</c> filter.</summary>
    public List<JsonElement>? Values { get; set; }
}

/// <summary>Request for the distinct values of one set column (the set filter's list).</summary>
public sealed class GridValuesRequest
{
    public string? Field { get; set; }

    /// <summary>Same meaning as <see cref="GridRequest.Search"/>: it scopes the rows the values come from.</summary>
    public string? Search { get; set; }

    /// <summary>The grid's other filters; the requested field's own filter is ignored.</summary>
    public Dictionary<string, GridFilter>? Filters { get; set; }

    public string? TimeZone { get; set; }
}
