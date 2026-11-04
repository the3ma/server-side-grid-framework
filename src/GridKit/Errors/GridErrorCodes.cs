namespace GridKit.Errors;

/// <summary>Machine-readable error codes and their value-free messages.</summary>
public static class GridErrorCodes
{
    public const string UnknownField = "unknownField";
    public const string NotSortable = "notSortable";
    public const string NotFilterable = "notFilterable";
    public const string TypeMismatch = "typeMismatch";
    public const string UnsupportedOperator = "unsupportedOperator";
    public const string ValueRequired = "valueRequired";
    public const string ValueNotAllowed = "valueNotAllowed";
    public const string InvalidValue = "invalidValue";
    public const string TooLong = "tooLong";
    public const string TooMany = "tooMany";
    public const string OutOfRange = "outOfRange";
    public const string Duplicate = "duplicate";
    public const string TimeZoneRequired = "timeZoneRequired";
    public const string InvalidTimeZone = "invalidTimeZone";
    public const string NotSetColumn = "notSetColumn";

    /// <summary>Key used for a filter or sort key that is not a well-formed field id; the raw key is never echoed.</summary>
    public const string InvalidFieldKey = "(invalid field)";

    public static string Message(string code) => code switch
    {
        UnknownField => "Unknown field.",
        NotSortable => "This field cannot be sorted.",
        NotFilterable => "This field cannot be filtered.",
        TypeMismatch => "The filter type does not match the column type.",
        UnsupportedOperator => "This operator is not supported for this field.",
        ValueRequired => "A value is required.",
        ValueNotAllowed => "A value is not allowed here.",
        InvalidValue => "The value is not valid for this field.",
        TooLong => "The value is too long.",
        TooMany => "Too many items.",
        OutOfRange => "The value is out of range.",
        Duplicate => "The field is used more than once.",
        TimeZoneRequired => "A time zone is required when a local date is filtered.",
        InvalidTimeZone => "The time zone is not a known time zone id.",
        NotSetColumn => "Values can only be listed for a filterable set column.",
        _ => "The request is invalid.",
    };
}
