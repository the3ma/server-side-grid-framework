namespace GridKit.Sql;

using System.Text;
using GridKit.Columns;
using GridKit.Contract;
using GridKit.Dialects;
using GridKit.Guard;

/// <summary>SQL text plus its generated parameters.</summary>
public sealed record GridCommand(string Sql, IReadOnlyDictionary<string, object?> Parameters);

/// <summary>The page command and, when the total was requested, the count command over the same WHERE.</summary>
public sealed record GridPageCommands(GridCommand Page, GridCommand? Count);

/// <summary>
/// Composes grid SQL from a validated plan. The handler's base SQL (its own SELECT, joins and row scope) is
/// wrapped as derived table <c>g</c>; filters are AND-ed outside it, so they can only narrow the scope. Every
/// expression comes from the column map and every value is a generated, bound parameter.
/// </summary>
public static class GridSql
{
    private const string Escape = " ESCAPE '\\'";

    public static GridPageCommands BuildPage(GridPlan plan, IGridDialect dialect, string baseSql)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dialect);
        var bag = new GridParameterBag(dialect.ParameterPrefix);
        var where = BuildWhere(plan.Filters, plan.Search, plan.SearchColumns, dialect, bag);
        var countBag = bag.Clone();

        var page = new StringBuilder()
            .Append("SELECT g.*\n")
            .Append(From(baseSql))
            .Append(where)
            .Append("ORDER BY ").Append(BuildOrderBy(plan, dialect)).Append('\n')
            .Append(dialect.Paging(
                bag.AddNamed("grid_offset", (long)plan.Page * plan.PageSize),
                bag.AddNamed("grid_fetch", plan.PageSize)))
            .ToString();

        GridCommand? count = null;
        if (plan.IncludeTotal)
        {
            var countSql = new StringBuilder()
                .Append("SELECT ").Append(dialect.CountExpression).Append('\n')
                .Append(From(baseSql))
                .Append(where)
                .ToString()
                .TrimEnd('\n');
            count = new GridCommand(countSql, countBag.Values);
        }

        return new GridPageCommands(new GridCommand(page, bag.Values), count);
    }

    /// <summary>Distinct non-null values of a set column, sorted, one more than the limit to detect truncation.</summary>
    public static GridCommand BuildValues(GridValuesPlan plan, IGridDialect dialect, string baseSql, int fetch)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dialect);
        var bag = new GridParameterBag(dialect.ParameterPrefix);
        var expression = plan.Column.Expression;
        var conditions = new List<string> { $"{expression} IS NOT NULL" };
        conditions.AddRange(Conditions(plan.Filters, plan.Search, plan.SearchColumns, dialect, bag));

        var sql = new StringBuilder()
            .Append("SELECT DISTINCT ").Append(expression).Append(" AS GridValue\n")
            .Append(From(baseSql))
            .Append(Where(conditions))
            .Append("ORDER BY GridValue\n")
            .Append(dialect.Limit(bag.AddNamed("grid_fetch", fetch)))
            .ToString();
        return new GridCommand(sql, bag.Values);
    }

    private static string From(string baseSql) => $"FROM (\n{baseSql.Trim()}\n) g\n";

    private static string BuildWhere(
        IReadOnlyList<GridPlanFilter> filters,
        string? search,
        IReadOnlyList<GridColumn> searchColumns,
        IGridDialect dialect,
        GridParameterBag bag) =>
        Where(Conditions(filters, search, searchColumns, dialect, bag));

    private static string Where(IReadOnlyList<string> conditions) =>
        conditions.Count == 0 ? string.Empty : $"WHERE {string.Join("\n  AND ", conditions)}\n";

    private static List<string> Conditions(
        IReadOnlyList<GridPlanFilter> filters,
        string? search,
        IReadOnlyList<GridColumn> searchColumns,
        IGridDialect dialect,
        GridParameterBag bag)
    {
        var conditions = filters.Select(f => Condition(f, dialect, bag)).ToList();
        if (search is not null && searchColumns.Count > 0)
        {
            var pattern = $"%{dialect.EscapeLike(search)}%";
            var parts = searchColumns.Select(c => dialect.TextCompare(c, "LIKE", bag.Add(pattern)) + Escape);
            conditions.Add($"({string.Join(" OR ", parts)})");
        }

        return conditions;
    }

    private static string Condition(GridPlanFilter filter, IGridDialect dialect, GridParameterBag bag)
    {
        var column = filter.Column;
        var expression = column.Expression;
        switch (filter.Op)
        {
            case GridOperator.Blank:
                return column.IsTextLike ? dialect.BlankText(expression) : $"{expression} IS NULL";
            case GridOperator.NotBlank:
                return column.IsTextLike ? dialect.NotBlankText(expression) : $"{expression} IS NOT NULL";
        }

        return column.Type switch
        {
            GridColumnType.Text => TextCondition(filter, dialect, bag),
            GridColumnType.Number => NumberCondition(filter, dialect, bag),
            GridColumnType.Date => DateCondition(filter, dialect, bag),
            GridColumnType.Set => filter.SetValues is { Count: > 0 } values ? dialect.SetIn(column, values, bag) : "1 = 0",
            _ => $"{expression} = {bag.Add(filter.Boolean == true ? column.TrueValue : column.FalseValue)}",
        };
    }

    private static string TextCondition(GridPlanFilter filter, IGridDialect dialect, GridParameterBag bag)
    {
        var column = filter.Column;
        var value = filter.Text!;
        string Like(string op, string pattern) => dialect.TextCompare(column, op, bag.Add(pattern)) + Escape;
        var escaped = dialect.EscapeLike(value);

        // Negative text operators include blanks, like the grid UI's own semantics.
        return filter.Op switch
        {
            GridOperator.Contains => Like("LIKE", $"%{escaped}%"),
            GridOperator.NotContains => $"({column.Expression} IS NULL OR {Like("NOT LIKE", $"%{escaped}%")})",
            GridOperator.StartsWith => Like("LIKE", $"{escaped}%"),
            GridOperator.EndsWith => Like("LIKE", $"%{escaped}"),
            GridOperator.Equals => dialect.TextCompare(column, "=", bag.Add(value)),
            _ => $"({column.Expression} IS NULL OR {dialect.TextCompare(column, "<>", bag.Add(value))})",
        };
    }

    private static string NumberCondition(GridPlanFilter filter, IGridDialect dialect, GridParameterBag bag)
    {
        var expression = filter.Column.Expression;
        string P(decimal value) => bag.Add(dialect.NumberValue(value));
        var value = filter.Number!.Value;

        return filter.Op switch
        {
            GridOperator.Equals => $"{expression} = {P(value)}",
            GridOperator.NotEqual => $"{expression} <> {P(value)}",
            GridOperator.LessThan => $"{expression} < {P(value)}",
            GridOperator.LessThanOrEqual => $"{expression} <= {P(value)}",
            GridOperator.GreaterThan => $"{expression} > {P(value)}",
            GridOperator.GreaterThanOrEqual => $"{expression} >= {P(value)}",
            _ => $"{expression} >= {P(value)} AND {expression} <= {P(filter.NumberTo!.Value)}",
        };
    }

    private static string DateCondition(GridPlanFilter filter, IGridDialect dialect, GridParameterBag bag)
    {
        var column = filter.Column;
        var expression = column.Expression;
        var semantics = column.DateSemantics ?? GridDateSemantics.Calendar;
        string P(DateTime value) => bag.Add(dialect.DateValue(value, semantics));

        if (filter.Op == GridOperator.NotEqual)
        {
            return $"({expression} < {P(filter.DateFrom!.Value)} OR {expression} >= {P(filter.DateTo!.Value)})";
        }

        var parts = new List<string>(2);
        if (filter.DateFrom is { } from)
        {
            parts.Add($"{expression} >= {P(from)}");
        }

        if (filter.DateTo is { } to)
        {
            parts.Add($"{expression} < {P(to)}");
        }

        return string.Join(" AND ", parts);
    }

    private static string BuildOrderBy(GridPlan plan, IGridDialect dialect)
    {
        var keys = plan.Sorts
            .Select(s => dialect.OrderBy(s.Column.Expression, s.Descending, s.Column.IsTextLike, s.Column.StoredUpperCase))
            .ToList();

        // The unique tiebreaker makes paging stable; skip it only when it is already a sort key.
        var tiebreaker = plan.Map.TiebreakerExpression;
        if (!plan.Sorts.Any(s => string.Equals(s.Column.Expression, tiebreaker, StringComparison.Ordinal)))
        {
            keys.Add(dialect.OrderBy(tiebreaker, descending: false, textLike: false, storedUpperCase: false));
        }

        return string.Join(", ", keys);
    }
}
