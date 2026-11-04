namespace GridKit.Tests;

using GridKit.Contract;
using GridKit.Errors;
using GridKit.Guard;
using static GridKit.Tests.G;

public class GridRestrictedColumnTests
{
    private static readonly GridGuardOptions Finance = new() { CanAccessColumn = column => column.Field == "tier" };

    [Fact]
    public void A_restricted_column_is_an_unknown_field_for_every_caller_by_default()
    {
        var filter = Invalid(Request(Filters(("tier", Set("contract")), ("status", Set("open")))));
        var values = () => GridGuard.ResolveValues(new GridValuesRequest { Field = "tier" }, Map);

        filter.ErrorCodes["tier"].Should().Equal(GridErrorCodes.UnknownField);
        values.Should().Throw<GridRequestInvalidException>().Which.ErrorCodes["field"].Should().Equal(GridErrorCodes.UnknownField);
    }

    [Fact]
    public void Unknown_and_restricted_look_exactly_the_same()
    {
        var restricted = Invalid(Request(Filters(("tier", Set("contract")))));
        var unknown = Invalid(Request(Filters(("tierX", Set("contract")))));

        restricted.Errors["tier"].Should().Equal(unknown.Errors["tierX"]);
    }

    [Fact]
    public void The_server_can_open_it_per_caller()
    {
        var plan = Resolve(Request(Filters(("tier", Set("contract")), ("status", Set("open")))), options: Finance);
        var values = GridGuard.ResolveValues(new GridValuesRequest { Field = "tier" }, Map, Finance);

        plan.Filters.Should().Contain(f => f.Column.Field == "tier");
        values.Column.Field.Should().Be("tier");
    }
}
