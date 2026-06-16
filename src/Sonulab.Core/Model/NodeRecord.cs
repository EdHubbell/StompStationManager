using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Sonulab.Core.Model;

public sealed class NodeRecord
{
    public string Path { get; }
    public JsonElement Json { get; }

    private NodeRecord(string path, JsonElement json) { Path = path; Json = json; }

    public static bool TryParse(string line, [NotNullWhen(true)] out NodeRecord? record)
    {
        record = null;
        int sep = line.IndexOf(":{", StringComparison.Ordinal);
        if (sep <= 0) return false;
        var path = line[..sep];
        var jsonText = line[(sep + 1)..];
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            record = new NodeRecord(path, doc.RootElement.Clone());
            return true;
        }
        catch (JsonException) { return false; }
    }

    public string? ValueString =>
        Json.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public double? ValueNumber =>
        Json.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
