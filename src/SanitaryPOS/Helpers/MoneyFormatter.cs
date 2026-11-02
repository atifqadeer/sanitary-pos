using System.Globalization;
using System.Text;

namespace SanitaryPOS.Helpers;

/// <summary>
/// Money is stored everywhere as INTEGER PAISA (Rs 1 = 100 paisa) — see schema.sql.
/// This helper formats paisa with Pakistani / Indian digit grouping
/// (lakh / crore style): 12345600 paisa -> "Rs 1,23,456".
/// </summary>
public static class MoneyFormatter
{
    /// <summary>12345600 -> "Rs 1,23,456"  |  -50000 -> "-Rs 500"</summary>
    public static string Format(long paisa)
    {
        bool negative = paisa < 0;
        long abs = Math.Abs(paisa);
        long rupees = abs / 100;
        long rem = abs % 100;

        string core = GroupPakistani(rupees);
        if (rem != 0) core += "." + rem.ToString("00");
        return (negative ? "-Rs " : "Rs ") + core;
    }

    /// <summary>Same grouping without the "Rs" prefix (for grid columns whose header says Rs).</summary>
    public static string FormatNumber(long paisa)
    {
        bool negative = paisa < 0;
        long abs = Math.Abs(paisa);
        long rupees = abs / 100;
        long rem = abs % 100;
        string core = GroupPakistani(rupees);
        if (rem != 0) core += "." + rem.ToString("00");
        return (negative ? "-" : "") + core;
    }

    /// <summary>Group digits Pakistani style: last 3, then pairs. 1234567 -> "12,34,567".</summary>
    public static string GroupPakistani(long value)
    {
        string s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Length <= 3) return s;

        var sb = new StringBuilder();
        string head = s[..^3];          // everything before last 3 digits
        string tail = s[^3..];          // last 3 digits
        // Insert a comma every 2 digits from the right in the head part
        int firstGroup = head.Length % 2;
        for (int i = 0; i < head.Length; i++)
        {
            if (i > 0 && ((i - firstGroup) % 2 == 0)) sb.Append(',');
            sb.Append(head[i]);
        }
        if (sb.Length > 0) sb.Append(',');
        sb.Append(tail);
        return sb.ToString();
    }

    /// <summary>Parse what the user typed in a Rupees box ("1,250" or "1250.50") into paisa.</summary>
    public static bool TryParseRupees(string? text, out long paisa)
    {
        paisa = 0;
        if (string.IsNullOrWhiteSpace(text)) return true; // empty box = 0
        string cleaned = text.Replace(",", "").Replace("Rs", "").Trim();
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rupees))
            return false;
        paisa = (long)Math.Round(rupees * 100m);
        return true;
    }

    public static long ParseRupeesOrZero(string? text)
        => TryParseRupees(text, out long p) ? p : 0;

    /// <summary>Paisa -> plain rupees text for an edit box, e.g. 125050 -> "1250.50".</summary>
    public static string ToRupeesText(long paisa)
    {
        decimal rupees = paisa / 100m;
        return rupees == Math.Truncate(rupees)
            ? ((long)rupees).ToString(CultureInfo.InvariantCulture)
            : rupees.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
