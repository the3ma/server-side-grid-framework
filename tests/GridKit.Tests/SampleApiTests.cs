namespace GridKit.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using SampleApi.Data;

/// <summary>Hosts the real SampleApi in memory over a temporary, seeded SQLite file.</summary>
public sealed class SampleApiFactory : WebApplicationFactory<Program>
{
    public const int Rows = 3000;

    private readonly string path = Path.Combine(Path.GetTempPath(), $"gridkit-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Shipments", $"Data Source={this.path}");
        builder.UseSetting("Seed:Rows", Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(this.path);
        }
        catch (IOException)
        {
            // A locked temp file is harmless.
        }
    }
}

public class SampleApiTests(SampleApiFactory factory) : IClassFixture<SampleApiFactory>
{
    private static readonly int Tenant1Rows = Enumerable.Range(1, SampleApiFactory.Rows)
        .Count(id => ShipmentDatabase.TenantOf(id, SampleApiFactory.Rows) == 1);

    [Fact]
    public void The_demo_api_refuses_to_start_outside_development()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var start = () => production.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*only runs in the Development environment*");
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_first_page_has_rows_a_total_and_capabilities()
    {
        var page = await this.PostOk(new { pageSize = 25 });

        page["rows"]!.AsArray().Should().HaveCount(25);
        page["totalCount"]!.GetValue<long>().Should().Be(Tenant1Rows);
        page["capabilities"]!["textFilters"]!.GetValue<string>().Should().Be("disabled");
        page["capabilities"]!["threshold"]!.GetValue<int>().Should().Be(50_000);

        var shipped = page["rows"]!.AsArray().Select(r => r!["shippedOn"]!.GetValue<string>()).ToList();
        shipped.Should().BeInDescendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task Filter_and_sort_narrow_and_order_the_rows()
    {
        var page = await this.PostOk(new
        {
            pageSize = 200,
            filters = new { status = new { type = "set", op = "in", values = new[] { "returned" } } },
            sort = new[] { new { field = "weightKg", dir = "asc" } },
        });

        var rows = page["rows"]!.AsArray();
        rows.Should().NotBeEmpty();
        rows.Should().AllSatisfy(r => r!["status"]!.GetValue<string>().Should().Be("returned"));
        var weights = rows.Select(r => r!["weightKg"]?.GetValue<double>()).Where(v => v is not null).Select(v => v!.Value).ToList();
        weights.Should().BeInAscendingOrder();
        page["capabilities"]!["textFilters"]!.GetValue<string>().Should().Be("enabled");
    }

    [Fact]
    public async Task An_invalid_request_is_a_value_free_400()
    {
        var response = await this.Post(new
        {
            pageSize = 1000,
            filters = new Dictionary<string, object> { ["x; DROP TABLE shipment"] = new { type = "text", op = "equals", value = "' OR 1=1 --" } },
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = JsonNode.Parse(body)!;
        problem["type"]!.GetValue<string>().Should().Be("grid-request-invalid");
        problem["errorCodes"]!["pageSize"]![0]!.GetValue<string>().Should().Be("outOfRange");
        problem["errorCodes"]!["(invalid field)"]![0]!.GetValue<string>().Should().Be("unknownField");
        problem["traceId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        body.Should().NotContain("DROP").And.NotContain("1=1");
    }

    [Fact]
    public async Task A_body_the_binder_cannot_read_is_the_same_value_free_400()
    {
        var response = await this.Post(new { pageSize = "abc' OR 1=1" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(body)!;
        problem["type"]!.GetValue<string>().Should().Be("grid-request-invalid");
        problem["errorCodes"]!.AsObject().Select(p => p.Key).Should().Equal("pageSize");
        body.Should().NotContain("1=1");
    }

    [Fact]
    public async Task A_scan_filter_without_a_narrowing_filter_is_refused()
    {
        var response = await this.Post(new
        {
            filters = new { destination = new { type = "text", op = "contains", value = "hardware" } },
        });
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem["type"]!.GetValue<string>().Should().Be("grid-filter-too-broad");
        problem["fields"]!.AsArray().Select(f => f!.GetValue<string>()).Should().Equal("destination");
        problem["threshold"]!.GetValue<int>().Should().Be(50_000);
    }

    [Fact]
    public async Task The_same_scan_filter_is_accepted_after_an_indexed_filter()
    {
        var page = await this.PostOk(new
        {
            filters = new
            {
                destination = new { type = "text", op = "contains", value = "HARDWARE" },
                warehouseCode = new { type = "set", op = "in", values = new[] { "WH-101", "WH-102" } },
            },
        });

        page["rows"]!.AsArray().Should().AllSatisfy(r =>
            r!["destination"]!.GetValue<string>().Should().ContainEquivalentOf("hardware"));
    }

    [Fact]
    public async Task Total_count_is_null_not_zero_when_not_requested()
    {
        var response = await this.Post(new { includeTotal = false, pageSize = 1 });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"totalCount\":null");
    }

    [Fact]
    public async Task Values_lists_the_distinct_values_of_a_set_column()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/shipments/grid/values", new { field = "status", filters = new { } });
        var page = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        page["values"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Equal("cancelled", "delivered", "in_transit", "received", "returned");
        page["truncated"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Each_tenant_sees_only_its_own_rows()
    {
        var tenant1 = await this.PostOk(new { pageSize = 500, sort = new[] { new { field = "shipmentId", dir = "asc" } } });
        var tenant2 = await this.PostOk(new { pageSize = 500, sort = new[] { new { field = "shipmentId", dir = "asc" } } }, tenant: "2");

        var ids1 = Ids(tenant1);
        var ids2 = Ids(tenant2);
        ids1.Should().OnlyContain(id => ShipmentDatabase.TenantOf(id, SampleApiFactory.Rows) == 1);
        ids2.Should().OnlyContain(id => ShipmentDatabase.TenantOf(id, SampleApiFactory.Rows) == 2);
        tenant2["totalCount"]!.GetValue<long>().Should().BeLessThan(tenant1["totalCount"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_restricted_column_is_unknown_and_null_without_the_finance_role()
    {
        var filter = new
        {
            filters = new
            {
                priceTier = new { type = "set", op = "in", values = new[] { "contract" } },
                status = new { type = "set", op = "in", values = new[] { "delivered" } },
            },
        };

        var withoutRole = await this.Post(filter);
        var withoutRoleRows = await this.PostOk(new { pageSize = 50 });
        var withRole = await this.PostOk(filter, finance: true);

        withoutRole.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await withoutRole.Content.ReadAsStringAsync())!["errorCodes"]!["priceTier"]![0]!
            .GetValue<string>().Should().Be("unknownField");
        withoutRoleRows["rows"]!.AsArray().Should().AllSatisfy(r => r!["priceTier"].Should().BeNull());
        withRole["rows"]!.AsArray().Should().NotBeEmpty()
            .And.AllSatisfy(r => r!["priceTier"]!.GetValue<string>().Should().Be("contract"));
    }

    [Fact]
    public async Task A_local_date_filter_uses_the_request_time_zone()
    {
        var page = await this.PostOk(new
        {
            pageSize = 500,
            timeZone = "Europe/Athens",
            filters = new
            {
                shippedOn = new { type = "date", op = "equals", value = "2026-03-29" },
                status = new { type = "set", op = "in", values = new[] { "delivered", "received", "in_transit", "returned", "cancelled" } },
            },
        });

        page["rows"]!.AsArray().Should().AllSatisfy(r =>
        {
            var instant = DateTime.Parse(r!["shippedOn"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal);
            instant.Should().BeOnOrAfter(new DateTime(2026, 3, 28, 22, 0, 0)).And.BeBefore(new DateTime(2026, 3, 29, 21, 0, 0));
        });
    }

    private static List<long> Ids(JsonNode page) =>
        page["rows"]!.AsArray().Select(r => r!["shipmentId"]!.GetValue<long>()).ToList();

    private async Task<HttpResponseMessage> Post(object body, string? tenant = null, bool finance = false)
    {
        var client = factory.CreateClient();
        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add("X-Demo-Tenant", tenant);
        }

        if (finance)
        {
            client.DefaultRequestHeaders.Add("X-Demo-Finance", "true");
        }

        return await client.PostAsJsonAsync("/api/shipments/grid", body);
    }

    private async Task<JsonNode> PostOk(object body, string? tenant = null, bool finance = false)
    {
        var response = await this.Post(body, tenant, finance);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions())!;
    }
}
