namespace GridKit.Columns;

using GridKit.Contract;

/// <summary>Fluent, opt-in column flags: a column can do nothing until it is told to.</summary>
public sealed class GridColumnOptions
{
    internal bool IsSortable { get; private set; }

    internal bool IsFilterable { get; private set; }

    internal bool IsSearchable { get; private set; }

    internal bool IsIndexed { get; private set; }

    internal bool IsStoredUpperCase { get; private set; }

    internal bool HasIntegerValues { get; private set; }

    internal bool IsRestricted { get; private set; }

    internal IReadOnlyList<GridOperator>? AllowedOperators { get; private set; }

    public GridColumnOptions Sortable()
    {
        this.IsSortable = true;
        return this;
    }

    public GridColumnOptions Filterable()
    {
        this.IsFilterable = true;
        return this;
    }

    public GridColumnOptions Searchable()
    {
        this.IsSearchable = true;
        return this;
    }

    public GridColumnOptions Indexed()
    {
        this.IsIndexed = true;
        return this;
    }

    public GridColumnOptions StoredUpperCase()
    {
        this.IsStoredUpperCase = true;
        return this;
    }

    public GridColumnOptions IntegerValues()
    {
        this.HasIntegerValues = true;
        return this;
    }

    public GridColumnOptions Restricted()
    {
        this.IsRestricted = true;
        return this;
    }

    /// <summary>Narrows the operators to a subset of the column type's operators.</summary>
    public GridColumnOptions Ops(params GridOperator[] operators)
    {
        this.AllowedOperators = operators.Distinct().ToArray();
        return this;
    }
}
