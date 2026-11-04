namespace GridKit.Guard;

using GridKit.Contract;

/// <summary>
/// Day boundaries per date semantics. Every date condition is half-open:
/// <c>equals d</c> is <c>&gt;= start(d) AND &lt; start(d + 1)</c>. The column decides the semantics, never the client.
/// </summary>
public static class GridDates
{
    private static readonly DateTime Floor = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The instant a calendar day starts for a column.</summary>
    /// <param name="day">The calendar day.</param>
    /// <param name="semantics">The column's semantics.</param>
    /// <param name="zone">The user's zone; used only for <see cref="GridDateSemantics.Local"/>.</param>
    public static DateTime StartOf(DateOnly day, GridDateSemantics semantics, TimeZoneInfo? zone)
    {
        switch (semantics)
        {
            case GridDateSemantics.Utc:
                return day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            case GridDateSemantics.Calendar:
                return day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            default:
                var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                var tz = zone ?? TimeZoneInfo.Utc;

                // A DST jump at midnight makes local midnight not exist: the day starts at the first valid minute.
                var guard = 0;
                while (tz.IsInvalidTime(local) && guard++ < 24 * 60)
                {
                    local = local.AddMinutes(1);
                }

                var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                return utc < Floor ? Floor : utc;
        }
    }

    /// <summary>Converts one operator into the half-open <c>[from, to)</c> bounds it needs.</summary>
    public static (DateTime? From, DateTime? To) Bounds(
        GridOperator op, DateOnly value, DateOnly? valueTo, GridDateSemantics semantics, TimeZoneInfo? zone)
    {
        DateTime Start(DateOnly d) => StartOf(d, semantics, zone);

        return op switch
        {
            GridOperator.Equals or GridOperator.NotEqual => (Start(value), Start(value.AddDays(1))),
            GridOperator.LessThan => (null, Start(value)),
            GridOperator.LessThanOrEqual => (null, Start(value.AddDays(1))),
            GridOperator.GreaterThan => (Start(value.AddDays(1)), null),
            GridOperator.GreaterThanOrEqual => (Start(value), null),
            GridOperator.InRange => (Start(value), Start(valueTo!.Value.AddDays(1))),
            _ => (null, null),
        };
    }
}
