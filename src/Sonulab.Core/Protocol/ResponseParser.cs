using System.Text.RegularExpressions;

namespace Sonulab.Core.Protocol;

public static class ResponseParser
{
    public static IEnumerable<string> Records(string raw) =>
        raw.Replace(" ", "").Split('\n')
           .Select(l => l.TrimEnd('\r'))
           .Where(l => l.Length > 0);

    public static bool IsMeter(string record) =>
        record.Contains(@"root\sys\_meters\") || record.Contains(@"root\usb\_status");

    public static IEnumerable<string> NonMeterRecords(string raw) =>
        Records(raw).Where(r => !IsMeter(r));

    public static string? ChunkHex(string raw, int chunk)
    {
        var rx = new Regex("\"chunk\":" + chunk + @"\b.*?""value"":""([0-9a-fA-F]*)""");
        foreach (var rec in Records(raw))
        {
            var m = rx.Match(rec);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
