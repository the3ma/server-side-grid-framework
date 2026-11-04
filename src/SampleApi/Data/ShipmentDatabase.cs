namespace SampleApi.Data;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>Opens connections to the demo SQLite database.</summary>
public sealed class ShipmentConnectionFactory(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(this.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

/// <summary>Creates and seeds the demo database with deterministic, synthetic shipments.</summary>
public static class ShipmentDatabase
{
    private static readonly string[] Warehouses =
        ["WH-100", "WH-101", "WH-102", "WH-103", "WH-104", "WH-105", "WH-106", "WH-107", "WH-108", "WH-109", "WH-110"];

    private static readonly string[] Destinations =
    [
        "Lakeview Hardware", "Northgate Outfitters", "Harbor Point Books", "Oak Valley Garden Supply", "Westfield Furniture",
        "Summit Cycle Works", "Riverside Kitchenware", "Pinecrest Toys", "Old Town Paper Co.", "Meadowbrook Home Goods",
        "Bayside Marine Supply", "Greenway Pet Store",
    ];

    private static readonly (string Status, int Weight)[] Statuses =
        [("delivered", 45), ("received", 25), ("in_transit", 15), ("returned", 8), ("cancelled", 7)];

    private static readonly string[] Carriers = ["Northline", "Parcelway", "Bluecrate", "Swiftlane", "Overland"];

    private static readonly string[] PriceTiers = ["standard", "discount", "contract"];

    private static readonly DateTime Anchor = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates the schema and seeds <paramref name="rows"/> shipments when the table is empty.</summary>
    public static void EnsureSeeded(string connectionString, int rows, ILogger logger)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS shipment (
                shipment_id    INTEGER PRIMARY KEY,
                tenant_id      INTEGER NOT NULL,
                tracking_no    TEXT    NOT NULL,
                warehouse_code TEXT    NOT NULL,
                destination    TEXT    NOT NULL,
                status         TEXT    NOT NULL,
                carrier        TEXT    NULL,
                weight_kg      REAL    NULL,
                shipped_at     TEXT    NOT NULL,
                delivered_at   TEXT    NULL,
                flagged        INTEGER NOT NULL,
                price_tier     TEXT    NULL
            );
            """);

        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM shipment";
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        var watch = Stopwatch.StartNew();
        Seed(connection, rows);
        Execute(connection, """
            CREATE INDEX IF NOT EXISTS ix_shipment_tenant_tracking ON shipment (tenant_id, tracking_no);
            CREATE INDEX IF NOT EXISTS ix_shipment_tenant_warehouse ON shipment (tenant_id, warehouse_code);
            CREATE INDEX IF NOT EXISTS ix_shipment_tenant_status ON shipment (tenant_id, status);
            """);
        logger.LogInformation("Seeded {Rows} synthetic shipments in {ElapsedMs} ms", rows, watch.ElapsedMilliseconds);
    }

    /// <summary>Tenant of the <paramref name="shipmentId"/>-th shipment: two thirds tenant 1, one sixth each tenant 2 and 3.</summary>
    public static int TenantOf(long shipmentId, int rows) =>
        shipmentId <= rows * 2L / 3 ? 1 : shipmentId <= rows * 5L / 6 ? 2 : 3;

    private static void Seed(SqliteConnection connection, int rows)
    {
        var random = new Random(20_260_630);
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO shipment (shipment_id, tenant_id, tracking_no, warehouse_code, destination, status, carrier,
                                  weight_kg, shipped_at, delivered_at, flagged, price_tier)
            VALUES ($id, $tenant, $tracking, $warehouse, $destination, $status, $carrier, $weight, $shipped, $delivered, $flagged, $tier)
            """;
        var names = new[] { "$id", "$tenant", "$tracking", "$warehouse", "$destination", "$status", "$carrier", "$weight", "$shipped", "$delivered", "$flagged", "$tier" };
        var parameters = names.Select(name => insert.Parameters.Add(new SqliteParameter { ParameterName = name })).ToArray();
        insert.Prepare();

        for (long id = 1; id <= rows; id++)
        {
            var status = PickStatus(random);
            var shipped = Anchor.AddSeconds(-random.NextInt64(0, 730L * 24 * 3600));
            DateTime? delivered = status is "delivered" or "returned" ? shipped.AddHours(random.Next(6, 120)) : null;

            parameters[0].Value = id;
            parameters[1].Value = TenantOf(id, rows);
            parameters[2].Value = $"TRK-{id:D7}";
            parameters[3].Value = Warehouses[random.Next(Warehouses.Length)];
            parameters[4].Value = Destinations[random.Next(Destinations.Length)];
            parameters[5].Value = status;
            parameters[6].Value = random.Next(100) < 5 ? DBNull.Value : Carriers[random.Next(Carriers.Length)];
            parameters[7].Value = random.Next(100) < 3 ? DBNull.Value : Math.Round(0.5 + (random.NextDouble() * 29.5), 1);
            parameters[8].Value = Iso(shipped);
            parameters[9].Value = delivered is { } d ? Iso(d) : DBNull.Value;
            parameters[10].Value = random.Next(100) < 7 ? 1 : 0;
            parameters[11].Value = PriceTiers[random.Next(PriceTiers.Length)];
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string PickStatus(Random random)
    {
        var roll = random.Next(100);
        foreach (var (status, weight) in Statuses)
        {
            if (roll < weight)
            {
                return status;
            }

            roll -= weight;
        }

        return Statuses[0].Status;
    }

    private static string Iso(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
