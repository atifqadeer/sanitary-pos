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
    {
        Directory.CreateDirectory(folder);
        string dest = Path.Combine(folder,
            $"sanitarypos-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.db");
        File.Copy(DbPath, dest, overwrite: true);
        return dest;
    }
}
