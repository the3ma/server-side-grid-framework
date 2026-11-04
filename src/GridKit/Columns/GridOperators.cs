namespace GridKit.Columns;

using GridKit.Contract;

/// <summary>Operator lists per column type, and the operator groups the guard reasons about.</summary>
public static class GridOperators
{
    public static readonly IReadOnlyList<GridOperator> Text =
    [
        GridOperator.Contains, GridOperator.NotContains, GridOperator.Equals, GridOperator.NotEqual,
        GridOperator.StartsWith, GridOperator.EndsWith, GridOperator.Blank, GridOperator.NotBlank,
    ];

    public static readonly IReadOnlyList<GridOperator> Comparable =
    [
        GridOperator.Equals, GridOperator.NotEqual, GridOperator.LessThan, GridOperator.LessThanOrEqual,
        GridOperator.GreaterThan, GridOperator.GreaterThanOrEqual, GridOperator.InRange,
        GridOperator.Blank, GridOperator.NotBlank,
    ];

    public static readonly IReadOnlyList<GridOperator> Set = [GridOperator.In];

    public static readonly IReadOnlyList<GridOperator> Boolean =
        [GridOperator.Equals, GridOperator.Blank, GridOperator.NotBlank];

    /// <summary>Text operators that cannot use an index and scan the whole scoped set.</summary>
    public static readonly IReadOnlySet<GridOperator> Scan =
        new HashSet<GridOperator> { GridOperator.Contains, GridOperator.NotContains, GridOperator.EndsWith };

    /// <summary>Operators that never narrow a set enough to count as selective.</summary>
    public static readonly IReadOnlySet<GridOperator> NotSelective = new HashSet<GridOperator>
    {
        GridOperator.Contains, GridOperator.NotContains, GridOperator.EndsWith,
        GridOperator.Blank, GridOperator.NotBlank, GridOperator.NotEqual,
    };

    public static readonly IReadOnlySet<GridOperator> NoValue =
        new HashSet<GridOperator> { GridOperator.Blank, GridOperator.NotBlank };

    public static IReadOnlyList<GridOperator> For(GridColumnType type) => type switch
    {
        GridColumnType.Text => Text,
        GridColumnType.Number or GridColumnType.Date => Comparable,
        GridColumnType.Set => Set,
        _ => Boolean,
    };
}
