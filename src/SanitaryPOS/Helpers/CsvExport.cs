using System.IO;
using System.Text;

namespace SanitaryPOS.Helpers;

/// <summary>
/// Phase 2 export: plain CSV that opens directly in Excel (the Reports screen
/// labels it honestly as "Export Excel (CSV)" — no heavy Excel library).
/// Written as UTF-8 WITH BOM so Excel shows Urdu text correctly, and every
/// field is quoted/escaped by the CSV rules, so commas inside names are safe.
/// </summary>
public static class CsvExport
{
    public static void WriteCsv(string path, IReadOnlyList<string> headers,
                                IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));

        // Encoding.UTF8 in .NET writes the BOM by default with StreamWriter —
        // that BOM is exactly what makes Excel detect Urdu/Arabic script.
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Escape(string? field)
    {
        string f = field ?? "";
        // Quote when the field has a comma, a quote or a newline.
        if (f.Contains(',') || f.Contains('"') || f.Contains('\n') || f.Contains('\r'))
            return "\"" + f.Replace("\"", "\"\"") + "\"";
        return f;
    }
}
