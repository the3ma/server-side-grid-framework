namespace GridKit.Columns;

using System.Text.RegularExpressions;
using GridKit.Contract;

/// <summary>Field-id rule shared by the server, the client and the manifest.</summary>
public static partial class GridFieldNames
{
    /// <summary>
    /// camelCase: a lowercase ASCII letter, then ASCII letters or digits. <c>\z</c>, not <c>$</c>,
    /// because in .NET <c>$</c> also matches before a trailing newline. camelCase ids survive any
    /// JSON writer that camel-cases dictionary keys, so error keys always match the client's columns.
    /// </summary>
    public const string Pattern = @"^[a-z][A-Za-z0-9]{0,63}\z";

    public static bool IsValid(string? field) => field is not null && FieldRegex().IsMatch(field);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FieldRegex();
}

/// <summary>A grid's allow-list: the only fields, operators and sort keys a request may use.</summary>
public abstract class GridColumnMap
{
    private readonly List<GridColumn> columns = [];
    private readonly Dictionary<string, GridColumn> byField = new(StringComparer.Ordinal);
    private readonly List<(GridColumn Column, bool Descending)> defaultSort = [];

    protected GridColumnMap(string name, string tiebreakerExpression)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A grid map needs a name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(tiebreakerExpression))
        {
            throw new ArgumentException("A grid map needs a unique tiebreaker expression.", nameof(tiebreakerExpression));
        }

        this.Name = name;
        this.TiebreakerExpression = tiebreakerExpression;
    }

    /// <summary>Grid id (logs, manifest).</summary>
    public string Name { get; }

    /// <summary>Unique, constant expression appended to every ORDER BY so paging is stable.</summary>
    public string TiebreakerExpression { get; }

    public IReadOnlyList<GridColumn> Columns => this.columns;

    public IReadOnlyList<(GridColumn Column, bool Descending)> DefaultSort => this.defaultSort;

    /// <summary>Large-dataset guard threshold; <c>null</c> when the grid has no guard.</summary>
    public int? GuardThreshold { get; private set; }

    public GridColumn? Find(string field) => this.byField.GetValueOrDefault(field);

    protected void AddColumn(GridColumn column)
    {
        if (!GridFieldNames.IsValid(column.Field))
        {
            throw new ArgumentException(
                $"Grid '{this.Name}': field id '{column.Field}' must match {GridFieldNames.Pattern} (camelCase).");
        }

        if (string.IsNullOrWhiteSpace(column.Expression))
        {
            throw new ArgumentException($"Grid '{this.Name}': field '{column.Field}' needs a SQL expression.");
        }

        if (this.byField.ContainsKey(column.Field))
        {
            throw new ArgumentException($"Grid '{this.Name}': field '{column.Field}' is declared twice.");
        }

        if (column.Searchable && column.Type != GridColumnType.Text)
        {
            throw new ArgumentException($"Grid '{this.Name}': only text columns can be searchable ('{column.Field}').");
        }

        if (column.StoredUpperCase && column.Type != GridColumnType.Text)
        {
            throw new ArgumentException($"Grid '{this.Name}': StoredUpperCase applies to text columns ('{column.Field}').");
        }

        if (column.IntegerValues && column.Type != GridColumnType.Set)
        {
            throw new ArgumentException($"Grid '{this.Name}': IntegerValues applies to set columns ('{column.Field}').");
        }

        var allowed = GridOperators.For(column.Type);
        if (column.Operators.Count == 0 || column.Operators.Any(op => !allowed.Contains(op)))
        {
            throw new ArgumentException($"Grid '{this.Name}': operators of '{column.Field}' must be a subset of its type's operators.");
        }

        this.columns.Add(column);
        this.byField.Add(column.Field, column);
    }

    protected void AddDefaultSort(string field, bool descending)
    {
        var column = this.Find(field)
            ?? throw new ArgumentException($"Grid '{this.Name}': default sort field '{field}' is not declared.");

        if (!column.Sortable)
        {
            throw new ArgumentException($"Grid '{this.Name}': default sort field '{field}' is not sortable.");
        }

        if (column.Restricted)
        {
            throw new ArgumentException($"Grid '{this.Name}': a restricted column cannot be a default sort key ('{field}').");
        }

        this.defaultSort.Add((column, descending));
    }

    protected void SetGuardThreshold(int threshold)
    {
        if (threshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "The guard threshold must be positive.");
        }

        this.GuardThreshold = threshold;
    }
}

/// <summary>Typed allow-list for rows of <typeparamref name="TRow"/>.</summary>
public sealed class GridColumnMap<TRow> : GridColumnMap
{
    private GridColumnMap(string name, string tiebreakerExpression)
        : base(name, tiebreakerExpression)
    {
    }

    /// <param name="name">Grid id.</param>
    /// <param name="tiebreakerExpression">Unique, constant expression, e.g. <c>g.Id</c>.</param>
    public static GridColumnMap<TRow> Create(string name, string tiebreakerExpression) => new(name, tiebreakerExpression);

    public GridColumnMap<TRow> Text(string field, string expression, Action<GridColumnOptions>? configure = null) =>
        this.Add(field, GridColumnType.Text, expression, configure);

    public GridColumnMap<TRow> Number(string field, string expression, Action<GridColumnOptions>? configure = null) =>
        this.Add(field, GridColumnType.Number, expression, configure);

    public GridColumnMap<TRow> Set(string field, string expression, Action<GridColumnOptions>? configure = null) =>
        this.Add(field, GridColumnType.Set, expression, configure);

    public GridColumnMap<TRow> Date(
        string field, string expression, GridDateSemantics semantics, Action<GridColumnOptions>? configure = null) =>
        this.Add(field, GridColumnType.Date, expression, configure, semantics);

    /// <param name="trueValue">Stored value for <c>true</c> (e.g. <c>1</c> or <c>"T"</c>).</param>
    /// <param name="falseValue">Stored value for <c>false</c>.</param>
    public GridColumnMap<TRow> Boolean(
        string field, string expression, object trueValue, object falseValue, Action<GridColumnOptions>? configure = null) =>
        this.Add(field, GridColumnType.Boolean, expression, configure, trueValue: trueValue, falseValue: falseValue);

    public GridColumnMap<TRow> DefaultSortBy(string field, bool descending = false)
    {
        this.AddDefaultSort(field, descending);
        return this;
    }

    /// <summary>Refuses scan-type text filters and search until an indexed filter narrows the set.</summary>
    public GridColumnMap<TRow> GuardScanFilters(int threshold)
    {
        this.SetGuardThreshold(threshold);
        return this;
    }

    private GridColumnMap<TRow> Add(
        string field,
        GridColumnType type,
        string expression,
        Action<GridColumnOptions>? configure,
        GridDateSemantics? semantics = null,
        object? trueValue = null,
        object? falseValue = null)
    {
        var options = new GridColumnOptions();
        configure?.Invoke(options);
        this.AddColumn(new GridColumn(field, type, expression, options, semantics, trueValue, falseValue));
        return this;
    }
}
