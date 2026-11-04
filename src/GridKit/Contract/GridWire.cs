namespace GridKit.Contract;

/// <summary>The exact strings of the wire contract (case-sensitive).</summary>
public static class GridWire
{
    private static readonly Dictionary<string, GridColumnType> Types = new(StringComparer.Ordinal)
    {
        ["text"] = GridColumnType.Text,
        ["number"] = GridColumnType.Number,
        ["date"] = GridColumnType.Date,
        ["set"] = GridColumnType.Set,
        ["boolean"] = GridColumnType.Boolean,
    };

    private static readonly Dictionary<string, GridOperator> Operators = new(StringComparer.Ordinal)
    {
        ["contains"] = GridOperator.Contains,
        ["notContains"] = GridOperator.NotContains,
        ["equals"] = GridOperator.Equals,
        ["notEqual"] = GridOperator.NotEqual,
        ["startsWith"] = GridOperator.StartsWith,
        ["endsWith"] = GridOperator.EndsWith,
        ["blank"] = GridOperator.Blank,
        ["notBlank"] = GridOperator.NotBlank,
        ["lessThan"] = GridOperator.LessThan,
        ["lessThanOrEqual"] = GridOperator.LessThanOrEqual,
        ["greaterThan"] = GridOperator.GreaterThan,
        ["greaterThanOrEqual"] = GridOperator.GreaterThanOrEqual,
        ["inRange"] = GridOperator.InRange,
        ["in"] = GridOperator.In,
    };

    private static readonly Dictionary<GridOperator, string> OperatorNames =
        Operators.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly Dictionary<GridColumnType, string> TypeNames =
        Types.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParseType(string? value, out GridColumnType type) =>
        Types.TryGetValue(value ?? string.Empty, out type);

    public static bool TryParseOperator(string? value, out GridOperator op) =>
        Operators.TryGetValue(value ?? string.Empty, out op);

    public static string Name(GridOperator op) => OperatorNames[op];

    public static string Name(GridColumnType type) => TypeNames[type];

    public static string Name(GridDateSemantics semantics) => semantics switch
    {
        GridDateSemantics.Utc => "utc",
        GridDateSemantics.Local => "local",
        _ => "calendar",
    };
}
