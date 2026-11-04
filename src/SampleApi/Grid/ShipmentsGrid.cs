namespace SampleApi.Grid;

using GridKit.Columns;
using GridKit.Contract;

/// <summary>The <c>shipments</c> grid: its allow-list and its base SQL. This is all a new grid has to write.</summary>
public static class ShipmentsGrid
{
    public const int GuardThreshold = 50_000;

    public static readonly GridColumnMap<ShipmentRow> Map = GridColumnMap<ShipmentRow>
        .Create("shipments", "g.ShipmentId")
        .Number("shipmentId", "g.ShipmentId", c => c.Sortable().Filterable().Indexed())
        .Text("trackingNo", "g.TrackingNo", c => c.Sortable().Filterable().Searchable().Indexed().StoredUpperCase())
        .Set("warehouseCode", "g.WarehouseCode", c => c.Sortable().Filterable().Indexed())
        .Text("destination", "g.Destination", c => c.Sortable().Filterable().Searchable())
        .Set("status", "g.Status", c => c.Sortable().Filterable().Indexed())
        .Set("carrier", "g.Carrier", c => c.Filterable())
        .Number("weightKg", "g.WeightKg", c => c.Sortable().Filterable())
        .Date("shippedOn", "g.ShippedAt", GridDateSemantics.Local, c => c.Sortable().Filterable())
        .Date("deliveredOn", "g.DeliveredAt", GridDateSemantics.Utc, c => c.Sortable().Filterable())
        .Boolean("flagged", "g.Flagged", 1L, 0L, c => c.Filterable())
        .Set("priceTier", "g.PriceTier", c => c.Filterable().Restricted())
        .DefaultSortBy("shippedOn", descending: true)
        .GuardScanFilters(GuardThreshold);

    /// <summary>
    /// The row scope lives here, bound to principal-derived parameters (<c>@tenantId</c>, <c>@finance</c>).
    /// No ORDER BY and no paging: the framework adds both. Grid filters are AND-ed outside, so they can only narrow it.
    /// </summary>
    public const string BaseSql = """
        SELECT s.shipment_id    AS ShipmentId,
               s.tracking_no    AS TrackingNo,
               s.warehouse_code AS WarehouseCode,
               s.destination    AS Destination,
               s.status         AS Status,
               s.carrier        AS Carrier,
               s.weight_kg      AS WeightKg,
               s.shipped_at     AS ShippedAt,
               s.delivered_at   AS DeliveredAt,
               s.flagged        AS Flagged,
               CASE WHEN @finance = 1 THEN s.price_tier END AS PriceTier
        FROM shipment s
        WHERE s.tenant_id = @tenantId
        """;
}
