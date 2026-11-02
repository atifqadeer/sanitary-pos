using System.Collections.ObjectModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Printing;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Reports screen: pick a report + a date range → one generic grid shows it.
/// Print = A4 FlowDocument (choose "Microsoft Print to PDF" in the dialog for
/// a PDF). Export = CSV via SaveFileDialog — the button is honestly labelled
/// "Export Excel (CSV)"; Excel opens CSV files directly.
///
/// The Profit report is ADMIN ONLY, following the Phase 1 pattern: for a
/// Cashier login the report is not even offered in the picker, and RunReport
/// double-checks the role before running it.
/// </summary>
public partial class ReportsViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly CustomerService _customers;
    private readonly SupplierService _suppliers;
    private readonly SettingsService _settings;
    private readonly SessionService _session;

    public ObservableCollection<string> ReportTypes { get; } = new();
    public ObservableCollection<ReportLine> Rows { get; } = new();

    [ObservableProperty] private string _selectedReport = "Sales";
    [ObservableProperty] private DateTime _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime _toDate = DateTime.Today;

    // Grid headings + title change per report (set by RunReport from the
    // localized strings, so they follow the current screen language).
    [ObservableProperty] private string _reportTitle = "";
    [ObservableProperty] private string _header1 = "";
    [ObservableProperty] private string _header2 = "";
    [ObservableProperty] private string _header3 = "";
    [ObservableProperty] private string _header4 = "";
    [ObservableProperty] private string _header5 = "";
    [ObservableProperty] private string _totalText = "";

    public bool IsAdmin => _session.IsAdmin;

    private bool _hasRun;

    public ReportsViewModel(LocalizationService loc, ReportService reports,
                            CustomerService customers, SupplierService suppliers,
                            SettingsService settings, SessionService session)
        : base(loc)
    {
        _reports = reports; _customers = customers; _suppliers = suppliers;
        _settings = settings; _session = session;

        ReportTypes.Add("Sales");
        if (IsAdmin) ReportTypes.Add("Profit");   // cashier never sees the profit report option
        ReportTypes.Add("Brand");
        ReportTypes.Add("Category");
        ReportTypes.Add("Receivable");
        ReportTypes.Add("Payable");
        ReportTypes.Add("StockValue");
        SelectedReport = "Sales";
    }

    // ---------- date presets ----------
    [RelayCommand] private void PresetToday() { FromDate = DateTime.Today; ToDate = DateTime.Today; }

    [RelayCommand]
    private void PresetWeek()
    {
        // Week starts Monday (Pakistan working week convention used by shops here).
        int diff = (7 + (DateTime.Today.DayOfWeek - DayOfWeek.Monday)) % 7;
        FromDate = DateTime.Today.AddDays(-diff);
        ToDate = DateTime.Today;
    }

    [RelayCommand]
    private void PresetMonth()
    {
        FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate = DateTime.Today;
    }

    private string FromDb => FromDate.ToString("yyyy-MM-dd");
    private string ToDb => ToDate.ToString("yyyy-MM-dd");
    private string RangeText => $"{DateHelper.FormatDate(FromDate)} — {DateHelper.FormatDate(ToDate)}";

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        if (_hasRun) RunReport(); // re-run so headings follow the new language
    }

    // ---------- run ----------
    [RelayCommand]
    private void RunReport()
    {
        if (SelectedReport == "Profit" && !IsAdmin)
        {
            Warn(S("ProfitAdminOnlyMsg"));
            return;
        }

        Rows.Clear();
        TotalText = "";

        switch (SelectedReport)
        {
            case "Profit": RunProfit(); break;
            case "Brand": RunGroup(_reports.SalesByBrand(FromDb, ToDb), S("ReportBrand")); break;
            case "Category": RunGroup(_reports.SalesByCategory(FromDb, ToDb), S("ReportCategory")); break;
            case "Receivable": RunReceivable(); break;
            case "Payable": RunPayable(); break;
            case "StockValue": RunStockValue(); break;
            default: RunSales(); break;
        }
        _hasRun = true;
    }

    private void SetHeaders(string title, string h1, string h2, string h3, string h4 = "", string h5 = "")
    {
        ReportTitle = title;
        Header1 = h1; Header2 = h2; Header3 = h3; Header4 = h4; Header5 = h5;
    }

    private void RunSales()
    {
        SetHeaders(S("ReportSales"), S("Date"), S("Bills"), S("Amount"), S("PaymentMethod"), S("Amount"));

        var byDate = _reports.SalesByDate(FromDb, ToDb);
        long total = 0; int bills = 0;
        foreach (var r in byDate)
        {
            Rows.Add(new ReportLine
            {
                C1 = DateHelper.FormatDbDate(r.DateDb),
                C2 = r.BillCount.ToString(),
                C3 = MoneyFormatter.Format(r.TotalPaisa)
            });
            total += r.TotalPaisa; bills += r.BillCount;
        }

        // Second block on the same grid: the same sales split by payment method.
        var byMethod = _reports.SalesByPaymentMethod(FromDb, ToDb);
        if (byMethod.Count > 0)
        {
            Rows.Add(new ReportLine { C1 = "—" });
            foreach (var m in byMethod)
            {
                Rows.Add(new ReportLine
                {
                    C1 = "",
                    C2 = "",
                    C3 = "",
                    C4 = m.PaymentMethod,
                    C5 = MoneyFormatter.Format(m.TotalPaisa)
                });
            }
        }
        TotalText = $"{S("Total")}: {MoneyFormatter.Format(total)}  ({bills} {S("Bills")})";
    }

    private void RunProfit()
    {
        SetHeaders(S("ReportProfit"), S("Date"), S("SalesWord"), S("CostWord"), S("EstimatedProfit"));
        var rows = _reports.ProfitByDate(FromDb, ToDb);
        long sales = 0, cost = 0;
        foreach (var r in rows)
        {
            Rows.Add(new ReportLine
            {
                C1 = DateHelper.FormatDbDate(r.DateDb),
                C2 = MoneyFormatter.Format(r.SalesPaisa),
                C3 = MoneyFormatter.Format(r.CostPaisa),
                C4 = MoneyFormatter.Format(r.ProfitPaisa)
            });
            sales += r.SalesPaisa; cost += r.CostPaisa;
        }
        TotalText = $"{S("EstimatedProfit")}: {MoneyFormatter.Format(sales - cost)}";
    }

    private void RunGroup(List<GroupSalesRow> data, string title)
    {
        SetHeaders(title, S("GroupName"), S("Qty"), S("Amount"));
        long total = 0;
        foreach (var r in data)
        {
            Rows.Add(new ReportLine
            {
                C1 = r.GroupName,
                C2 = r.Qty.ToString("0.##"),
                C3 = MoneyFormatter.Format(r.AmountPaisa)
            });
            total += r.AmountPaisa;
        }
        TotalText = $"{S("Total")}: {MoneyFormatter.Format(total)}";
    }

    private void RunReceivable()
    {
        SetHeaders(S("ReportReceivable"), S("CustomerName"), S("Mobile"), S("CurrentBalance"),
                   S("CreditLimitShort"), S("OverLimit"));
        long total = 0;
        foreach (var c in _customers.GetAll().Where(c => c.BalancePaisa > 0)
                                            .OrderByDescending(c => c.BalancePaisa))
        {
            Rows.Add(new ReportLine
            {
                C1 = c.Name,
                C2 = c.Mobile ?? "",
                C3 = MoneyFormatter.Format(c.BalancePaisa),
                C4 = c.CreditLimitPaisa > 0 ? MoneyFormatter.Format(c.CreditLimitPaisa) : "-",
                C5 = c.IsOverCreditLimit ? S("YesWord") : ""
            });
            total += c.BalancePaisa;
        }
        TotalText = $"{S("TotalUdhaarReceivable")}: {MoneyFormatter.Format(total)}";
    }

    private void RunPayable()
    {
        SetHeaders(S("ReportPayable"), S("Supplier"), S("Mobile"), S("PayableWord"));
        long total = 0;
        foreach (var s in _suppliers.GetAll().Where(s => s.BalancePaisa > 0)
                                             .OrderByDescending(s => s.BalancePaisa))
        {
            Rows.Add(new ReportLine
            {
                C1 = s.Name,
                C2 = s.Mobile ?? "",
                C3 = MoneyFormatter.Format(s.BalancePaisa)
            });
            total += s.BalancePaisa;
        }
        TotalText = $"{S("TotalPayable")}: {MoneyFormatter.Format(total)}";
    }

    private void RunStockValue()
    {
        SetHeaders(S("ReportStockValue"), S("Name"), S("Brand"), S("Qty"),
                   S("StockPurchaseValue"), S("StockSaleValue"));
        long purchaseTotal = 0, retailTotal = 0;
        foreach (var r in _reports.StockValue())
        {
            Rows.Add(new ReportLine
            {
                C1 = r.ProductName,
                C2 = r.BrandName ?? "",
                C3 = r.Qty.ToString("0.##"),
                C4 = MoneyFormatter.Format(r.PurchaseValuePaisa),
                C5 = MoneyFormatter.Format(r.RetailValuePaisa)
            });
            purchaseTotal += r.PurchaseValuePaisa;
            retailTotal += r.RetailValuePaisa;
        }
        TotalText = $"{S("StockPurchaseValue")}: {MoneyFormatter.Format(purchaseTotal)}   |   " +
                    $"{S("StockSaleValue")}: {MoneyFormatter.Format(retailTotal)}";
    }

    // ---------- print / export ----------
    private List<string> CurrentHeaders()
    {
        var h = new List<string> { Header1, Header2, Header3, Header4, Header5 };
        while (h.Count > 0 && string.IsNullOrWhiteSpace(h[^1])) h.RemoveAt(h.Count - 1);
        return h;
    }

    private List<IReadOnlyList<string>> CurrentRowsAsLists()
    {
        var headers = CurrentHeaders();
        var list = new List<IReadOnlyList<string>>();
        foreach (var r in Rows)
        {
            var cells = new List<string> { r.C1, r.C2, r.C3, r.C4, r.C5 };
            list.Add(cells.Take(headers.Count).ToList());
        }
        return list;
    }

    [RelayCommand]
    private void PrintReport()
    {
        if (!_hasRun) RunReport();
        var headers = CurrentHeaders();
        new ReportPrintService().PrintReport(
            ReportTitle,
            SelectedReport is "Receivable" or "Payable" or "StockValue" ? "" : RangeText,
            headers,
            CurrentRowsAsLists(),
            TotalText,
            _settings.GetAll(),
            _settings.Get("a4_printer", ""));
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (!_hasRun) RunReport();
        var dialog = new SaveFileDialog
        {
            Filter = "Excel CSV (*.csv)|*.csv",
            FileName = $"{SelectedReport}-{FromDb}-to-{ToDb}.csv"
        };
        if (dialog.ShowDialog() != true) return;

        var headers = CurrentHeaders();
        var rows = CurrentRowsAsLists()
            .Select(r => (IReadOnlyList<string>)r).ToList();
        CsvExport.WriteCsv(dialog.FileName, headers, rows);
        Info(S("ExportDoneMsg"));
    }
}
