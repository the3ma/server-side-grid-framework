namespace GridKit.Guard;

using GridKit.Columns;
using GridKit.Contract;

/// <summary>A validated request: only allow-listed columns and typed, checked values. The SQL builder reads only this.</summary>
public sealed class GridPlan
{
    public required GridColumnMap Map { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    /// <summary>Requested sorts, or the map's default sort. The tiebreaker is added by the SQL builder.</summary>
    public required IReadOnlyList<GridPlanSort> Sorts { get; init; }

    public required IReadOnlyList<GridPlanFilter> Filters { get; init; }

    /// <summary>Trimmed quick-search text, or <c>null</c>.</summary>
    public string? Search { get; init; }

    public required IReadOnlyList<GridColumn> SearchColumns { get; init; }

    public required bool IncludeTotal { get; init; }

    public required GridCapabilities Capabilities { get; init; }

    /// <summary>Error keys of everything that narrowed the rows (filtered fields, <c>search</c>).</summary>
    public IReadOnlyList<string> NarrowingKeys =>
        this.Filters.Select(f => f.Column.Field).Concat(this.Search is null ? [] : ["search"]).ToArray();
}

/// <summary>A validated values request (distinct values of one set column).</summary>
public sealed class GridValuesPlan
{
    public required GridColumnMap Map { get; init; }

    public required GridColumn Column { get; init; }

    public required IReadOnlyList<GridPlanFilter> Filters { get; init; }

    public string? Search { get; init; }

    public required IReadOnlyList<GridColumn> SearchColumns { get; init; }

    public IReadOnlyList<string> NarrowingKeys =>
        this.Filters.Select(f => f.Column.Field).Concat(this.Search is null ? [] : ["search"]).ToArray();
}

public sealed record GridPlanSort(GridColumn Column, bool Descending);

/// <summary>One validated condition. Only the members that apply to the column type are set.</summary>
public sealed class GridPlanFilter
{
    public required GridColumn Column { get; init; }

    public required GridOperator Op { get; init; }

    /// <summary>Text value (text filters).</summary>
    public string? Text { get; init; }

    public decimal? Number { get; init; }

    /// <summary>Upper bound of a number <c>inRange</c> (inclusive).</summary>
    public decimal? NumberTo { get; init; }

    /// <summary>Inclusive lower instant of a date condition (<c>&gt;=</c>).</summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>Exclusive upper instant of a date condition (<c>&lt;</c>).</summary>
    public DateTime? DateTo { get; init; }

    /// <summary>Set values (strings, or whole numbers on an integer-valued column).</summary>
    public IReadOnlyList<object>? SetValues { get; init; }

    public bool? Boolean { get; init; }
}
