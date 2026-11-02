using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;

namespace SanitaryPOS.Helpers;

/// <summary>Date display is always DD-MM-YYYY (Pakistan). Stored in DB as ISO text.</summary>
public static class DateHelper
{
    public static string FormatDate(DateTime dt) => dt.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string FormatDateTime(DateTime dt) => dt.ToString("dd-MM-yyyy hh:mm tt", CultureInfo.InvariantCulture);

    /// <summary>DB stores 'yyyy-MM-dd HH:mm:ss'. Parse safely for display.</summary>
    public static DateTime ParseDb(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;
        if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out DateTime dt)) return dt;
        return DateTime.TryParse(text, out dt) ? dt : DateTime.MinValue;
    }

    public static string FormatDbDate(string? dbText) => FormatDate(ParseDb(dbText));
}

/// <summary>
/// Simple SHA-256 password hashing for Phase 1 (login for a single shop PC).
/// Phase 3 hardening note: move to a salted slow hash (e.g. BCrypt) — see PHASE1.md.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes("SanitaryPOS::" + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool Verify(string password, string storedHash)
        => string.Equals(Hash(password), storedHash, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Grid converter: paisa (long) -> "Rs 1,23,456".</summary>
public class MoneyValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long p ? MoneyFormatter.Format(p) : "Rs 0";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Grid converter: DB date text -> DD-MM-YYYY.</summary>
public class DateDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? DateHelper.FormatDbDate(s) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
