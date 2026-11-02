using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Data;
using SanitaryPOS.Localization;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly DatabaseService _db;
    private readonly MainViewModel _main;

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

    public SettingsViewModel(LocalizationService loc, SettingsService settings,
                             DatabaseService db, MainViewModel main)
        : base(loc)
    {
        _settings = settings; _db = db; _main = main;
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
            ["backup_folder"] = BackupFolder
        });
        _main.RefreshShopNamePublic();
        Info(S("SettingsSavedMsg"));
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
            Info($"{S("BackupDoneMsg")}\n{dest}");
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }
}
