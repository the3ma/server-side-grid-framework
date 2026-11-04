namespace GridKit.Dialects;

using System.Data.Common;
using GridKit.Columns;
using GridKit.Contract;

/// <summary>
/// Oracle 12c+: <c>:grid_pN</c>; case-insensitive text through <c>UPPER()</c> on both sides, except on
/// <see cref="GridColumn.StoredUpperCase"/> columns, which compare the bare column so a plain index can serve it;
/// <c>''</c> is NULL; sets as OR-ed IN lists of 200 binds (ORA-01795-safe); explicit null ordering.
/// </summary>
public sealed class OracleGridDialect : IGridDialect
{
    public static readonly OracleGridDialect Instance = new();

    private static readonly HashSet<int> ConversionErrors = [1722, 1858, 1840, 1841];

    private OracleGridDialect()
    {
    }

    public string Name => "oracle";

    public string ParameterPrefix => ":";

    public string CountExpression => "COUNT(*)";

    public string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public string TextCompare(GridColumn column, string sqlOperator, string parameter) =>
        column.StoredUpperCase
            ? $"{column.Expression} {sqlOperator} UPPER({parameter})"
            : $"UPPER({column.Expression}) {sqlOperator} UPPER({parameter})";

    public string BlankText(string expression) => $"{expression} IS NULL";

    public string NotBlankText(string expression) => $"{expression} IS NOT NULL";

    public string SetIn(GridColumn column, IReadOnlyList<object> values, GridParameterBag bag)
    {
        var lists = values
            .Chunk(GridLimits.OracleInListChunk)
            .Select(chunk => $"{column.Expression} IN ({string.Join(", ", chunk.Select(bag.Add))})")
            .ToArray();
        return lists.Length == 1 ? lists[0] : $"({string.Join(" OR ", lists)})";
    }

    public string OrderBy(string expression, bool descending, bool textLike, bool storedUpperCase)
    {
        var key = textLike && !storedUpperCase ? $"UPPER({expression})" : expression;
        return descending ? $"{key} DESC NULLS LAST" : $"{key} ASC NULLS FIRST";
    }

    public string Paging(string offsetParameter, string fetchParameter) =>
        $"OFFSET {offsetParameter} ROWS FETCH NEXT {fetchParameter} ROWS ONLY";

    public string Limit(string fetchParameter) => $"FETCH FIRST {fetchParameter} ROWS ONLY";

    public object? DateValue(DateTime value, GridDateSemantics semantics) => value;

    public object? NumberValue(decimal value) => value;

    public bool IsTimeout(DbException exception) => DriverErrors.Number(exception) == 1013;

    public bool IsValueConversionError(DbException exception) =>
        DriverErrors.Number(exception) is { } number && ConversionErrors.Contains(number);
}
