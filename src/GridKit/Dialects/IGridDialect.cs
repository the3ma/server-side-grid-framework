namespace GridKit.Dialects;

using System.Data.Common;
using System.Reflection;
using GridKit.Columns;
using GridKit.Contract;

/// <summary>Everything that differs between databases. The SQL builder never branches on the database itself.</summary>
public interface IGridDialect
{
    string Name { get; }

    /// <summary>Placeholder prefix: <c>@</c> or <c>:</c>.</summary>
    string ParameterPrefix { get; }

    string CountExpression { get; }

    /// <summary>Escapes LIKE wildcards in a user value (the escape character is <c>\</c>).</summary>
    string EscapeLike(string value);

    /// <summary>A text comparison (<c>=</c>, <c>&lt;&gt;</c>, <c>LIKE</c>, <c>NOT LIKE</c>), case-insensitive.</summary>
    string TextCompare(GridColumn column, string sqlOperator, string parameter);

    string BlankText(string expression);

    string NotBlankText(string expression);

    /// <summary>A set-membership predicate for a non-empty value list.</summary>
    string SetIn(GridColumn column, IReadOnlyList<object> values, GridParameterBag bag);

    string OrderBy(string expression, bool descending, bool textLike, bool storedUpperCase);

    /// <summary>Paging clause after ORDER BY.</summary>
    string Paging(string offsetParameter, string fetchParameter);

    /// <summary>A "first N rows" clause after ORDER BY.</summary>
    string Limit(string fetchParameter);

    object? DateValue(DateTime value, GridDateSemantics semantics);

    object? NumberValue(decimal value);

    /// <summary>The driver reported a command timeout.</summary>
    bool IsTimeout(DbException exception);

    /// <summary>The driver failed to convert a value (its message may echo the value).</summary>
    bool IsValueConversionError(DbException exception);
}

internal static class DriverErrors
{
    /// <summary>Reads the provider's numeric error code without referencing the provider package.</summary>
    public static int? Number(DbException exception)
    {
        var property = exception.GetType().GetProperty("Number", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(exception) is int number ? number : null;
    }
}
