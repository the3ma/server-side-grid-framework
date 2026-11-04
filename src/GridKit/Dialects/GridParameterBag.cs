namespace GridKit.Dialects;

/// <summary>
/// Collects bound values under generated names (<c>grid_p0</c>, <c>grid_p1</c> ...). SQL text only ever
/// contains these names, so it depends on the request's shape, never on its values.
/// </summary>
public sealed class GridParameterBag
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
    private readonly string prefix;
    private int next;

    public GridParameterBag(string prefix)
    {
        this.prefix = prefix;
    }

    /// <summary>Names without the prefix, as Dapper expects them.</summary>
    public IReadOnlyDictionary<string, object?> Values => this.values;

    /// <summary>Adds a value and returns its placeholder, e.g. <c>@grid_p3</c>.</summary>
    public string Add(object? value)
    {
        var name = $"grid_p{this.next++}";
        this.values.Add(name, value);
        return this.prefix + name;
    }

    /// <summary>Adds a fixed-name value (paging) and returns its placeholder.</summary>
    public string AddNamed(string name, object? value)
    {
        this.values[name] = value;
        return this.prefix + name;
    }

    public GridParameterBag Clone()
    {
        var copy = new GridParameterBag(this.prefix) { next = this.next };
        foreach (var (key, value) in this.values)
        {
            copy.values.Add(key, value);
        }

        return copy;
    }
}
