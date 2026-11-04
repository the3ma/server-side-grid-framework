namespace GridKit.Tests;

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using GridKit.Dialects;
using GridKit.Errors;
using GridKit.Execution;
using Microsoft.Data.Sqlite;
using static GridKit.Tests.G;

public class GridExecutorTests
{
    private const string Secret = "secret-value-123";

    [Fact]
    public async Task A_query_that_outlives_the_grid_timeout_becomes_a_504()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // A deliberately slow base query: a recursive counter to 50 million.
        const string slow = """
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 50000000)
            SELECT x AS Id FROM n
            """;
        var map = GridKit.Columns.GridColumnMap<TestRow>.Create("slow", "g.Id").Number("id", "g.Id", c => c.Sortable());

        var act = () => new GridExecutor().QueryPageAsync(
            connection, SqliteGridDialect.Instance, slow, map, Request(), null,
            new GridExecutionOptions { Timeout = TimeSpan.FromMilliseconds(200) });

        (await act.Should().ThrowAsync<GridQueryTimeoutException>()).Which.TimeoutSeconds.Should().Be(1);
    }

    [Fact]
    public async Task A_caller_that_went_away_is_not_reported_as_a_timeout()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => new GridExecutor().QueryPageAsync(
            connection, SqliteGridDialect.Instance, "SELECT 1 AS Id", Unguarded, Request(), null, null, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_driver_conversion_error_is_rethrown_value_free_naming_the_filtered_fields()
    {
        using var connection = new FailingConnection(new FakeDriverException(245, $"Conversion failed when converting '{Secret}'"));

        var act = () => new GridExecutor().QueryPageAsync(
            connection, SqlServerGridDialect.Instance, "SELECT 1 AS Id", Unguarded,
            Request(Filters(("amount", Number("equals", 3))), search: "abc"), null);

        var error = (await act.Should().ThrowAsync<GridRequestInvalidException>()).Which;
        error.ErrorCodes.Keys.Should().BeEquivalentTo("amount", "search");
        error.InnerException.Should().BeNull();
        error.ToString().Should().NotContain(Secret);
    }

    [Fact]
    public async Task A_driver_conversion_error_without_filters_is_a_value_free_500()
    {
        using var connection = new FailingConnection(new FakeDriverException(1722, $"ORA-01722: invalid number '{Secret}'"));

        var act = () => new GridExecutor().QueryPageAsync(
            connection, OracleGridDialect.Instance, "SELECT 1 AS Id FROM dual", Unguarded, Request(), null);

        var error = (await act.Should().ThrowAsync<GridQueryFailedException>()).Which;
        error.InnerException.Should().BeNull();
        error.ToString().Should().NotContain(Secret);
    }

    [Fact]
    public async Task A_driver_timeout_code_maps_to_504()
    {
        using var connection = new FailingConnection(new FakeDriverException(-2, "Execution Timeout Expired."));

        var act = () => new GridExecutor().QueryPageAsync(
            connection, SqlServerGridDialect.Instance, "SELECT 1 AS Id", Unguarded, Request(), null);

        await act.Should().ThrowAsync<GridQueryTimeoutException>();
    }

    [Fact]
    public async Task Other_driver_errors_are_not_disguised()
    {
        using var connection = new FailingConnection(new FakeDriverException(208, "Invalid object name."));

        var act = () => new GridExecutor().QueryPageAsync(
            connection, SqlServerGridDialect.Instance, "SELECT 1 AS Id", Unguarded, Request(), null);

        await act.Should().ThrowAsync<FakeDriverException>();
    }

    /// <summary>Stands in for a provider exception: drivers expose their error code as <c>Number</c>.</summary>
    public sealed class FakeDriverException(int number, string message) : DbException(message)
    {
        public int Number { get; } = number;
    }

    /// <summary>A connection whose every command fails with the given driver exception.</summary>
    private sealed class FailingConnection(DbException failure) : DbConnection
    {
        private ConnectionState state = ConnectionState.Open;

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "1";

        public override ConnectionState State => this.state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => this.state = ConnectionState.Closed;

        public override void Open() => this.state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FailingCommand(failure) { Connection = this };
    }

    private sealed class FailingCommand(DbException failure) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new SqliteCommand().Parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw failure;

        public override object ExecuteScalar() => throw failure;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new SqliteParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw failure;
    }
}
