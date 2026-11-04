namespace GridKit.Manifest;

using System.Text;
using System.Text.Json;
using GridKit.Columns;
using GridKit.Contract;

/// <summary>
/// A SQL-free description of a column map. The backend writes it to a file; the frontend test compares its own
/// column declaration with it, so the two sides cannot drift apart unnoticed.
/// </summary>
public sealed class GridManifest
{
    private readonly GridColumnMap map;

    private GridManifest(GridColumnMap map)
    {
        this.map = map;
    }

    public static GridManifest From(GridColumnMap map) => new(map ?? throw new ArgumentNullException(nameof(map)));

    /// <summary>Indented JSON with <c>\n</c> line endings, so the file is identical on every OS.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("grid", this.map.Name);
            if (this.map.GuardThreshold is { } threshold)
            {
                writer.WriteNumber("threshold", threshold);
            }
            else
            {
                writer.WriteNull("threshold");
            }

            writer.WriteStartArray("defaultSort");
            foreach (var (column, descending) in this.map.DefaultSort)
            {
                writer.WriteStartObject();
                writer.WriteString("field", column.Field);
                writer.WriteString("dir", descending ? "desc" : "asc");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("columns");
            foreach (var column in this.map.Columns)
            {
                writer.WriteStartObject();
                writer.WriteString("field", column.Field);
                writer.WriteString("type", GridWire.Name(column.Type));
                writer.WriteBoolean("sortable", column.Sortable);
                writer.WriteBoolean("filterable", column.Filterable);
                writer.WriteBoolean("searchable", column.Searchable);
                writer.WriteBoolean("indexed", column.Indexed);
                writer.WriteBoolean("restricted", column.Restricted);
                writer.WriteStartArray("ops");
                foreach (var op in column.Operators)
                {
                    writer.WriteStringValue(GridWire.Name(op));
                }

                writer.WriteEndArray();
                if (column.DateSemantics is { } semantics)
                {
                    writer.WriteString("dateSemantics", GridWire.Name(semantics));
                }
                else
                {
                    writer.WriteNull("dateSemantics");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
