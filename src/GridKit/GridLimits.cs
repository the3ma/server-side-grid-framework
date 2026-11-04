namespace GridKit;

/// <summary>Every limit of the contract in one place.</summary>
public static class GridLimits
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
    public const int MaxSorts = 3;
    public const int MaxFilters = 50;
    public const int MaxSetValues = 1000;
    public const int MaxSearchLength = 200;
    public const int MaxTextValueLength = 200;
    public const int MaxDistinctValues = 1000;
    public const int MaxTimeZoneLength = 64;

    /// <summary>Oracle refuses more than 1000 items in one IN list (ORA-01795); chunk well below it.</summary>
    public const int OracleInListChunk = 200;

    /// <summary>The SQL Server <c>datetime</c> floor; earlier dates are refused.</summary>
    public static readonly DateOnly MinDate = new(1753, 1, 1);

    /// <summary>Leaves room for the exclusive "next day" bound.</summary>
    public static readonly DateOnly MaxDate = new(9998, 12, 31);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
}
