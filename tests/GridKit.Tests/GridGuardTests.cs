namespace GridKit.Tests;

using GridKit.Contract;
using GridKit.Errors;
using static GridKit.Tests.G;

public class GridGuardTests
{
    private const string Secret = "' OR 1=1 --";

    [Fact]
    public void A_valid_request_resolves_to_a_plan_with_typed_values()
    {
        var plan = Resolve(Request(
            Filters(
                ("status", Set("open", "closed")),
                ("amount", Number("inRange", 10, 20)),
                ("active", Bool("equals", true)),
                ("regionId", Set("7", "42"))),
            sort: [Sort("amount", "desc")],
            page: 2,
            pageSize: 50));

        plan.Page.Should().Be(2);
        plan.PageSize.Should().Be(50);
        plan.Sorts.Should().ContainSingle().Which.Descending.Should().BeTrue();
        plan.Filters.Single(f => f.Column.Field == "amount").NumberTo.Should().Be(20);
        plan.Filters.Single(f => f.Column.Field == "active").Boolean.Should().BeTrue();
        plan.Filters.Single(f => f.Column.Field == "regionId").SetValues.Should().Equal(7L, 42L);
    }

    [Fact]
    public void Without_a_requested_sort_the_default_sort_is_used()
    {
        var plan = Resolve(Request());

        plan.Sorts.Should().ContainSingle().Which.Column.Field.Should().Be("name");
    }

    [Theory]
    [InlineData(-1, 100, "page")]
    [InlineData(0, 0, "pageSize")]
    [InlineData(0, 501, "pageSize")]
    public void Paging_limits_are_enforced(int page, int pageSize, string key)
    {
        var error = Invalid(Request(page: page, pageSize: pageSize));

        error.ErrorCodes.Should().ContainKey(key).WhoseValue.Should().Equal(GridErrorCodes.OutOfRange);
    }

    [Fact]
    public void At_most_three_sorts_are_accepted()
    {
        var error = Invalid(Request(sort: [Sort("name"), Sort("amount"), Sort("id"), Sort("code")]));

        error.ErrorCodes["sort"].Should().Equal(GridErrorCodes.TooMany);
    }

    [Fact]
    public void Sorts_are_checked_against_the_map()
    {
        var error = Invalid(Request(sort: [Sort("note"), Sort("nope"), Sort("name")]));
        var duplicate = Invalid(Request(sort: [Sort("name"), Sort("name", "desc")]));
        var direction = Invalid(Request(sort: [Sort("name", "sideways")]));

        error.ErrorCodes["note"].Should().Equal(GridErrorCodes.NotSortable);
        error.ErrorCodes["nope"].Should().Equal(GridErrorCodes.UnknownField);
        duplicate.ErrorCodes["name"].Should().Equal(GridErrorCodes.Duplicate);
        direction.ErrorCodes["sort"].Should().Equal(GridErrorCodes.InvalidValue);
    }

    [Fact]
    public void Search_is_capped_at_200_characters()
    {
        var error = Invalid(Request(search: new string('x', 201), filters: Filters(("status", Set("open")))));

        error.ErrorCodes["search"].Should().Equal(GridErrorCodes.TooLong);
    }

    [Fact]
    public void At_most_fifty_filters_are_accepted()
    {
        var filters = Enumerable.Range(0, 51).ToDictionary(i => $"f{i}", _ => Text("equals", "x"));

        var error = Invalid(Request(filters));

        error.ErrorCodes["filters"].Should().Equal(GridErrorCodes.TooMany);
    }

    [Fact]
    public void Set_values_are_capped_at_one_thousand()
    {
        var values = Enumerable.Range(0, 1001).Select(i => $"v{i}").ToArray();

        var error = Invalid(Request(Filters(("status", Set(values)))));

        error.ErrorCodes["status"].Should().Equal(GridErrorCodes.TooMany);
    }

    [Fact]
    public void Filters_are_checked_against_the_map()
    {
        var error = Invalid(Request(Filters(
            ("nope", Text("equals", "x")),
            ("hidden", Text("equals", "x")),
            ("name", Number("equals", 1)),
            ("amount", Number("startsWith", 1)),
            ("code", Text("inRange", "a")))));

        error.ErrorCodes["nope"].Should().Equal(GridErrorCodes.UnknownField);
        error.ErrorCodes["hidden"].Should().Equal(GridErrorCodes.NotFilterable);
        error.ErrorCodes["name"].Should().Equal(GridErrorCodes.TypeMismatch);
        error.ErrorCodes["amount"].Should().Equal(GridErrorCodes.UnsupportedOperator);
        error.ErrorCodes["code"].Should().Equal(GridErrorCodes.UnsupportedOperator);
    }

    [Fact]
    public void Values_must_fit_the_operator()
    {
        var error = Invalid(Request(Filters(
            ("name", Text("equals")),
            ("note", Text("blank", "x")),
            ("amount", Number("equals", 1, 2)),
            ("id", Number("inRange", 5, 1)),
            ("active", Bool("equals")))));

        error.ErrorCodes["name"].Should().Equal(GridErrorCodes.ValueRequired);
        error.ErrorCodes["note"].Should().Equal(GridErrorCodes.ValueNotAllowed);
        error.ErrorCodes["amount"].Should().Equal(GridErrorCodes.ValueNotAllowed);
        error.ErrorCodes["id"].Should().Equal(GridErrorCodes.OutOfRange);
        error.ErrorCodes["active"].Should().Equal(GridErrorCodes.ValueRequired);
    }

    [Fact]
    public void Value_types_are_checked()
    {
        var error = Invalid(Request(Filters(
            ("name", new GridFilter { Type = "text", Op = "equals", Value = J(42) }),
            ("amount", new GridFilter { Type = "number", Op = "equals", Value = J("12") }),
            ("active", new GridFilter { Type = "boolean", Op = "equals", Value = J("true") }),
            ("status", new GridFilter { Type = "set", Op = "in", Values = [J(1)] }),
            ("regionId", Set("7", "seven")))));

        error.ErrorCodes.Keys.Should().BeEquivalentTo("name", "amount", "active", "status", "regionId");
        error.ErrorCodes.Values.Should().AllSatisfy(codes => codes.Should().Equal(GridErrorCodes.InvalidValue));
    }

    [Theory]
    [InlineData("2026-13-01", GridErrorCodes.InvalidValue)]
    [InlineData("2026-1-1", GridErrorCodes.InvalidValue)]
    [InlineData("2026-01-01T00:00:00Z", GridErrorCodes.InvalidValue)]
    [InlineData("1752-12-31", GridErrorCodes.OutOfRange)]
    public void Dates_must_be_plain_days_from_1753(string value, string code)
    {
        var error = Invalid(Request(Filters(("auditedOn", Date("equals", value)))));

        error.ErrorCodes["auditedOn"].Should().Equal(code);
    }

    [Fact]
    public void Text_values_are_capped_at_200_characters()
    {
        var error = Invalid(Request(Filters(("code", Text("startsWith", new string('A', 201))))));

        error.ErrorCodes["code"].Should().Equal(GridErrorCodes.TooLong);
    }

    [Fact]
    public void A_local_date_filter_needs_a_valid_time_zone()
    {
        var missing = Invalid(Request(Filters(("createdOn", Date("equals", "2026-03-29")))));
        var unknown = Invalid(Request(Filters(("createdOn", Date("equals", "2026-03-29"))), timeZone: "Mars/Olympus"));
        var utcIgnoresZone = Resolve(Request(Filters(("auditedOn", Date("equals", "2026-03-29")))));

        missing.ErrorCodes["timeZone"].Should().Equal(GridErrorCodes.TimeZoneRequired);
        unknown.ErrorCodes["timeZone"].Should().Equal(GridErrorCodes.InvalidTimeZone);
        utcIgnoresZone.Filters.Should().ContainSingle();
    }

    [Fact]
    public void A_malformed_key_is_redacted_and_never_echoed()
    {
        var error = Invalid(Request(
            Filters(("x; DROP TABLE shipment", Text("equals", Secret))),
            sort: [Sort("Name'--")]));

        error.ErrorCodes.Should().ContainKey(GridErrorCodes.InvalidFieldKey)
            .WhoseValue.Should().Equal(GridErrorCodes.UnknownField);
        Everything(error).Should().NotContain(s => s.Contains("DROP") || s.Contains("Name'--"));
    }

    [Fact]
    public void Messages_never_contain_submitted_values()
    {
        var error = Invalid(Request(Filters(
            ("name", Text("equals", new string('x', 300))),
            ("amount", new GridFilter { Type = "number", Op = "equals", Value = J(Secret) }),
            ("auditedOn", Date("equals", Secret)),
            ("regionId", Set(Secret)),
            ("active", new GridFilter { Type = "boolean", Op = "equals", Value = J(Secret) }))));

        Everything(error).Should().NotContain(s => s.Contains(Secret) || s.Contains("xxxxxxxx"));
        error.Message.Should().NotContain(Secret);
    }

    [Fact]
    public void Blank_takes_no_value_and_an_empty_set_matches_nothing()
    {
        var plan = Resolve(Request(Filters(("note", Text("blank")), ("status", Set()))));

        plan.Filters.Should().HaveCount(2);
        plan.Filters.Single(f => f.Column.Field == "status").SetValues.Should().BeEmpty();
    }

    [Fact]
    public void Values_requests_need_a_filterable_set_column_and_ignore_its_own_filter()
    {
        var notSet = () => Guard.GridGuard.ResolveValues(new GridValuesRequest { Field = "name" }, Map);
        var plan = Guard.GridGuard.ResolveValues(
            new GridValuesRequest { Field = "status", Filters = Filters(("status", Set("open")), ("amount", Number("equals", 1))) },
            Map);

        notSet.Should().Throw<GridRequestInvalidException>().Which.ErrorCodes["field"].Should().Equal(GridErrorCodes.NotSetColumn);
        plan.Filters.Should().ContainSingle().Which.Column.Field.Should().Be("amount");
    }

    private static IEnumerable<string> Everything(GridRequestInvalidException error) =>
        error.Errors.Keys
            .Concat(error.Errors.Values.SelectMany(v => v))
            .Concat(error.ErrorCodes.Keys)
            .Concat(error.ErrorCodes.Values.SelectMany(v => v));
}
