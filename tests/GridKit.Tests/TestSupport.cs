namespace GridKit.Tests;

using System.Text.Json;
using GridKit.Columns;
using GridKit.Contract;
using GridKit.Errors;
using GridKit.Guard;

public sealed class TestRow
{
    public long Id { get; init; }
}

/// <summary>Builders that read like the JSON a client sends.</summary>
public static class G
{
    public static JsonElement J(object? value) => JsonSerializer.SerializeToElement(value);

    public static GridFilter Text(string op, string? value = null) =>
        new() { Type = "text", Op = op, Value = value is null ? null : J(value) };

    public static GridFilter Number(string op, decimal? value = null, decimal? valueTo = null) =>
        new() { Type = "number", Op = op, Value = value is null ? null : J(value), ValueTo = valueTo is null ? null : J(valueTo) };

    public static GridFilter Date(string op, string? value = null, string? valueTo = null) =>
        new() { Type = "date", Op = op, Value = value is null ? null : J(value), ValueTo = valueTo is null ? null : J(valueTo) };

    public static GridFilter Set(params string[] values) =>
        new() { Type = "set", Op = "in", Values = values.Select(v => J(v)).ToList() };

    public static GridFilter Bool(string op, bool? value = null) =>
        new() { Type = "boolean", Op = op, Value = value is null ? null : J(value) };

    public static GridRequest Request(
        Dictionary<string, GridFilter>? filters = null,
        string? search = null,
        List<GridSort>? sort = null,
        string? timeZone = null,
        int page = 0,
        int pageSize = 100,
        bool includeTotal = true) => new()
        {
            Filters = filters,
            Search = search,
            Sort = sort,
            TimeZone = timeZone,
            Page = page,
            PageSize = pageSize,
            IncludeTotal = includeTotal,
        };

    public static GridSort Sort(string field, string dir = "asc") => new() { Field = field, Dir = dir };

    /// <summary>A map that exercises every column type and flag.</summary>
    public static readonly GridColumnMap<TestRow> Map = GridColumnMap<TestRow>
        .Create("test-grid", "g.Id")
        .Number("id", "g.Id", c => c.Sortable().Filterable().Indexed())
        .Text("code", "g.Code", c => c.Sortable().Filterable().Searchable().Indexed().StoredUpperCase())
        .Text("name", "g.Name", c => c.Sortable().Filterable().Searchable())
        .Text("note", "g.Note", c => c.Filterable())
        .Set("status", "g.Status", c => c.Sortable().Filterable().Indexed())
        .Set("regionId", "g.RegionId", c => c.Filterable().IntegerValues())
        .Number("amount", "g.Amount", c => c.Sortable().Filterable())
        .Date("createdOn", "g.CreatedAt", GridDateSemantics.Local, c => c.Sortable().Filterable())
        .Date("auditedOn", "g.AuditedAt", GridDateSemantics.Utc, c => c.Sortable().Filterable())
        .Date("dueOn", "g.DueDate", GridDateSemantics.Calendar, c => c.Filterable())
        .Boolean("active", "g.ActiveFlag", "T", "F", c => c.Filterable())
        .Set("tier", "g.Tier", c => c.Filterable().Restricted())
        .Text("hidden", "g.Hidden")
        .DefaultSortBy("name")
        .GuardScanFilters(50_000);

    /// <summary>The same columns without the large-dataset guard.</summary>
    public static readonly GridColumnMap<TestRow> Unguarded = GridColumnMap<TestRow>
        .Create("test-unguarded", "g.Id")
        .Text("name", "g.Name", c => c.Sortable().Filterable().Searchable())
        .Set("status", "g.Status", c => c.Filterable())
        .Number("amount", "g.Amount", c => c.Sortable().Filterable());

    public static GridPlan Resolve(GridRequest request, GridColumnMap? map = null, GridGuardOptions? options = null) =>
        GridGuard.Resolve(request, map ?? Map, options);

    public static GridRequestInvalidException Invalid(GridRequest request, GridColumnMap? map = null, GridGuardOptions? options = null)
    {
        var act = () => GridGuard.Resolve(request, map ?? Map, options);
        return act.Should().Throw<GridRequestInvalidException>().Which;
    }

    public static Dictionary<string, GridFilter> Filters(params (string Field, GridFilter Filter)[] filters) =>
        filters.ToDictionary(f => f.Field, f => f.Filter);
}
