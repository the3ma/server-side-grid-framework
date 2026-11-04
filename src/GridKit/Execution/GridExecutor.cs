namespace GridKit.Execution;

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using GridKit.Columns;
using GridKit.Contract;
using GridKit.Dialects;
using GridKit.Errors;
using GridKit.Guard;
using GridKit.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Server-derived options for one grid call.</summary>
public sealed class GridExecutionOptions
{
    public static readonly GridExecutionOptions Default = new();

    /// <summary>Opens restricted columns for this caller.</summary>
    public Func<GridColumn, bool>? CanAccessColumn { get; init; }

    /// <summary>Known row count of the caller's scoped set; lifts the guard at or under the threshold.</summary>
    public long? KnownRowCount { get; init; }

    /// <summary>Per-request timeout; also the ADO.NET command timeout.</summary>
    public TimeSpan Timeout { get; init; } = GridLimits.DefaultTimeout;
}

/// <summary>Runs grid requests: validate, build, execute, translate failures. One instance is safe to share.</summary>
public interface IGridExecutor
{
    Task<GridPage<TRow>> QueryPageAsync<TRow>(
        IDbConnection connection,
        IGridDialect dialect,
        string baseSql,
        GridColumnMap<TRow> map,
        GridRequest request,
        object? scopeParameters,
        GridExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<GridValuesPage> QueryValuesAsync<TRow>(
        IDbConnection connection,
        IGridDialect dialect,
        string baseSql,
        GridColumnMap<TRow> map,
        GridValuesRequest request,
        object? scopeParameters,
        GridExecutionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class GridExecutor : IGridExecutor
{
    private readonly ILogger<GridExecutor> logger;

    public GridExecutor(ILogger<GridExecutor>? logger = null)
    {
        this.logger = logger ?? NullLogger<GridExecutor>.Instance;
    }

    public async Task<GridPage<TRow>> QueryPageAsync<TRow>(
        IDbConnection connection,
        IGridDialect dialect,
        string baseSql,
        GridColumnMap<TRow> map,
        GridRequest request,
        object? scopeParameters,
        GridExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= GridExecutionOptions.Default;
        var plan = GridGuard.Resolve(request, map, GuardOptions(options));
        var commands = GridSql.BuildPage(plan, dialect, baseSql);
        var watch = Stopwatch.StartNew();

        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var rows = (await connection.QueryAsync<TRow>(
                Definition(commands.Page, scopeParameters, options, linked.Token)).ConfigureAwait(false)).AsList();

            long? total = null;
            if (commands.Count is not null)
            {
                total = await connection.ExecuteScalarAsync<long>(
                    Definition(commands.Count, scopeParameters, options, linked.Token)).ConfigureAwait(false);
            }

            this.LogPage(plan, rows.Count, total, watch.ElapsedMilliseconds);
            return new GridPage<TRow> { Rows = rows, TotalCount = total, Capabilities = plan.Capabilities };
        }
        catch (Exception ex) when (Translate(ex, dialect, plan.NarrowingKeys, options, timeout, cancellationToken) is { } translated)
        {
            this.logger.LogWarning(
                "Grid {Grid} failed with {ProblemType} after {ElapsedMs} ms",
                map.Name,
                translated.ProblemType,
                watch.ElapsedMilliseconds);
            throw translated;
        }
    }

    public async Task<GridValuesPage> QueryValuesAsync<TRow>(
        IDbConnection connection,
        IGridDialect dialect,
        string baseSql,
        GridColumnMap<TRow> map,
        GridValuesRequest request,
        object? scopeParameters,
        GridExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= GridExecutionOptions.Default;
        var plan = GridGuard.ResolveValues(request, map, GuardOptions(options));
        var command = GridSql.BuildValues(plan, dialect, baseSql, GridLimits.MaxDistinctValues + 1);
        var watch = Stopwatch.StartNew();

        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var raw = await connection.QueryAsync<object>(
                Definition(command, scopeParameters, options, linked.Token)).ConfigureAwait(false);
            var values = raw
                .Select(row => ((IDictionary<string, object>)row)["GridValue"])
                .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
                .ToList();
            var truncated = values.Count > GridLimits.MaxDistinctValues;
            if (truncated)
            {
                values.RemoveRange(GridLimits.MaxDistinctValues, values.Count - GridLimits.MaxDistinctValues);
            }

            this.logger.LogInformation(
                "Grid {Grid} values {Field} filters {Filters} search {HasSearch} -> {ValueCount} values (truncated {Truncated}) in {ElapsedMs} ms",
                map.Name,
                plan.Column.Field,
                Describe(plan.Filters),
                plan.Search is not null,
                values.Count,
                truncated,
                watch.ElapsedMilliseconds);
            return new GridValuesPage { Values = values, Truncated = truncated };
        }
        catch (Exception ex) when (Translate(ex, dialect, plan.NarrowingKeys, options, timeout, cancellationToken) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>Builds a PHI-free summary of the filters: field ids, operators and set-value counts only.</summary>
    public static string Describe(IEnumerable<GridPlanFilter> filters) =>
        string.Join(
            ", ",
            filters.Select(f => f.SetValues is { } values
                ? $"{f.Column.Field}:{GridWire.Name(f.Op)}({values.Count})"
                : $"{f.Column.Field}:{GridWire.Name(f.Op)}"));

    private static GridGuardOptions GuardOptions(GridExecutionOptions options) => new()
    {
        CanAccessColumn = options.CanAccessColumn,
        KnownRowCount = options.KnownRowCount,
    };

    private static CommandDefinition Definition(
        GridCommand command, object? scope, GridExecutionOptions options, CancellationToken token)
    {
        var parameters = new DynamicParameters(scope);
        foreach (var (name, value) in command.Parameters)
        {
            parameters.Add(name, value);
        }

        return new CommandDefinition(
            command.Sql,
            parameters,
            commandTimeout: (int)Math.Ceiling(options.Timeout.TotalSeconds),
            cancellationToken: token);
    }

    /// <summary>
    /// Replaces driver failures with value-free grid exceptions. The driver exception is never chained: its message
    /// can echo a request value or a stored value. Returns <c>null</c> to rethrow the original (caller cancelled, or
    /// an unrelated failure).
    /// </summary>
    private static GridException? Translate(
        Exception exception,
        IGridDialect dialect,
        IReadOnlyList<string> narrowingKeys,
        GridExecutionOptions options,
        CancellationTokenSource timeout,
        CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
        {
            return null;
        }

        var timeoutSeconds = (int)Math.Ceiling(options.Timeout.TotalSeconds);
        if (timeout.IsCancellationRequested && exception is OperationCanceledException or DbException)
        {
            return new GridQueryTimeoutException(timeoutSeconds);
        }

        if (exception is not DbException db)
        {
            return null;
        }

        if (dialect.IsTimeout(db))
        {
            return new GridQueryTimeoutException(timeoutSeconds);
        }

        if (dialect.IsValueConversionError(db))
        {
            if (narrowingKeys.Count == 0)
            {
                return new GridQueryFailedException();
            }

            var keys = narrowingKeys.Distinct(StringComparer.Ordinal).ToArray();
            return new GridRequestInvalidException(
                keys.ToDictionary(k => k, _ => new[] { GridErrorCodes.Message(GridErrorCodes.InvalidValue) }),
                keys.ToDictionary(k => k, _ => new[] { GridErrorCodes.InvalidValue }));
        }

        return null;
    }

    private void LogPage(GridPlan plan, int rowCount, long? total, long elapsedMs) =>
        this.logger.LogInformation(
            "Grid {Grid} page {Page} size {PageSize} sort {Sort} filters {Filters} search {HasSearch} -> {RowCount} rows, total {TotalCount}, text filters {TextFilters} in {ElapsedMs} ms",
            plan.Map.Name,
            plan.Page,
            plan.PageSize,
            string.Join(", ", plan.Sorts.Select(s => $"{s.Column.Field} {(s.Descending ? "desc" : "asc")}")),
            Describe(plan.Filters),
            plan.Search is not null,
            rowCount,
            total?.ToString(CultureInfo.InvariantCulture) ?? "not computed",
            plan.Capabilities.TextFilters,
            elapsedMs);
}
