using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Helpers;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>
/// Phase 3 — Users &amp; Roles. All user management goes through here.
/// Passwords are stored only as hashes (PasswordHasher, same as login).
///
/// GUARDS enforced in code (the Users screen also hides itself from
/// Cashier, but these checks hold even if a screen is reached some other way):
/// - nobody can deactivate their OWN account;
/// - the LAST active Admin can never be deactivated or demoted to Cashier —
///   a shop must always keep at least one working Admin login.
/// Guard failures throw InvalidOperationException with a *key* the screen
/// translates: the message text is a localization key, not a sentence,
/// so the warning always appears in the current screen language.
/// </summary>
public class UserService
{
    private readonly DatabaseService _db;
    public UserService(DatabaseService db) => _db = db;

    public List<User> GetAll()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<User>("SELECT * FROM users ORDER BY is_active DESC, username").ToList();
    }

    public User? GetById(long id)
    {
        using var conn = _db.CreateConnection();
        return conn.QueryFirstOrDefault<User>("SELECT * FROM users WHERE id = @id", new { id });
    }

    public bool UsernameExists(string username, long exceptId = 0)
    {
        using var conn = _db.CreateConnection();
        long n = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM users WHERE LOWER(username) = LOWER(@u) AND id <> @exceptId",
            new { u = username.Trim(), exceptId });
        return n > 0;
    }

    private int CountOtherActiveAdmins(long exceptUserId)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM users
              WHERE role = 'Admin' AND is_active = 1 AND id <> @exceptUserId",
            new { exceptUserId });
    }

    /// <summary>Create a user. Returns the new id. Caller validates username/password non-empty.</summary>
    public long CreateUser(string username, string plainPassword, string fullName, string role)
    {
        if (UsernameExists(username))
            throw new InvalidOperationException("UsernameExistsMsg");
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<long>(
            @"INSERT INTO users (username, password_hash, full_name, role)
              VALUES (@u, @h, @n, @r); SELECT last_insert_rowid();",
            new { u = username.Trim(), h = PasswordHasher.Hash(plainPassword), n = fullName.Trim(), r = role });
    }

    /// <summary>Edit full name + role. Demoting the last active Admin is refused.</summary>
    public void UpdateUser(long id, string fullName, string role)
    {
        var user = GetById(id);
        if (user == null) return;
        if (user.IsAdmin && user.IsActive == 1 && role != "Admin" && CountOtherActiveAdmins(id) == 0)
            throw new InvalidOperationException("CannotDeactivateLastAdminMsg");
        using var conn = _db.CreateConnection();
        conn.Execute(
            "UPDATE users SET full_name = @n, role = @r WHERE id = @id",
            new { n = fullName.Trim(), r = role, id });
    }

    /// <summary>Activate / deactivate, with the two guards from the class summary.</summary>
    public void SetActive(long id, bool active, long currentUserId)
    {
        if (!active && id == currentUserId)
            throw new InvalidOperationException("CannotDeactivateYourselfMsg");
        var user = GetById(id);
        if (user == null) return;
        if (!active && user.IsAdmin && user.IsActive == 1 && CountOtherActiveAdmins(id) == 0)
            throw new InvalidOperationException("CannotDeactivateLastAdminMsg");
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE users SET is_active = @a WHERE id = @id",
                     new { a = active ? 1 : 0, id });
    }

    /// <summary>Admin resets any user's password (no old password needed).</summary>
    public void ResetPassword(long id, string newPlainPassword)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE users SET password_hash = @h WHERE id = @id",
                     new { h = PasswordHasher.Hash(newPlainPassword), id });
        RefreshDefaultPasswordFlag();
    }

    /// <summary>
    /// A user changes their OWN password: the current password must verify
    /// first. Returns false when the current password is wrong.
    /// </summary>
    public bool ChangeOwnPassword(long userId, string currentPlainPassword, string newPlainPassword)
    {
        var user = GetById(userId);
        if (user == null) return false;
        if (!PasswordHasher.Verify(currentPlainPassword, user.PasswordHash)) return false;
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE users SET password_hash = @h WHERE id = @id",
                     new { h = PasswordHasher.Hash(newPlainPassword), id = userId });
        RefreshDefaultPasswordFlag();
        return true;
    }

    /// <summary>
    /// Keep the 'admin_default_password' settings flag true to life: it is 1
    /// exactly while the user named 'admin' would log in with 'admin123'.
    /// DatabaseService.ApplyMigrations does the same check at startup; this
    /// keeps the MainWindow banner correct immediately after a change,
    /// without waiting for a restart.
    /// </summary>
    public void RefreshDefaultPasswordFlag()
    {
        using var conn = _db.CreateConnection();
        string? adminHash = conn.QueryFirstOrDefault<string>(
            "SELECT password_hash FROM users WHERE username = 'admin'");
        if (adminHash == null) return;
        string flag = PasswordHasher.Verify("admin123", adminHash) ? "1" : "0";
        conn.Execute(
            @"INSERT INTO settings (key, value) VALUES ('admin_default_password', @flag)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { flag });
    }
}
