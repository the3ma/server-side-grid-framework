namespace GridKit.Contract;

public enum GridColumnType
{
    Text,
    Number,
    Date,
    Set,
    Boolean,
}

public enum GridOperator
{
    Contains,
    NotContains,
    Equals,
    NotEqual,
    StartsWith,
    EndsWith,
    Blank,
    NotBlank,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    InRange,
    In,
}

/// <summary>How a date column's day boundaries are computed.</summary>
public enum GridDateSemantics
{
    /// <summary>UTC midnight; the request time zone is ignored. For regulated records.</summary>
    Utc,

    /// <summary>The user's local midnight (request time zone) converted to UTC; DST-safe.</summary>
    Local,

    /// <summary>A plain date (day-valued column).</summary>
    Calendar,
}
