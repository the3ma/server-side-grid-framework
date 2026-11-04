namespace GridKit.Tests;

using System.Text.Json;
using GridKit.Manifest;
using SampleApi.Grid;

public class GridManifestTests
{
    private const string UpdateVariable = "GRIDKIT_UPDATE_MANIFEST";

    /// <summary>
    /// The frontend compares its column declaration with this file. Writes it when it is missing or when
    /// GRIDKIT_UPDATE_MANIFEST=1; otherwise the committed file must equal the map.
    /// </summary>
    [Fact]
    public void The_committed_manifest_matches_the_shipments_map()
    {
        var path = Path.Combine(RepoRoot(), "docs", "manifest", "shipments.json");
        var json = GridManifest.From(ShipmentsGrid.Map).ToJson();

        if (!File.Exists(path) || Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(json, $"run the tests with {UpdateVariable}=1 after changing the map, and update the frontend columns");
    }

    [Fact]
    public void The_manifest_is_sql_free_and_flags_restricted_columns()
    {
        var json = GridManifest.From(ShipmentsGrid.Map).ToJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        json.Should().NotContain("g.").And.NotContain("SELECT");
        root.GetProperty("grid").GetString().Should().Be("shipments");
        root.GetProperty("threshold").GetInt32().Should().Be(50_000);
        root.GetProperty("defaultSort")[0].GetProperty("dir").GetString().Should().Be("desc");
        var columns = root.GetProperty("columns").EnumerateArray().ToList();
        columns.Should().HaveCount(11);
        columns.Single(c => c.GetProperty("field").GetString() == "priceTier")
            .GetProperty("restricted").GetBoolean().Should().BeTrue();
        columns.Single(c => c.GetProperty("field").GetString() == "shippedOn")
            .GetProperty("dateSemantics").GetString().Should().Be("local");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GridKit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("GridKit.sln not found above the test output.");
    }
}
