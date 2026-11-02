using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Data;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Settings screen. Phase 3 additions:
/// - Scheduled auto-backup fields (on/off, time, keep-last-N) — the actual
///   copying is done by AutoBackupService; these settings only configure it,
///   and the last-backup status line is read from settings here.
/// - "Change MY Password" for the logged-in user — this is how a CASHIER
///   changes their password (they never see the Admin-only Users screen).
/// - Role rule: shop info stays viewable by everyone; the whole backup
///   panel (folder, Backup Now, auto-backup) is hidden for Cashier in the
///   VIEW code-behind — same pattern as ProductsView hiding purchase rates.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly DatabaseService _db;
    private readonly MainViewModel _main;
    private readonly SessionService _session;
    private readonly UserService _users;

    public string[] LanguageOptions { get; } = { "ur", "en" };

    [ObservableProperty] private string _shopNameEn = "";
    [ObservableProperty] private string _shopNameUr = "";
    [ObservableProperty] private string _shopAddress = "";
    [ObservableProperty] private string _shopPhone = "";
    [ObservableProperty] private string _shopWhatsApp = "";
    [ObservableProperty] private bool _taxEnabled;
    [ObservableProperty] private string _taxRateText = "0";
    [ObservableProperty] private string _defaultLanguage = "ur";
    [ObservableProperty] private string _receiptPrinter = "";
    [ObservableProperty] private string _a4Printer = "";
    [ObservableProperty] private string _backupFolder = "";

    // ----- Phase 3: scheduled auto-backup -----
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private string _backupTimeText = "21:00";
    [ObservableProperty] private string _keepBackupsText = "30";
    [ObservableProperty] private string _lastBackupText = "";

    // ----- Phase 3: change my password (PasswordBoxes feed these) -----
    [ObservableProperty] private string _myCurrentPassword = "";
    [ObservableProperty] private string _myNewPassword = "";
    [ObservableProperty] private string _myConfirmPassword = "";

    public bool IsAdmin => _session.IsAdmin;

    public SettingsViewModel(LocalizationService loc, SettingsService settings,
                             DatabaseService db, MainViewModel main,
                             SessionService session, UserService users)
        : base(loc)
    {
        _settings = settings; _db = db; _main = main;
        _session = session; _users = users;
        Load();
    }

    private void Load()
    {
        ShopNameEn = _settings.Get("shop_name_en", "");
        ShopNameUr = _settings.Get("shop_name_ur", "");
        ShopAddress = _settings.Get("shop_address", "");
        ShopPhone = _settings.Get("shop_phone", "");
        ShopWhatsApp = _settings.Get("shop_whatsapp", "");
        TaxEnabled = _settings.TaxEnabled;
        TaxRateText = _settings.Get("tax_rate_percent", "0");
        DefaultLanguage = _settings.Get("default_language", "ur");
        ReceiptPrinter = _settings.Get("receipt_printer", "");
        A4Printer = _settings.Get("a4_printer", "");
        BackupFolder = _settings.Get("backup_folder", "");

        // Phase 3 auto-backup configuration + status.
        AutoBackupEnabled = _settings.Get("backup_auto_enabled", "0") == "1";
        BackupTimeText = _settings.Get("backup_time", "21:00");
        KeepBackupsText = _settings.Get("backup_keep_count", "30");
        string lastAt = _settings.Get("last_backup_at", "");
        LastBackupText = string.IsNullOrWhiteSpace(lastAt)
            ? S("NeverBackup")
            : DateHelper.FormatDateTime(DateHelper.ParseDb(lastAt));
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.SetMany(new Dictionary<string, string>
        {
            ["shop_name_en"] = ShopNameEn,
            ["shop_name_ur"] = ShopNameUr,
            ["shop_address"] = ShopAddress,
            ["shop_phone"] = ShopPhone,
            ["shop_whatsapp"] = ShopWhatsApp,
            ["tax_enabled"] = TaxEnabled ? "1" : "0",
            ["tax_rate_percent"] = TaxRateText,
            ["default_language"] = DefaultLanguage,
            ["receipt_printer"] = ReceiptPrinter,
            ["a4_printer"] = A4Printer,
            ["backup_folder"] = BackupFolder,
            ["backup_auto_enabled"] = AutoBackupEnabled ? "1" : "0",
            ["backup_time"] = BackupTimeText,
            ["backup_keep_count"] = KeepBackupsText
        });
        _main.RefreshShopNamePublic();
        Info(S("SettingsSavedMsg"));
    }

    /// <summary>
    /// Change the LOGGED-IN user's password (current password verified in
    /// UserService). Same flow as the Users screen's section, kept here so
    /// a Cashier has somewhere to do it. PasswordBoxes in the view copy
    /// into the My* properties (the Login screen pattern).
    /// </summary>
    [RelayCommand]
    private void ChangeMyPassword()
    {
        var me = _session.CurrentUser;
        if (me == null) return;
        if (MyNewPassword.Length < 4) { Warn(S("PasswordTooShortMsg")); return; }
        if (MyNewPassword != MyConfirmPassword) { Warn(S("PasswordMismatchMsg")); return; }
        if (!_users.ChangeOwnPassword(me.Id, MyCurrentPassword, MyNewPassword))
        { Warn(S("WrongCurrentPasswordMsg")); return; }
        MyCurrentPassword = ""; MyNewPassword = ""; MyConfirmPassword = "";
        Info(S("PasswordChangedMsg"));
        _main.RefreshPasswordWarningPublic();
    }

    [RelayCommand]
    private void BackupNow()
    {
        if (string.IsNullOrWhiteSpace(BackupFolder))
        {
            Warn(S("BackupFolder"));
            return;
        }
        try
        {
            string dest = _db.BackupTo(BackupFolder);
            // Phase 3: stamp the backup status so the Dashboard/Settings
            // "Last Backup" line covers manual backups too — and so the
            // auto-backup timer knows today is already covered.
            var now = DateTime.Now;
            _settings.SetMany(new Dictionary<string, string>
            {
                ["last_backup_date"] = now.ToString("yyyy-MM-dd"),
                ["last_backup_at"] = now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            LastBackupText = DateHelper.FormatDateTime(now);
            Info($"{S("BackupDoneMsg")}\n{dest}");
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }
}
