using System.IO;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using SanitaryPOS.Helpers;
using SanitaryPOS.Models;

namespace SanitaryPOS.Data;

/// <summary>
/// Owns the SQLite database file: %AppData%\SanitaryPOS\sanitarypos.db
/// On first run it creates the file from the embedded schema.sql (the tested
/// schema in the repo at database/schema.sql) and seeds ONE admin user:
///     username: admin    password: admin123   <-- MUST be changed before live use.
/// </summary>
public class DatabaseService
{
    static DatabaseService()
    {
        // products.purchase_rate_paisa  ->  Product.PurchaseRatePaisa, etc.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public string DbFolder { get; }
    public string DbPath { get; }
    public string ConnectionString { get; }

    public DatabaseService()
    {
        DbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SanitaryPOS");
        DbPath = Path.Combine(DbFolder, "sanitarypos.db");
        ConnectionString = $"Data Source={DbPath}";
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        conn.Execute("PRAGMA foreign_keys = ON;");
        return conn;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(DbFolder);

        bool needsSchema = true;
        if (File.Exists(DbPath))
        {
            using var check = CreateConnection();
            long tables = check.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users'");
            needsSchema = tables == 0;
        }

        if (needsSchema)
        {
            string sql = ReadEmbeddedSchema();
            using var conn = CreateConnection();
            conn.Execute(sql); // schema.sql includes seed units/categories/settings
        }

        SeedAdminIfNeeded();
        ApplyMigrations();
    }

    /// <summary>
    /// Phase 3 migrations — run on EVERY startup, safe to run any number of
    /// times. This is how databases created by the Phase 1/2 builds gain the
    /// new table + settings keys without losing a single bill:
    /// - day_closings table (CREATE TABLE IF NOT EXISTS — also in schema.sql
    ///   for brand-new databases, same statement).
    /// - Phase 3 settings keys with defaults (INSERT OR IGNORE — a value the
    ///   shop already set is never overwritten).
    /// - 'admin_default_password' flag: recomputed honestly — it is 1 exactly
    ///   while the user named 'admin' still verifies against 'admin123'.
    ///   The MainWindow warning banner reads this flag. Changing the admin
    ///   password (Users/Settings screen) updates the stored hash, so the
    ///   next startup — and UserService.RefreshDefaultPasswordFlag right at
    ///   change time — clears the flag.
    /// </summary>
    private void ApplyMigrations()
    {
        using var conn = CreateConnection();

        conn.Execute(
            @"CREATE TABLE IF NOT EXISTS day_closings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                closing_date TEXT NOT NULL UNIQUE,
                expected_paisa INTEGER NOT NULL DEFAULT 0,
                counted_paisa INTEGER NOT NULL DEFAULT 0,
                difference_paisa INTEGER NOT NULL DEFAULT 0,
                note TEXT,
                user_id INTEGER REFERENCES users(id),
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
              )");

        var defaults = new (string Key, string Value)[]
        {
            ("admin_default_password", "0"),
            ("backup_auto_enabled", "0"),
            ("backup_time", "21:00"),
            ("backup_keep_count", "30"),
            ("last_backup_date", ""),
            ("last_backup_at", "")
        };
        foreach (var (key, value) in defaults)
        {
            conn.Execute(
                "INSERT OR IGNORE INTO settings (key, value) VALUES (@key, @value)",
                new { key, value });
        }

        // Recompute the default-password flag from the real hash.
        string? adminHash = conn.QueryFirstOrDefault<string>(
            "SELECT password_hash FROM users WHERE username = 'admin'");
        if (adminHash != null)
        {
            string flag = PasswordHasher.Verify("admin123", adminHash) ? "1" : "0";
            conn.Execute(
                @"INSERT INTO settings (key, value) VALUES ('admin_default_password', @flag)
                  ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                new { flag });
        }
    }

    private static string ReadEmbeddedSchema()
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream("SanitaryPOS.schema.sql");
        if (stream == null)
            throw new InvalidOperationException(
                "Embedded schema.sql not found. Check the EmbeddedResource item in SanitaryPOS.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void SeedAdminIfNeeded()
    {
        using var conn = CreateConnection();
        long users = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users");
        if (users > 0) return;

        conn.Execute(
            "INSERT INTO users (username, password_hash, full_name, role) VALUES (@u, @h, @n, 'Admin')",
            new { u = "admin", h = PasswordHasher.Hash("admin123"), n = "Shop Owner" });

        // The seeded admin starts life on the public default password —
        // flag it so the shell shows the change-password warning banner.
        conn.Execute(
            @"INSERT INTO settings (key, value) VALUES ('admin_default_password', '1')
              ON CONFLICT(key) DO UPDATE SET value = excluded.value");
    }

    /// <summary>Login check used by the Login screen.</summary>
    public User? Authenticate(string username, string password)
    {
        using var conn = CreateConnection();
        var user = conn.QueryFirstOrDefault<User>(
            "SELECT * FROM users WHERE username = @u AND is_active = 1", new { u = username.Trim() });
        if (user == null) return null;
        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    /// <summary>One-click backup: copy the .db file to the given folder with a dated name.</summary>
    public string BackupTo(string folder)
        => BackupCopyTo(folder, $"sanitarypos-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.db");

    /// <summary>
    /// Phase 3: the one file-copy used by every backup path (manual Backup
    /// Now, scheduled auto-backup, exit catch-up) — callers only choose the
    /// file name. Auto-backups are named sanitarypos-yyyy-MM-dd-HHmm.db.
    /// Note: SQLite is in rollback-journal mode here (no WAL files are
    /// created by this app), so copying the single .db file is a complete
    /// backup as long as no write transaction is mid-flight — backups run
    /// on the UI thread between operations, never inside a save.
    /// </summary>
    public string BackupCopyTo(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        string dest = Path.Combine(folder, fileName);
        File.Copy(DbPath, dest, overwrite: true);
        return dest;
    }
}
