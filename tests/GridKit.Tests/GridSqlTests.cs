namespace GridKit.Tests;

using GridKit.Contract;
using GridKit.Dialects;
using GridKit.Guard;
using GridKit.Sql;
using static GridKit.Tests.G;

public class GridSqlTests
{
    private const string BaseSql = "SELECT * FROM items WHERE tenant_id = @tenantId";
    private const string Secret = "' OR 1=1 --";

    private static GridRequest Representative() => Request(
        Filters(
            ("name", Text("contains", "50%_off")),
            ("status", Set("open", "closed")),
            ("createdOn", Date("equals", "2026-03-29"))),
        sort: [Sort("name", "desc")],
        timeZone: "Europe/Athens",
        page: 2,
        pageSize: 25);

    private static GridPageCommands Build(GridRequest request, IGridDialect dialect) =>
        GridSql.BuildPage(Resolve(request), dialect, BaseSql);

    private static string N(string sql) => sql.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Sql_server_snapshot()
    {
        var commands = Build(Representative(), SqlServerGridDialect.Instance);

        N(commands.Page.Sql).Should().Be(N("""
            SELECT g.*
            FROM (
            SELECT * FROM items WHERE tenant_id = @tenantId
            ) g
            WHERE g.Name LIKE @grid_p0 ESCAPE '\'
              AND g.Status IN (SELECT [value] FROM OPENJSON(@grid_p1))
              AND g.CreatedAt >= @grid_p2 AND g.CreatedAt < @grid_p3
            ORDER BY g.Name DESC, g.Id ASC
            OFFSET @grid_offset ROWS FETCH NEXT @grid_fetch ROWS ONLY
            """));
        N(commands.Count!.Sql).Should().Be(N("""
            SELECT COUNT_BIG(*)
            FROM (
            SELECT * FROM items WHERE tenant_id = @tenantId
            ) g
            WHERE g.Name LIKE @grid_p0 ESCAPE '\'
              AND g.Status IN (SELECT [value] FROM OPENJSON(@grid_p1))
              AND g.CreatedAt >= @grid_p2 AND g.CreatedAt < @grid_p3
            """));
        commands.Page.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["grid_p0"] = "%50\\%\\_off%",
            ["grid_p1"] = "[\"open\",\"closed\"]",
            ["grid_p2"] = new DateTime(2026, 3, 28, 22, 0, 0, DateTimeKind.Utc),
            ["grid_p3"] = new DateTime(2026, 3, 29, 21, 0, 0, DateTimeKind.Utc),
            ["grid_offset"] = 50L,
            ["grid_fetch"] = 25,
        });
        commands.Count.Parameters.Should().NotContainKeys("grid_offset", "grid_fetch");
    }

    [Fact]
    public void Oracle_snapshot()
    {
        var commands = Build(Representative(), OracleGridDialect.Instance);

        N(commands.Page.Sql).Should().Be(N("""
            SELECT g.*
            FROM (
            SELECT * FROM items WHERE tenant_id = @tenantId
            ) g
            WHERE UPPER(g.Name) LIKE UPPER(:grid_p0) ESCAPE '\'
              AND g.Status IN (:grid_p1, :grid_p2)
              AND g.CreatedAt >= :grid_p3 AND g.CreatedAt < :grid_p4
            ORDER BY UPPER(g.Name) DESC NULLS LAST, g.Id ASC NULLS FIRST
            OFFSET :grid_offset ROWS FETCH NEXT :grid_fetch ROWS ONLY
            """));
        N(commands.Count!.Sql).Should().StartWith("SELECT COUNT(*)\n");
        commands.Page.Parameters["grid_p1"].Should().Be("open");
        commands.Page.Parameters["grid_p2"].Should().Be("closed");
    }

    [Fact]
    public void Sqlite_snapshot()
    {
        var commands = Build(Representative(), SqliteGridDialect.Instance);

        N(commands.Page.Sql).Should().Be(N("""
            SELECT g.*
            FROM (
            SELECT * FROM items WHERE tenant_id = @tenantId
            ) g
            WHERE g.Name LIKE @grid_p0 ESCAPE '\'
              AND g.Status IN (SELECT value FROM json_each(@grid_p1))
              AND g.CreatedAt >= @grid_p2 AND g.CreatedAt < @grid_p3
            ORDER BY g.Name COLLATE NOCASE DESC, g.Id ASC
            LIMIT @grid_fetch OFFSET @grid_offset
            """));
        commands.Page.Parameters["grid_p2"].Should().Be("2026-03-28T22:00:00Z");
        commands.Page.Parameters["grid_p3"].Should().Be("2026-03-29T21:00:00Z");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Sql_text_depends_only_on_the_request_shape(IGridDialect dialect)
    {
        var first = Build(Representative(), dialect);
        var other = Representative();
        other.Filters!["name"] = Text("contains", Secret);
        other.Filters["status"] = Set(Secret, "x");
        other.Filters["createdOn"] = Date("equals", "1999-12-31");

        var second = Build(other, dialect);

        second.Page.Sql.Should().Be(first.Page.Sql);
        second.Page.Sql.Should().NotContain("1=1");
        second.Page.Parameters.Values.OfType<string>().Should().Contain(v => v.Contains(Secret));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Search_is_an_or_of_contains_across_the_searchable_columns(IGridDialect dialect)
    {
        var commands = Build(Request(Filters(("status", Set("open"))), search: "ab_c"), dialect);

        var expected = dialect switch
        {
            OracleGridDialect => "(g.Code LIKE UPPER(:grid_p1) ESCAPE '\\' OR UPPER(g.Name) LIKE UPPER(:grid_p2) ESCAPE '\\')",
            SqliteGridDialect => "(g.Code LIKE UPPER(@grid_p1) ESCAPE '\\' OR g.Name LIKE @grid_p2 ESCAPE '\\')",
            _ => "(g.Code LIKE @grid_p1 ESCAPE '\\' OR g.Name LIKE @grid_p2 ESCAPE '\\')",
        };
        commands.Page.Sql.Should().Contain(expected);
        commands.Page.Parameters["grid_p2"].Should().Be("%ab\\_c%");
    }

    [Fact]
    public void Oracle_splits_large_sets_into_in_lists_of_200()
    {
        var values = Enumerable.Range(0, 450).Select(i => $"v{i}").ToArray();

        var sql = Build(Request(Filters(("status", Set(values)))), OracleGridDialect.Instance).Page.Sql;

        sql.Should().Contain("(g.Status IN (:grid_p0, ");
        sql.Split(" IN (").Should().HaveCount(4);
        sql.Should().Contain(":grid_p449)").And.Contain(") OR g.Status IN (:grid_p200, ");
    }

    [Fact]
    public void Integer_set_values_are_bound_as_numbers()
    {
        var oracle = Build(Request(Filters(("regionId", Set("7", "42")), ("status", Set("open")))), OracleGridDialect.Instance);
        var sqlServer = Build(Request(Filters(("regionId", Set("7", "42")), ("status", Set("open")))), SqlServerGridDialect.Instance);

        oracle.Page.Parameters["grid_p0"].Should().Be(7L);
        sqlServer.Page.Parameters["grid_p0"].Should().Be("[7,42]");
    }

    [Fact]
    public void Sql_server_escapes_its_bracket_wildcard()
    {
        var commands = Build(Request(Filters(("code", Text("startsWith", "a[b%")))), SqlServerGridDialect.Instance);

        commands.Page.Parameters["grid_p0"].Should().Be("a\\[b\\%%");
    }

    [Fact]
    public void Oracle_compares_stored_upper_case_columns_bare_so_a_plain_index_serves_them()
    {
        var sql = Build(Request(Filters(("code", Text("equals", "ab"))), sort: [Sort("code")]), OracleGridDialect.Instance).Page.Sql;

        sql.Should().Contain("WHERE g.Code = UPPER(:grid_p0)");
        sql.Should().Contain("ORDER BY g.Code ASC NULLS FIRST, g.Id ASC NULLS FIRST");
    }

    [Theory]
    [InlineData("sqlserver", "(g.Note IS NULL OR g.Note = '')")]
    [InlineData("oracle", "g.Note IS NULL")]
    [InlineData("sqlite", "(g.Note IS NULL OR g.Note = '')")]
    public void Blank_text_follows_the_database(string dialect, string expected)
    {
        var sql = Build(Request(Filters(("note", Text("blank")))), ByName(dialect)).Page.Sql;

        sql.Should().Contain($"WHERE {expected}\n");
    }

    [Fact]
    public void Negative_text_operators_include_blanks_and_number_not_equal_excludes_nulls()
    {
        var sql = Build(
            Request(Filters(("status", Set("open")), ("name", Text("notContains", "x")), ("amount", Number("notEqual", 3)))),
            SqlServerGridDialect.Instance).Page.Sql;

        sql.Should().Contain("(g.Name IS NULL OR g.Name NOT LIKE @grid_p1 ESCAPE '\\')");
        sql.Should().Contain("g.Amount <> @grid_p2");
    }

    [Fact]
    public void Date_not_equal_is_outside_the_day()
    {
        var sql = Build(Request(Filters(("auditedOn", Date("notEqual", "2026-01-15")))), SqlServerGridDialect.Instance).Page.Sql;

        sql.Should().Contain("(g.AuditedAt < @grid_p0 OR g.AuditedAt >= @grid_p1)");
    }

    [Fact]
    public void Booleans_bind_the_stored_values_and_an_empty_set_matches_nothing()
    {
        var commands = Build(Request(Filters(("active", Bool("equals", false)), ("status", Set()))), SqlServerGridDialect.Instance);

        commands.Page.Sql.Should().Contain("g.ActiveFlag = @grid_p0").And.Contain("AND 1 = 0");
        commands.Page.Parameters["grid_p0"].Should().Be("F");
    }

    [Fact]
    public void The_tiebreaker_is_always_last_and_never_repeated()
    {
        var byName = Build(Request(sort: [Sort("name")]), SqlServerGridDialect.Instance).Page.Sql;
        var byId = Build(Request(sort: [Sort("id", "desc")]), SqlServerGridDialect.Instance).Page.Sql;

        byName.Should().Contain("ORDER BY g.Name ASC, g.Id ASC\n");
        byId.Should().Contain("ORDER BY g.Id DESC\n");
    }

    [Fact]
    public void No_count_query_runs_when_the_total_is_not_requested()
    {
        var commands = Build(Request(includeTotal: false), SqlServerGridDialect.Instance);

        commands.Count.Should().BeNull();
        commands.Page.Sql.Should().NotContain(") g\nWHERE");
    }

    [Fact]
    public void Values_query_lists_distinct_non_null_values_one_past_the_limit()
    {
        var plan = GridGuard.ResolveValues(
            new GridValuesRequest { Field = "status", Filters = Filters(("amount", Number("greaterThan", 1))) }, Map);

        var command = GridSql.BuildValues(plan, SqliteGridDialect.Instance, BaseSql, 1001);

        N(command.Sql).Should().Be(N("""
            SELECT DISTINCT g.Status AS GridValue
            FROM (
            SELECT * FROM items WHERE tenant_id = @tenantId
            ) g
            WHERE g.Status IS NOT NULL
              AND g.Amount > @grid_p0
            ORDER BY GridValue
            LIMIT @grid_fetch
            """));
        command.Parameters["grid_fetch"].Should().Be(1001);
    }

    public static TheoryData<IGridDialect> Dialects() =>
        new() { SqlServerGridDialect.Instance, OracleGridDialect.Instance, SqliteGridDialect.Instance };

    private static IGridDialect ByName(string name) => name switch
    {
        "oracle" => OracleGridDialect.Instance,
        "sqlite" => SqliteGridDialect.Instance,
        _ => SqlServerGridDialect.Instance,
    };
}
