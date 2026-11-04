namespace GridKit.Guard;

using System.Globalization;
using System.Text.Json;
using GridKit.Columns;
using GridKit.Contract;
using GridKit.Errors;

/// <summary>Per-caller, server-derived inputs to the guard. Never taken from the request.</summary>
public sealed class GridGuardOptions
{
    public static readonly GridGuardOptions Default = new();

    /// <summary>Opens <see cref="GridColumn.Restricted"/> columns for this caller. Restricted columns are closed when absent.</summary>
    public Func<GridColumn, bool>? CanAccessColumn { get; init; }

    /// <summary>Row count of the caller's scoped set when the server knows it; at or under the threshold lifts the guard.</summary>
    public long? KnownRowCount { get; init; }
}

/// <summary>
/// Validates a request against a column map and resolves it into a typed plan. Everything not in the map is refused;
/// every error is keyed by an allow-listed name and never contains the submitted value.
/// </summary>
public static class GridGuard
{
    public static GridPlan Resolve(GridRequest request, GridColumnMap map, GridGuardOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(map);
        var context = new Context(map, options ?? GridGuardOptions.Default);

        if (request.Page < 0)
        {
            context.Add("page", GridErrorCodes.OutOfRange);
        }

        if (request.PageSize < 1 || request.PageSize > GridLimits.MaxPageSize)
        {
            context.Add("pageSize", GridErrorCodes.OutOfRange);
        }

        var sorts = ResolveSorts(request.Sort, context);
        var (search, searchColumns) = ResolveSearch(request.Search, context);
        var zone = ResolveZone(request.TimeZone, context);
        var filters = ResolveFilters(request.Filters, skipField: null, zone, context);
        context.ThrowIfInvalid();

        var narrowed = IsNarrowed(filters, context);
        EnforceGuard(filters, search, narrowed, map);

        return new GridPlan
        {
            Map = map,
            Page = request.Page,
            PageSize = request.PageSize,
            Sorts = sorts,
            Filters = filters,
            Search = search,
            SearchColumns = searchColumns,
            IncludeTotal = request.IncludeTotal,
            Capabilities = CapabilitiesFor(map, narrowed),
        };
    }

    public static GridValuesPlan ResolveValues(GridValuesRequest request, GridColumnMap map, GridGuardOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(map);
        var context = new Context(map, options ?? GridGuardOptions.Default);

        GridColumn? column = null;
        if (!GridFieldNames.IsValid(request.Field))
        {
            context.Add("field", GridErrorCodes.UnknownField);
        }
        else
        {
            column = context.Lookup(request.Field!);
            if (column is null)
            {
                context.Add("field", GridErrorCodes.UnknownField);
            }
            else if (column.Type != GridColumnType.Set || !column.Filterable)
            {
                context.Add("field", GridErrorCodes.NotSetColumn);
            }
        }

        var (search, searchColumns) = ResolveSearch(request.Search, context);
        var zone = ResolveZone(request.TimeZone, context);

        // The field's own filter is ignored: the list must offer every value the other filters allow.
        var skip = GridFieldNames.IsValid(request.Field) ? request.Field : null;
        var filters = ResolveFilters(request.Filters, skip, zone, context);
        context.ThrowIfInvalid();

        EnforceGuard(filters, search, IsNarrowed(filters, context), map);

        return new GridValuesPlan
        {
            Map = map,
            Column = column!,
            Filters = filters,
            Search = search,
            SearchColumns = searchColumns,
        };
    }

    private static List<GridPlanSort> ResolveSorts(List<GridSort>? sorts, Context context)
    {
        if (sorts is null || sorts.Count == 0)
        {
            return context.Map.DefaultSort.Select(s => new GridPlanSort(s.Column, s.Descending)).ToList();
        }

        if (sorts.Count > GridLimits.MaxSorts)
        {
            context.Add("sort", GridErrorCodes.TooMany);
            return [];
        }

        var result = new List<GridPlanSort>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sort in sorts)
        {
            if (sort is null)
            {
                context.Add("sort", GridErrorCodes.InvalidValue);
                continue;
            }

            var column = context.LookupKey(sort.Field, out var key);
            if (column is null)
            {
                context.Add(key, GridErrorCodes.UnknownField);
                continue;
            }

            if (!column.Sortable)
            {
                context.Add(key, GridErrorCodes.NotSortable);
                continue;
            }

            if (!seen.Add(column.Field))
            {
                context.Add(key, GridErrorCodes.Duplicate);
                continue;
            }

            switch (sort.Dir)
            {
                case "asc":
                    result.Add(new GridPlanSort(column, Descending: false));
                    break;
                case "desc":
                    result.Add(new GridPlanSort(column, Descending: true));
                    break;
                default:
                    context.Add("sort", GridErrorCodes.InvalidValue);
                    break;
            }
        }

        return result;
    }

    private static (string? Search, IReadOnlyList<GridColumn> Columns) ResolveSearch(string? search, Context context)
    {
        var columns = context.Map.Columns.Where(c => c.Searchable && context.CanAccess(c)).ToArray();
        if (string.IsNullOrWhiteSpace(search))
        {
            return (null, columns);
        }

        if (search.Length > GridLimits.MaxSearchLength)
        {
            context.Add("search", GridErrorCodes.TooLong);
            return (null, columns);
        }

        if (columns.Length == 0)
        {
            context.Add("search", GridErrorCodes.InvalidValue);
            return (null, columns);
        }

        return (search.Trim(), columns);
    }

    private static TimeZoneInfo? ResolveZone(string? timeZone, Context context)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return null;
        }

        if (timeZone.Length > GridLimits.MaxTimeZoneLength
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
        {
            context.Add("timeZone", GridErrorCodes.InvalidTimeZone);
            context.ZoneInvalid = true;
            return null;
        }

        return zone;
    }

    private static List<GridPlanFilter> ResolveFilters(
        Dictionary<string, GridFilter>? filters, string? skipField, TimeZoneInfo? zone, Context context)
    {
        if (filters is null || filters.Count == 0)
        {
            return [];
        }

        if (filters.Count > GridLimits.MaxFilters)
        {
            context.Add("filters", GridErrorCodes.TooMany);
            return [];
        }

        var result = new List<GridPlanFilter>();
        var needsZone = false;
        foreach (var (rawKey, filter) in filters)
        {
            if (skipField is not null && string.Equals(rawKey, skipField, StringComparison.Ordinal))
            {
                continue;
            }

            var column = context.LookupKey(rawKey, out var key);
            if (column is null)
            {
                context.Add(key, GridErrorCodes.UnknownField);
                continue;
            }

            if (!column.Filterable)
            {
                context.Add(key, GridErrorCodes.NotFilterable);
                continue;
            }

            if (filter is null)
            {
                context.Add(key, GridErrorCodes.ValueRequired);
                continue;
            }

            if (!GridWire.TryParseType(filter.Type, out var type) || type != column.Type)
            {
                context.Add(key, GridErrorCodes.TypeMismatch);
                continue;
            }

            if (!GridWire.TryParseOperator(filter.Op, out var op) || !column.Operators.Contains(op))
            {
                context.Add(key, GridErrorCodes.UnsupportedOperator);
                continue;
            }

            if (column.Type == GridColumnType.Date && column.DateSemantics == GridDateSemantics.Local)
            {
                needsZone = true;
            }

            var resolved = ResolveCondition(column, op, filter, zone, key, context);
            if (resolved is not null)
            {
                result.Add(resolved);
            }
        }

        if (needsZone && zone is null && !context.ZoneInvalid)
        {
            context.Add("timeZone", GridErrorCodes.TimeZoneRequired);
        }

        return result;
    }

    private static GridPlanFilter? ResolveCondition(
        GridColumn column, GridOperator op, GridFilter filter, TimeZoneInfo? zone, string key, Context context)
    {
        if (GridOperators.NoValue.Contains(op))
        {
            if (Present(filter.Value) || Present(filter.ValueTo) || filter.Values is not null)
            {
                context.Add(key, GridErrorCodes.ValueNotAllowed);
                return null;
            }

            return new GridPlanFilter { Column = column, Op = op };
        }

        return column.Type switch
        {
            GridColumnType.Text => ResolveText(column, op, filter, key, context),
            GridColumnType.Number => ResolveNumber(column, op, filter, key, context),
            GridColumnType.Date => ResolveDate(column, op, filter, zone, key, context),
            GridColumnType.Set => ResolveSet(column, op, filter, key, context),
            _ => ResolveBoolean(column, op, filter, key, context),
        };
    }

    private static GridPlanFilter? ResolveText(GridColumn column, GridOperator op, GridFilter filter, string key, Context context)
    {
        if (Present(filter.ValueTo) || filter.Values is not null)
        {
            context.Add(key, GridErrorCodes.ValueNotAllowed);
            return null;
        }

        if (!Present(filter.Value))
        {
            context.Add(key, GridErrorCodes.ValueRequired);
            return null;
        }

        if (filter.Value!.Value.ValueKind != JsonValueKind.String)
        {
            context.Add(key, GridErrorCodes.InvalidValue);
            return null;
        }

        var text = filter.Value.Value.GetString()!;
        if (text.Length == 0)
        {
            context.Add(key, GridErrorCodes.InvalidValue);
            return null;
        }

        if (text.Length > GridLimits.MaxTextValueLength)
        {
            context.Add(key, GridErrorCodes.TooLong);
            return null;
        }

        return new GridPlanFilter { Column = column, Op = op, Text = text };
    }

    private static GridPlanFilter? ResolveNumber(GridColumn column, GridOperator op, GridFilter filter, string key, Context context)
    {
        if (filter.Values is not null || (op != GridOperator.InRange && Present(filter.ValueTo)))
        {
            context.Add(key, GridErrorCodes.ValueNotAllowed);
            return null;
        }

        if (!TryNumber(filter.Value, key, context, out var from))
        {
            return null;
        }

        decimal? to = null;
        if (op == GridOperator.InRange)
        {
            if (!TryNumber(filter.ValueTo, key, context, out var upper))
            {
                return null;
            }

            if (from > upper)
            {
                context.Add(key, GridErrorCodes.OutOfRange);
                return null;
            }

            to = upper;
        }

        return new GridPlanFilter { Column = column, Op = op, Number = from, NumberTo = to };
    }

    private static GridPlanFilter? ResolveDate(
        GridColumn column, GridOperator op, GridFilter filter, TimeZoneInfo? zone, string key, Context context)
    {
        if (filter.Values is not null || (op != GridOperator.InRange && Present(filter.ValueTo)))
        {
            context.Add(key, GridErrorCodes.ValueNotAllowed);
            return null;
        }

        if (!TryDate(filter.Value, key, context, out var from))
        {
            return null;
        }

        DateOnly? to = null;
        if (op == GridOperator.InRange)
        {
            if (!TryDate(filter.ValueTo, key, context, out var upper))
            {
                return null;
            }

            if (from > upper)
            {
                context.Add(key, GridErrorCodes.OutOfRange);
                return null;
            }

            to = upper;
        }

        var semantics = column.DateSemantics ?? GridDateSemantics.Calendar;
        var (lower, upperBound) = GridDates.Bounds(op, from, to, semantics, zone);
        return new GridPlanFilter { Column = column, Op = op, DateFrom = lower, DateTo = upperBound };
    }

    private static GridPlanFilter? ResolveSet(GridColumn column, GridOperator op, GridFilter filter, string key, Context context)
    {
        if (Present(filter.Value) || Present(filter.ValueTo))
        {
            context.Add(key, GridErrorCodes.ValueNotAllowed);
            return null;
        }

        if (filter.Values is null)
        {
            context.Add(key, GridErrorCodes.ValueRequired);
            return null;
        }

        if (filter.Values.Count > GridLimits.MaxSetValues)
        {
            context.Add(key, GridErrorCodes.TooMany);
            return null;
        }

        var values = new List<object>(filter.Values.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in filter.Values)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                context.Add(key, GridErrorCodes.InvalidValue);
                return null;
            }

            var text = element.GetString()!;
            if (text.Length > GridLimits.MaxTextValueLength)
            {
                context.Add(key, GridErrorCodes.TooLong);
                return null;
            }

            if (!seen.Add(text))
            {
                continue;
            }

            if (column.IntegerValues)
            {
                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                {
                    context.Add(key, GridErrorCodes.InvalidValue);
                    return null;
                }

                values.Add(number);
            }
            else
            {
                values.Add(text);
            }
        }

        return new GridPlanFilter { Column = column, Op = op, SetValues = values };
    }

    private static GridPlanFilter? ResolveBoolean(GridColumn column, GridOperator op, GridFilter filter, string key, Context context)
    {
        if (Present(filter.ValueTo) || filter.Values is not null)
        {
            context.Add(key, GridErrorCodes.ValueNotAllowed);
            return null;
        }

        if (!Present(filter.Value))
        {
            context.Add(key, GridErrorCodes.ValueRequired);
            return null;
        }

        var kind = filter.Value!.Value.ValueKind;
        if (kind is not (JsonValueKind.True or JsonValueKind.False))
        {
            context.Add(key, GridErrorCodes.InvalidValue);
            return null;
        }

        return new GridPlanFilter { Column = column, Op = op, Boolean = kind == JsonValueKind.True };
    }

    private static bool TryNumber(JsonElement? element, string key, Context context, out decimal value)
    {
        value = 0;
        if (!Present(element))
        {
            context.Add(key, GridErrorCodes.ValueRequired);
            return false;
        }

        if (element!.Value.ValueKind != JsonValueKind.Number || !element.Value.TryGetDecimal(out value))
        {
            context.Add(key, GridErrorCodes.InvalidValue);
            return false;
        }

        return true;
    }

    private static bool TryDate(JsonElement? element, string key, Context context, out DateOnly value)
    {
        value = default;
        if (!Present(element))
        {
            context.Add(key, GridErrorCodes.ValueRequired);
            return false;
        }

        if (element!.Value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(
                element.Value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            context.Add(key, GridErrorCodes.InvalidValue);
            return false;
        }

        if (value < GridLimits.MinDate || value > GridLimits.MaxDate)
        {
            context.Add(key, GridErrorCodes.OutOfRange);
            return false;
        }

        return true;
    }

    private static bool Present(JsonElement? element) =>
        element is { } e && e.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static bool IsNarrowed(IReadOnlyList<GridPlanFilter> filters, Context context)
    {
        var map = context.Map;
        if (map.GuardThreshold is { } threshold && context.Options.KnownRowCount is { } known && known <= threshold)
        {
            return true;
        }

        return filters.Any(f => f.Column.Indexed && !GridOperators.NotSelective.Contains(f.Op));
    }

    private static void EnforceGuard(IReadOnlyList<GridPlanFilter> filters, string? search, bool narrowed, GridColumnMap map)
    {
        if (map.GuardThreshold is not { } threshold || narrowed)
        {
            return;
        }

        var refused = filters
            .Where(f => f.Column.Type == GridColumnType.Text && GridOperators.Scan.Contains(f.Op))
            .Select(f => f.Column.Field)
            .ToList();
        if (search is not null)
        {
            refused.Add("search");
        }

        if (refused.Count > 0)
        {
            throw new GridFilterTooBroadException(refused, threshold);
        }
    }

    private static GridCapabilities CapabilitiesFor(GridColumnMap map, bool narrowed)
    {
        if (map.GuardThreshold is not { } threshold)
        {
            return new GridCapabilities();
        }

        return narrowed
            ? new GridCapabilities { Threshold = threshold }
            : new GridCapabilities
            {
                TextFilters = GridCapabilities.Disabled,
                Reason = GridCapabilities.ResultSetTooLarge,
                Threshold = threshold,
            };
    }

    private sealed class Context(GridColumnMap map, GridGuardOptions options)
    {
        private readonly Dictionary<string, List<string>> codes = new(StringComparer.Ordinal);

        public GridColumnMap Map { get; } = map;

        public GridGuardOptions Options { get; } = options;

        public bool ZoneInvalid { get; set; }

        public bool CanAccess(GridColumn column) =>
            !column.Restricted || (this.Options.CanAccessColumn?.Invoke(column) ?? false);

        /// <summary>A column the caller may use, or <c>null</c> (unknown and inaccessible look the same).</summary>
        public GridColumn? Lookup(string field)
        {
            var column = this.Map.Find(field);
            return column is not null && this.CanAccess(column) ? column : null;
        }

        /// <summary>Like <see cref="Lookup"/>, and gives the error key: the field id, or the redacted key when malformed.</summary>
        public GridColumn? LookupKey(string? rawKey, out string key)
        {
            if (!GridFieldNames.IsValid(rawKey))
            {
                key = GridErrorCodes.InvalidFieldKey;
                return null;
            }

            key = rawKey!;
            return this.Lookup(rawKey!);
        }

        public void Add(string key, string code)
        {
            if (!this.codes.TryGetValue(key, out var list))
            {
                list = [];
                this.codes.Add(key, list);
            }

            if (!list.Contains(code))
            {
                list.Add(code);
            }
        }

        public void ThrowIfInvalid()
        {
            if (this.codes.Count == 0)
            {
                return;
            }

            var errorCodes = this.codes.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
            var errors = this.codes.ToDictionary(
                p => p.Key, p => p.Value.Select(GridErrorCodes.Message).ToArray(), StringComparer.Ordinal);
            throw new GridRequestInvalidException(errors, errorCodes);
        }
    }
}
