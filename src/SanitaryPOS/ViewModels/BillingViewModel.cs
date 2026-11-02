using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Printing;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// POS Billing screen. Designed to fit 1366x768 with no scrolling:
/// search row on top, items grid in the middle, totals + payment at the bottom.
///
/// STOCK + UDHAAR RULES (enforced here + in SaleService):
/// - Completed bill: stock is cut in the same transaction as the save.
/// - Paid less than total = the rest goes to the customer's khata automatically,
///   and that REQUIRES a saved customer (walk-in cannot take udhaar).
/// - Overpayment is change to return: only the bill total is stored as paid.
/// </summary>
public partial class BillingViewModel : ViewModelBase
{
    private readonly ProductService _products;
    private readonly CustomerService _customers;
    private readonly SaleService _sales;
    private readonly SettingsService _settings;
    private readonly SessionService _session;

    private readonly bool _taxEnabled;
    private readonly decimal _taxRatePercent;

    public ObservableCollection<BillLine> Lines { get; } = new();
    public ObservableCollection<Product> SearchResults { get; } = new();
    public ObservableCollection<Customer> CustomerOptions { get; } = new();
    public ObservableCollection<Sale> HeldBills { get; } = new();

    public string[] PriceLevels { get; } = { "Retail", "Trade", "Wholesale" };
    public string[] PaymentMethods { get; } = { "Cash", "Card", "Bank", "JazzCash", "EasyPaisa", "Credit", "Mixed" };
    public string[] DiscountModes { get; } = { "Rs", "Percent" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private string _selectedPriceLevel = "Retail";
    [ObservableProperty] private string _selectedPaymentMethod = "Cash";
    [ObservableProperty] private string _paidText = "";
    [ObservableProperty] private string _billDiscountText = "0";
    [ObservableProperty] private bool _heldListVisible;

    /// <summary>Phase 2: green status line, e.g. the exchange credit note after a return.</summary>
    [ObservableProperty] private string _statusMessage = "";
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    // ----- Computed totals (paisa, refreshed by Recalculate) -----
    [ObservableProperty] private long _subtotalPaisa;
    [ObservableProperty] private long _itemDiscountPaisa;
    [ObservableProperty] private long _billDiscountPaisa;
    [ObservableProperty] private long _taxPaisa;
    [ObservableProperty] private long _grandTotalPaisa;
    [ObservableProperty] private long _changePaisa;
    [ObservableProperty] private long _balanceToKhataPaisa;

    public string SubtotalText => MoneyFormatter.Format(SubtotalPaisa);
    public string ItemDiscountText => MoneyFormatter.Format(ItemDiscountPaisa);
    public string TaxText => MoneyFormatter.Format(TaxPaisa);
    public string GrandTotalText => MoneyFormatter.Format(GrandTotalPaisa);
    public string ChangeText => MoneyFormatter.Format(ChangePaisa);
    public string BalanceToKhataText => MoneyFormatter.Format(BalanceToKhataPaisa);

    public BillingViewModel(LocalizationService loc, ProductService products, CustomerService customers,
                            SaleService sales, SettingsService settings, SessionService session)
        : base(loc)
    {
        _products = products; _customers = customers; _sales = sales;
        _settings = settings; _session = session;

        _taxEnabled = settings.TaxEnabled;
        _taxRatePercent = settings.TaxRatePercent;

        // Walk-in sentinel at the top of the customer list (Id = 0).
        CustomerOptions.Add(new Customer { Id = 0, Name = S("WalkIn") });
        foreach (var c in customers.GetAll()) CustomerOptions.Add(c);
        SelectedCustomer = CustomerOptions[0];

        Lines.CollectionChanged += LinesChanged;
    }

    // ---------- totals plumbing ----------
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BillLine l in e.NewItems) l.PropertyChanged += LineChanged;
        if (e.OldItems != null)
            foreach (BillLine l in e.OldItems) l.PropertyChanged -= LineChanged;
        Recalculate();
    }

    private void LineChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    partial void OnBillDiscountTextChanged(string value) => Recalculate();
    partial void OnPaidTextChanged(string value) => Recalculate();

    private void Recalculate()
    {
        SubtotalPaisa = Lines.Sum(l => l.GrossPaisa);
        ItemDiscountPaisa = Lines.Sum(l => l.DiscountPaisa);
        BillDiscountPaisa = MoneyFormatter.ParseRupeesOrZero(BillDiscountText);
        long net = SubtotalPaisa - ItemDiscountPaisa - BillDiscountPaisa;
        if (net < 0) net = 0;
        TaxPaisa = _taxEnabled ? (long)Math.Round(net * (double)_taxRatePercent / 100.0) : 0;
        GrandTotalPaisa = net + TaxPaisa;

        long paid = MoneyFormatter.ParseRupeesOrZero(PaidText);
        ChangePaisa = paid > GrandTotalPaisa ? paid - GrandTotalPaisa : 0;
        BalanceToKhataPaisa = GrandTotalPaisa > paid ? GrandTotalPaisa - paid : 0;

        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(ItemDiscountText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(BalanceToKhataText));
    }

    // ---------- search & add ----------
    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var p in _products.GetAll(value).Take(8)) SearchResults.Add(p);
    }

    /// <summary>Called by the View when Enter is pressed in the search box (barcode scanners send Enter).</summary>
    public void OnSearchEnter()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        var byBarcode = _products.GetByBarcode(SearchText);
        if (byBarcode != null)
        {
            AddProduct(byBarcode);
            SearchText = "";
            return;
        }
        if (SearchResults.Count == 1)
        {
            AddProduct(SearchResults[0]);
            SearchText = "";
            return;
        }
        if (SearchResults.Count == 0) Warn(S("NoProductFoundMsg"));
        // If many results: cashier clicks the right one in the results list.
    }

    [RelayCommand]
    private void AddProduct(Product? product)
    {
        if (product == null) return;
        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing != null)
        {
            existing.Qty += 1;
        }
        else
        {
            Lines.Add(BillLine.FromProduct(product, SelectedPriceLevel));
        }
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(BillLine? line)
    {
        if (line != null) Lines.Remove(line);
    }

    partial void OnSelectedPriceLevelChanged(string value)
    {
        // Re-rate all lines at the newly selected price level.
        foreach (var line in Lines)
        {
            var p = _products.GetById(line.ProductId);
            if (p != null) line.RatePaisa = p.RateFor(value);
        }
        Recalculate();
    }

    // ---------- bill actions ----------
    [RelayCommand]
    private void NewBill() => ClearBill();

    private void ClearBill()
    {
        Lines.Clear();
        SearchText = "";
        PaidText = "";
        StatusMessage = "";
        BillDiscountText = "0";
        SelectedPaymentMethod = "Cash";
        SelectedCustomer = CustomerOptions.Count > 0 ? CustomerOptions[0] : null;
        HeldListVisible = false;
        Recalculate();
    }

    private Sale BuildSale(string status)
    {
        long paid = MoneyFormatter.ParseRupeesOrZero(PaidText);
        if (string.IsNullOrWhiteSpace(PaidText))
            paid = SelectedPaymentMethod == "Credit" ? 0 : GrandTotalPaisa;
        if (paid > GrandTotalPaisa) paid = GrandTotalPaisa; // overpay = change, not khata credit

        return new Sale
        {
            CustomerId = SelectedCustomer != null && SelectedCustomer.Id > 0 ? SelectedCustomer.Id : null,
            PriceLevel = SelectedPriceLevel,
            SubtotalPaisa = SubtotalPaisa,
            ItemDiscountPaisa = ItemDiscountPaisa,
            BillDiscountPaisa = BillDiscountPaisa,
            TaxPaisa = TaxPaisa,
            TotalPaisa = GrandTotalPaisa,
            PaidPaisa = status == "Held" ? 0 : paid,
            PaymentMethod = SelectedPaymentMethod,
            Status = status,
            UserId = _session.CurrentUser?.Id
        };
    }

    private bool ValidateForComplete()
    {
        if (Lines.Count == 0) { Warn(S("EmptyBillMsg")); return false; }
        long paid = MoneyFormatter.ParseRupeesOrZero(PaidText);
        bool partPaid = !string.IsNullOrWhiteSpace(PaidText) && paid < GrandTotalPaisa;
        bool isCredit = SelectedPaymentMethod == "Credit";
        if ((isCredit || partPaid) && (SelectedCustomer == null || SelectedCustomer.Id == 0))
        {
            Warn(S("CreditCustomerRequiredMsg"));
            return false;
        }
        return true;
    }

    [RelayCommand]
    private void SaveBill() => Save(false);

    [RelayCommand]
    private void SaveAndPrint() => Save(true);

    private void Save(bool print)
    {
        if (!ValidateForComplete()) return;
        var sale = BuildSale("Completed");
        var items = Lines.Select(l => l.ToSaleItem()).ToList();
        var saved = _sales.SaveSale(sale, items);

        if (print)
        {
            var detail = _sales.GetSale(saved.Id);
            if (detail != null)
            {
                string? customerName = SelectedCustomer != null && SelectedCustomer.Id > 0
                    ? SelectedCustomer.Name : null;
                var receipt = new ReceiptService();
                receipt.PrintReceipt(detail.Sale, detail.Items, _settings.GetAll(),
                                     customerName,
                                     _settings.Get("receipt_printer", ""));
            }
        }

        Info($"{S("BillSavedMsg")}  #{saved.BillNo} — {MoneyFormatter.Format(saved.TotalPaisa)}");
        ClearBill();
    }

    /// <summary>
    /// Phase 2 — Exchange prefill (called by the shell after a return settled
    /// as Exchange): the returned customer is selected and the credit amount is
    /// shown on the status line. The new bill itself stays a normal bill —
    /// the counter adjusts Paid/Discount by hand against this credit.
    /// </summary>
    public void PrefillFromExchange(Customer? customer, long creditPaisa, long billNo)
    {
        ClearBill();
        if (customer != null)
        {
            SelectedCustomer = CustomerOptions.FirstOrDefault(c => c.Id == customer.Id)
                               ?? CustomerOptions[0];
        }
        StatusMessage = string.Format(S("ExchangeCreditMsg"),
                                      MoneyFormatter.Format(creditPaisa), billNo);
    }

    /// <summary>Phase 2 — Reprint the most recent Completed bill (closes a Phase 1 gap).</summary>
    [RelayCommand]
    private void ReprintLastBill()
    {
        var detail = _sales.GetLastCompletedSale();
        if (detail == null) { Warn(S("NoBillToReprintMsg")); return; }
        new ReceiptService().PrintReceipt(
            detail.Sale, detail.Items, _settings.GetAll(),
            detail.Sale.CustomerName,
            _settings.Get("receipt_printer", ""));
    }

    [RelayCommand]
    private void HoldBill()
    {
        if (Lines.Count == 0) { Warn(S("EmptyBillMsg")); return; }
        var sale = BuildSale("Held");
        var items = Lines.Select(l => l.ToSaleItem()).ToList();
        _sales.SaveSale(sale, items);
        Info(S("BillHeldMsg"));
        ClearBill();
    }

    [RelayCommand]
    private void ToggleHeldList()
    {
        HeldListVisible = !HeldListVisible;
        if (HeldListVisible)
        {
            HeldBills.Clear();
            foreach (var h in _sales.GetHeldBills()) HeldBills.Add(h);
        }
    }

    [RelayCommand]
    private void RecallBill(Sale? held)
    {
        if (held == null) return;
        var detail = _sales.GetSale(held.Id);
        if (detail == null) return;

        ClearBill();
        SelectedPriceLevel = detail.Sale.PriceLevel;
        SelectedCustomer = CustomerOptions.FirstOrDefault(c => c.Id == (detail.Sale.CustomerId ?? 0))
                           ?? CustomerOptions[0];
        BillDiscountText = MoneyFormatter.ToRupeesText(detail.Sale.BillDiscountPaisa);

        foreach (var i in detail.Items)
        {
            Lines.Add(new BillLine
            {
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                Qty = i.Qty,
                RatePaisa = i.RatePaisa,
                DiscountMode = "Rs",
                DiscountValue = i.DiscountPaisa / 100.0
            });
        }

        // Held record is removed so its stock (never cut) cannot be cut twice later.
        _sales.DeleteHeldSale(held.Id);
        HeldListVisible = false;
        Recalculate();
    }
}
