namespace GridKit.Dialects;

using System.Data.Common;
using System.Text.Json;
using GridKit.Columns;
using GridKit.Contract;

/// <summary>
/// SQL Server: <c>@grid_pN</c>; text compared bare (a case-insensitive collation is assumed);
/// a set is one JSON parameter read with <c>OPENJSON</c>; native null ordering.
/// </summary>
public sealed class SqlServerGridDialect : IGridDialect
{
    public static readonly SqlServerGridDialect Instance = new();

    private static readonly HashSet<int> ConversionErrors = [245, 248, 8114, 8115];

    private SqlServerGridDialect()
    {
    }

    public string Name => "sqlserver";

    public string ParameterPrefix => "@";

    public string CountExpression => "COUNT_BIG(*)";

    public string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public string TextCompare(GridColumn column, string sqlOperator, string parameter) =>
        $"{column.Expression} {sqlOperator} {parameter}";

    public string BlankText(string expression) => $"({expression} IS NULL OR {expression} = '')";

    public string NotBlankText(string expression) => $"({expression} IS NOT NULL AND {expression} <> '')";

    public string SetIn(GridColumn column, IReadOnlyList<object> values, GridParameterBag bag)
    {
        var parameter = bag.Add(JsonSerializer.Serialize(values));
        return $"{column.Expression} IN (SELECT [value] FROM OPENJSON({parameter}))";
    }

    public string OrderBy(string expression, bool descending, bool textLike, bool storedUpperCase) =>
        $"{expression} {(descending ? "DESC" : "ASC")}";

    public string Paging(string offsetParameter, string fetchParameter) =>
        $"OFFSET {offsetParameter} ROWS FETCH NEXT {fetchParameter} ROWS ONLY";

    public string Limit(string fetchParameter) => $"OFFSET 0 ROWS FETCH NEXT {fetchParameter} ROWS ONLY";

    public object? DateValue(DateTime value, GridDateSemantics semantics) => value;

    public object? NumberValue(decimal value) => value;

    public bool IsTimeout(DbException exception) => DriverErrors.Number(exception) == -2;

    public bool IsValueConversionError(DbException exception) =>
        DriverErrors.Number(exception) is { } number && ConversionErrors.Contains(number);
}
