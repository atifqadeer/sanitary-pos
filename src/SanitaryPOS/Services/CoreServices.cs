using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>Who is logged in right now (Admin / Cashier). Set at login, used to hide profit &amp; purchase rates.</summary>
public class SessionService
{
    public User? CurrentUser { get; private set; }
    public bool IsAdmin => CurrentUser?.IsAdmin == true;
    public void SignIn(User user) => CurrentUser = user;
    public void SignOut() => CurrentUser = null;
}

/// <summary>Reads/writes the settings key-value table (shop info, tax, language, printers).</summary>
public class SettingsService
{
    private readonly DatabaseService _db;
    public SettingsService(DatabaseService db) => _db = db;

    public Dictionary<string, string> GetAll()
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query("SELECT key, value FROM settings");
        var dict = new Dictionary<string, string>();
        foreach (var r in rows) dict[(string)r.key] = (string?)r.value ?? "";
        return dict;
    }

    public string Get(string key, string fallback = "")
    {
        using var conn = _db.CreateConnection();
        string? v = conn.QueryFirstOrDefault<string>(
            "SELECT value FROM settings WHERE key = @key", new { key });
        return v ?? fallback;
    }

    public void Set(string key, string value)
    {
        using var conn = _db.CreateConnection();
        conn.Execute(
            @"INSERT INTO settings (key, value) VALUES (@key, @value)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { key, value });
    }

    public void SetMany(IDictionary<string, string> values)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        foreach (var kv in values)
        {
            conn.Execute(
                @"INSERT INTO settings (key, value) VALUES (@key, @value)
                  ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                new { key = kv.Key, value = kv.Value }, tx);
        }
        tx.Commit();
    }

    public bool TaxEnabled => Get("tax_enabled", "0") == "1";

    public decimal TaxRatePercent =>
        decimal.TryParse(Get("tax_rate_percent", "0"), out decimal r) ? r : 0m;
}

/// <summary>
/// The ONLY place stock numbers change. Every change writes a stock_movements
/// row AND updates the stock table, inside the caller's transaction when given.
/// Rule from PLAN.md: stock is never edited directly anywhere else.
/// </summary>
public class StockService
{
    private readonly DatabaseService _db;
    public StockService(DatabaseService db) => _db = db;

    public void ApplyMovement(Microsoft.Data.Sqlite.SqliteConnection conn,
                              Microsoft.Data.Sqlite.SqliteTransaction? tx,
                              long productId, string movementType, double qty,
                              string? referenceType, long? referenceId,
                              long? userId, string? note = null)
    {
        conn.Execute(
            @"INSERT INTO stock_movements (product_id, movement_type, qty, reference_type, reference_id, note, user_id)
              VALUES (@productId, @movementType, @qty, @referenceType, @referenceId, @note, @userId)",
            new { productId, movementType, qty, referenceType, referenceId, note, userId }, tx);

        // Damaged returns go to damaged_qty, everything else to sellable qty.
        if (movementType == "Damage")
        {
            conn.Execute(
                @"INSERT INTO stock (product_id, qty, damaged_qty) VALUES (@productId, 0, @qty)
                  ON CONFLICT(product_id) DO UPDATE SET damaged_qty = damaged_qty + @qty",
                new { productId, qty }, tx);
        }
        else
        {
            conn.Execute(
                @"INSERT INTO stock (product_id, qty) VALUES (@productId, @qty)
                  ON CONFLICT(product_id) DO UPDATE SET qty = qty + @qty",
                new { productId, qty }, tx);
        }
    }

    public double GetQty(long productId)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<double>(
            "SELECT COALESCE((SELECT qty FROM stock WHERE product_id = @id), 0)", new { id = productId });
    }
}
