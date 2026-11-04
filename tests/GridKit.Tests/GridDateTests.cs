namespace GridKit.Tests;

using GridKit.Contract;
using GridKit.Guard;
using static GridKit.Tests.G;

public class GridDateTests
{
    private static DateTime Utc(int y, int mo, int d, int h = 0) => new(y, mo, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Iana_time_zone_ids_resolve_on_every_os()
    {
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Athens", out _).Should().BeTrue();
        TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out _).Should().BeTrue();
    }

    [Fact]
    public void Utc_columns_use_utc_midnight_and_ignore_the_request_zone()
    {
        var plan = Resolve(Request(Filters(("auditedOn", Date("equals", "2026-03-29"))), timeZone: "America/New_York"));

        var filter = plan.Filters.Single();
        filter.DateFrom.Should().Be(Utc(2026, 3, 29));
        filter.DateTo.Should().Be(Utc(2026, 3, 30));
    }

    [Fact]
    public void Local_columns_use_the_users_midnight_across_a_dst_change()
    {
        // Europe/Athens moves from UTC+2 to UTC+3 at 03:00 local on 2026-03-29: that day is 23 hours long.
        var plan = Resolve(Request(Filters(("createdOn", Date("equals", "2026-03-29"))), timeZone: "Europe/Athens"));

        var filter = plan.Filters.Single();
        filter.DateFrom.Should().Be(Utc(2026, 3, 28, 22));
        filter.DateTo.Should().Be(Utc(2026, 3, 29, 21));
        (filter.DateTo - filter.DateFrom).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void Local_midnight_that_does_not_exist_starts_at_the_first_valid_minute()
    {
        // America/Havana jumps from 00:00 to 01:00 on its spring-forward day; 2025-03-09 is one such day.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        var start = GridDates.StartOf(new DateOnly(2025, 3, 9), GridDateSemantics.Local, zone);

        zone.IsInvalidTime(new DateTime(2025, 3, 9, 0, 0, 0)).Should().BeTrue();
        start.Should().Be(Utc(2025, 3, 9, 5));
    }

    [Theory]
    [InlineData("lessThan", null, "2026-03-10")]
    [InlineData("lessThanOrEqual", null, "2026-03-11")]
    [InlineData("greaterThan", "2026-03-11", null)]
    [InlineData("greaterThanOrEqual", "2026-03-10", null)]
    public void Every_operator_is_half_open(string op, string? from, string? to)
    {
        var plan = Resolve(Request(Filters(("auditedOn", Date(op, "2026-03-10")))));

        var filter = plan.Filters.Single();
        filter.DateFrom.Should().Be(from is null ? null : DateTime.SpecifyKind(DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
        filter.DateTo.Should().Be(to is null ? null : DateTime.SpecifyKind(DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
    }

    [Fact]
    public void In_range_includes_its_last_day()
    {
        var plan = Resolve(Request(Filters(("auditedOn", Date("inRange", "2026-03-01", "2026-03-31")))));

        plan.Filters.Single().DateTo.Should().Be(Utc(2026, 4, 1));
    }

    [Fact]
    public void Local_bounds_are_clamped_to_the_minimum_date()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        GridDates.StartOf(new DateOnly(1753, 1, 1), GridDateSemantics.Local, zone)
            .Should().Be(new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
