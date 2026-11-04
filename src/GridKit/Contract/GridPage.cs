namespace GridKit.Contract;

using System.Text.Json.Serialization;

/// <summary>One page of rows.</summary>
public sealed class GridPage<TRow>
{
    public required IReadOnlyList<TRow> Rows { get; init; }

    /// <summary><c>null</c> means "not computed" (the request asked for no total). Never coerced to 0.</summary>
    public long? TotalCount { get; init; }

    public required GridCapabilities Capabilities { get; init; }
}

/// <summary>What the client may offer the user for the current request.</summary>
public sealed class GridCapabilities
{
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
    public const string ResultSetTooLarge = "resultSetTooLarge";

    /// <summary><c>enabled</c> or <c>disabled</c>: whether scan-type text filters and search are accepted.</summary>
    public string TextFilters { get; init; } = Enabled;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>The grid's large-dataset threshold, when it has one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Threshold { get; init; }
}

/// <summary>Distinct values of one set column.</summary>
public sealed class GridValuesPage
{
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary><c>true</c> when there were more than <see cref="GridLimits.MaxDistinctValues"/> values.</summary>
    public bool Truncated { get; init; }
}
