namespace GridKit.Dialects;

using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GridKit.Columns;
using GridKit.Contract;

/// <summary>
/// SQLite (the runnable demo): <c>@grid_pN</c>; <c>LIKE</c> is case-insensitive for ASCII, equality uses
/// <c>COLLATE NOCASE</c>; a set is one JSON parameter read with <c>json_each</c>; dates are ISO-8601 UTC strings,
/// which compare correctly as text.
/// </summary>
public sealed class SqliteGridDialect : IGridDialect
{
    public static readonly SqliteGridDialect Instance = new();

    private SqliteGridDialect()
    {
    }

    public string Name => "sqlite";

    public string ParameterPrefix => "@";

    public string CountExpression => "COUNT(*)";

    public string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public string TextCompare(GridColumn column, string sqlOperator, string parameter) =>
        sqlOperator is "=" or "<>" && !column.StoredUpperCase
            ? $"{column.Expression} {sqlOperator} {parameter} COLLATE NOCASE"
            : column.StoredUpperCase
                ? $"{column.Expression} {sqlOperator} UPPER({parameter})"
                : $"{column.Expression} {sqlOperator} {parameter}";

    public string BlankText(string expression) => $"({expression} IS NULL OR {expression} = '')";

    public string NotBlankText(string expression) => $"({expression} IS NOT NULL AND {expression} <> '')";

    public string SetIn(GridColumn column, IReadOnlyList<object> values, GridParameterBag bag)
    {
        var parameter = bag.Add(JsonSerializer.Serialize(values));
        return $"{column.Expression} IN (SELECT value FROM json_each({parameter}))";
    }

    public string OrderBy(string expression, bool descending, bool textLike, bool storedUpperCase)
    {
        var key = textLike && !storedUpperCase ? $"{expression} COLLATE NOCASE" : expression;
        return $"{key} {(descending ? "DESC" : "ASC")}";
    }

    public string Paging(string offsetParameter, string fetchParameter) =>
        $"LIMIT {fetchParameter} OFFSET {offsetParameter}";

    public string Limit(string fetchParameter) => $"LIMIT {fetchParameter}";

    public object? DateValue(DateTime value, GridDateSemantics semantics) =>
        semantics == GridDateSemantics.Calendar
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public object? NumberValue(decimal value) => (double)value;

    public bool IsTimeout(DbException exception) => false;

    public bool IsValueConversionError(DbException exception) => false;
}
