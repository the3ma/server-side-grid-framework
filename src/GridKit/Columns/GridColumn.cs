namespace GridKit.Columns;

using GridKit.Contract;

/// <summary>
/// One allow-listed column: a field id bound to a constant SQL expression over the derived table <c>g</c>.
/// A request can only choose among these columns; it can never add SQL.
/// </summary>
public sealed class GridColumn
{
    internal GridColumn(
        string field,
        GridColumnType type,
        string expression,
        GridColumnOptions options,
        GridDateSemantics? dateSemantics,
        object? trueValue,
        object? falseValue)
    {
        this.Field = field;
        this.Type = type;
        this.Expression = expression;
        this.Sortable = options.IsSortable;
        this.Filterable = options.IsFilterable;
        this.Searchable = options.IsSearchable;
        this.Indexed = options.IsIndexed;
        this.StoredUpperCase = options.IsStoredUpperCase;
        this.IntegerValues = options.HasIntegerValues;
        this.Restricted = options.IsRestricted;
        this.DateSemantics = dateSemantics;
        this.TrueValue = trueValue;
        this.FalseValue = falseValue;
        this.Operators = options.AllowedOperators ?? GridOperators.For(type);
    }

    /// <summary>camelCase field id; the same on the wire, in errors and in the manifest.</summary>
    public string Field { get; }

    public GridColumnType Type { get; }

    /// <summary>Constant SQL expression, e.g. <c>g.Barcode</c>. Never built from a request.</summary>
    public string Expression { get; }

    public bool Sortable { get; }

    public bool Filterable { get; }

    /// <summary>Included in the quick search (text columns only).</summary>
    public bool Searchable { get; }

    /// <summary>A selective filter on this column is index-backed: it lifts the large-dataset guard.</summary>
    public bool Indexed { get; }

    /// <summary>Values are always upper-case, so Oracle can compare the bare column (plain index).</summary>
    public bool StoredUpperCase { get; }

    /// <summary>Set column whose values are whole numbers (bound as integers).</summary>
    public bool IntegerValues { get; }

    /// <summary>Unknown for every caller unless the server opens it (role-restricted data).</summary>
    public bool Restricted { get; }

    public GridDateSemantics? DateSemantics { get; }

    /// <summary>Stored value for <c>true</c> on a boolean column.</summary>
    public object? TrueValue { get; }

    /// <summary>Stored value for <c>false</c> on a boolean column.</summary>
    public object? FalseValue { get; }

    public IReadOnlyList<GridOperator> Operators { get; }

    /// <summary>Text-like for comparison and case-insensitive ordering.</summary>
    public bool IsTextLike => this.Type is GridColumnType.Text or GridColumnType.Set;
}
