namespace SampleApi.Grid;

using System.Text.Json.Serialization;

/// <summary>
/// One row of the <c>shipments</c> grid. Property names equal the base SQL projection aliases (for Dapper);
/// JSON names equal the grid's field ids (for the client).
/// </summary>
public sealed class ShipmentRow
{
    public long ShipmentId { get; init; }

    public string TrackingNo { get; init; } = string.Empty;

    public string WarehouseCode { get; init; } = string.Empty;

    public string Destination { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Carrier { get; init; }

    public double? WeightKg { get; init; }

    /// <summary>ISO-8601 UTC instant.</summary>
    [JsonPropertyName("shippedOn")]
    public string ShippedAt { get; init; } = string.Empty;

    /// <summary>ISO-8601 UTC instant; the proof-of-delivery record date.</summary>
    [JsonPropertyName("deliveredOn")]
    public string? DeliveredAt { get; init; }

    /// <summary>Stored as 1 / 0.</summary>
    [JsonIgnore]
    public long Flagged { get; init; }

    [JsonPropertyName("flagged")]
    public bool IsFlagged => this.Flagged == 1;

    /// <summary><c>null</c> unless the caller has the finance role (the base SQL nulls it).</summary>
    public string? PriceTier { get; init; }
}
