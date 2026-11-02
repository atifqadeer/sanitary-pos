using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using SanitaryPOS.Data;

namespace SanitaryPOS.Services;

/// <summary>
/// Phase 3 — scheduled daily auto-backup, deliberately small.
///
/// Behaviour (also in PHASE3.md):
/// - A DispatcherTimer ticks every minute while the app is open.
/// - If auto-backup is ON (settings: backup_auto_enabled = 1), a backup
///   folder is set, the clock has reached backup_time, and no backup has
///   been done today (last_backup_date), it copies the database via
///   DatabaseService.BackupCopyTo with the name sanitarypos-yyyy-MM-dd-HHmm.db
///   and records last_backup_date / last_backup_at in settings.
/// - The same check runs once at startup, so if the PC was OFF at the set
///   time, the backup happens when the shop opens the app next (catch-up).
/// - On app exit, RunExitCatchUp does the same "not done today" backup —
///   this REPLACES any separate on-close backup (Phase 1 only ever had the
///   manual Backup Now button), so a day can never get two scheduled copies.
/// - After each auto-backup, old auto-backups beyond backup_keep_count
///   (default 30) are deleted — ONLY files matching the auto-backup name
///   pattern sanitarypos-yyyy-MM-dd-HHmm.db. Manual backups
///   (sanitarypos-backup-...) and everything else in the folder are
///   never touched.
/// - Every failure is swallowed on purpose: a backup problem must never
///   crash or block the billing counter. last_backup_at only moves on
///   success, so the Dashboard/Settings status line tells the truth.
/// </summary>
public class AutoBackupService
{
    private static readonly Regex AutoBackupName =
        new(@"^sanitarypos-\d{4}-\d{2}-\d{2}-\d{4}\.db$");

    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;

    public AutoBackupService(DatabaseService db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => RunScheduledCheck();
    }

    /// <summary>Start the minute timer + one immediate catch-up check.</summary>
    public void Start()
    {
        RunScheduledCheck();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void RunScheduledCheck()
    {
        try
        {
            if (!TryGetFolder(out string folder)) return;
            if (!TimeSpan.TryParse(_settings.Get("backup_time", "21:00"),
                                   CultureInfo.InvariantCulture, out TimeSpan at))
                at = new TimeSpan(21, 0, 0);
            if (DateTime.Now.TimeOfDay < at) return;
            if (AlreadyDoneToday()) return;
            DoBackup(folder);
        }
        catch
        {
            // Deliberately silent — see class summary. A failed copy simply
            // leaves last_backup_date unchanged, so the next tick retries.
        }
    }

    /// <summary>Called from App.OnExit: one catch-up copy if today has none.</summary>
    public void RunExitCatchUp()
    {
        try
        {
            if (!TryGetFolder(out string folder)) return;
            if (AlreadyDoneToday()) return;
            DoBackup(folder);
        }
        catch
        {
            // Silent on exit too — the app must always close cleanly.
        }
    }

    private bool TryGetFolder(out string folder)
    {
        folder = _settings.Get("backup_folder", "");
        return _settings.Get("backup_auto_enabled", "0") == "1"
               && !string.IsNullOrWhiteSpace(folder);
    }

    private bool AlreadyDoneToday()
        => _settings.Get("last_backup_date", "")
           == DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void DoBackup(string folder)
    {
        var now = DateTime.Now;
        string name = $"sanitarypos-{now:yyyy-MM-dd-HHmm}.db";
        _db.BackupCopyTo(folder, name);
        _settings.SetMany(new Dictionary<string, string>
        {
            ["last_backup_date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["last_backup_at"] = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        });
        Prune(folder);
    }

    private void Prune(string folder)
    {
        int keep = int.TryParse(_settings.Get("backup_keep_count", "30"), out int k) && k > 0
            ? k : 30;
        var files = Directory.GetFiles(folder)
            .Select(Path.GetFileName)
            .Where(f => f != null && AutoBackupName.IsMatch(f))
            .OrderByDescending(f => f) // yyyy-MM-dd-HHmm sorts correctly as text
            .ToList();
        foreach (string? f in files.Skip(keep))
        {
            try { File.Delete(Path.Combine(folder, f!)); }
            catch { /* a locked file just stays for the next round */ }
        }
    }
}
