using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly SaleService _sales;
    private readonly CustomerService _customers;
    private readonly ProductService _products;
    private readonly SessionService _session;
    private readonly SettingsService _settings;

    [ObservableProperty] private string _todaysSalesText = "Rs 0";
    [ObservableProperty] private int _todaysBills;
    [ObservableProperty] private string _todaysProfitText = "Rs 0";
    [ObservableProperty] private string _totalReceivableText = "Rs 0";
    [ObservableProperty] private int _lowStockCount;
    [ObservableProperty] private string _backupStatusText = "";

    /// <summary>Profit card is only visible to Admin (see DashboardView XAML binding).</summary>
    public bool ShowProfit => _session.IsAdmin;
    public bool HasBackupStatus => !string.IsNullOrEmpty(BackupStatusText);

    public DashboardViewModel(LocalizationService loc, SaleService sales, CustomerService customers,
                              ProductService products, SessionService session, SettingsService settings)
        : base(loc)
    {
        _sales = sales; _customers = customers; _products = products; _session = session;
        _settings = settings;
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        var (total, count, profit) = _sales.GetTodayStats();
        TodaysSalesText = MoneyFormatter.Format(total);
        TodaysBills = count;
        TodaysProfitText = MoneyFormatter.Format(profit);
        TotalReceivableText = MoneyFormatter.Format(_customers.GetTotalReceivable());
        LowStockCount = _products.GetLowStock().Count;

        // Phase 3: backup status line — last successful backup (manual or
        // automatic) + the folder it went to. Empty until the first backup.
        string lastAt = _settings.Get("last_backup_at", "");
        BackupStatusText = string.IsNullOrWhiteSpace(lastAt)
            ? ""
            : $"{S("LastBackup")}: {DateHelper.FormatDateTime(DateHelper.ParseDb(lastAt))}  •  {_settings.Get("backup_folder", "")}";
        OnPropertyChanged(nameof(HasBackupStatus));
    }
}
