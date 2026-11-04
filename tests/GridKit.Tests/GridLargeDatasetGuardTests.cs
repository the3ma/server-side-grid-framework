namespace GridKit.Tests;

using GridKit.Contract;
using GridKit.Errors;
using GridKit.Guard;
using static GridKit.Tests.G;

public class GridLargeDatasetGuardTests
{
    [Theory]
    [InlineData("contains")]
    [InlineData("notContains")]
    [InlineData("endsWith")]
    public void Scan_text_filters_are_refused_until_an_indexed_filter_narrows_the_set(string op)
    {
        var act = () => Resolve(Request(Filters(("name", Text(op, "hardware")))));

        var error = act.Should().Throw<GridFilterTooBroadException>().Which;
        error.Fields.Should().Equal("name");
        error.Threshold.Should().Be(50_000);
    }

    [Fact]
    public void The_quick_search_is_a_scan_too()
    {
        var act = () => Resolve(Request(search: "hardware"));

        act.Should().Throw<GridFilterTooBroadException>().Which.Fields.Should().Equal("search");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("code")]
    [InlineData("id")]
    public void A_selective_filter_on_an_indexed_column_lifts_the_guard(string field)
    {
        var narrowing = field switch
        {
            "status" => Set("open"),
            "code" => Text("startsWith", "AB"),
            _ => Number("lessThan", 1000),
        };

        var plan = Resolve(Request(Filters(("name", Text("contains", "hardware")), (field, narrowing)), search: "x"));

        plan.Capabilities.TextFilters.Should().Be(GridCapabilities.Enabled);
        plan.Capabilities.Threshold.Should().Be(50_000);
    }

    [Theory]
    [InlineData("notEqual")]
    [InlineData("blank")]
    [InlineData("notBlank")]
    [InlineData("contains")]
    public void Non_selective_operators_on_indexed_columns_do_not_lift_it(string op)
    {
        var indexed = op is "blank" or "notBlank" ? Text(op) : Text(op, "AB");

        var act = () => Resolve(Request(Filters(("name", Text("contains", "hardware")), ("code", indexed))));

        act.Should().Throw<GridFilterTooBroadException>();
    }

    [Fact]
    public void A_known_small_scoped_set_lifts_it()
    {
        var plan = Resolve(Request(search: "hardware"), options: new GridGuardOptions { KnownRowCount = 50_000 });
        var tooBig = () => Resolve(Request(search: "hardware"), options: new GridGuardOptions { KnownRowCount = 50_001 });

        plan.Capabilities.TextFilters.Should().Be(GridCapabilities.Enabled);
        tooBig.Should().Throw<GridFilterTooBroadException>();
    }

    [Fact]
    public void Every_page_reports_whether_scan_filters_are_available()
    {
        var plan = Resolve(Request(Filters(("amount", Number("greaterThan", 3)))));

        plan.Capabilities.TextFilters.Should().Be(GridCapabilities.Disabled);
        plan.Capabilities.Reason.Should().Be(GridCapabilities.ResultSetTooLarge);
        plan.Capabilities.Threshold.Should().Be(50_000);
    }

    [Fact]
    public void A_grid_without_a_threshold_never_refuses()
    {
        var plan = Resolve(Request(Filters(("name", Text("contains", "x"))), search: "y"), Unguarded);

        plan.Capabilities.TextFilters.Should().Be(GridCapabilities.Enabled);
        plan.Capabilities.Threshold.Should().BeNull();
    }

    [Fact]
    public void A_values_request_is_judged_on_its_own_filters()
    {
        // The status filter is the only narrowing one, and a values request for status ignores it.
        var request = new GridValuesRequest
        {
            Field = "status",
            Search = "hardware",
            Filters = Filters(("status", Set("open"))),
        };

        var act = () => GridGuard.ResolveValues(request, Map);

        act.Should().Throw<GridFilterTooBroadException>().Which.Fields.Should().Equal("search");
    }
}
