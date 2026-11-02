using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Purchase (GRN) screen. Top: enter a supplier bill — supplier (or add a new
/// supplier inline), their bill number, date, items found by search/barcode
/// with qty + purchase rate, one bill discount, paid-now amount + method.
/// Bottom: purchase history with a read-only detail of the selected purchase.
///
/// Saving is one transaction in PurchaseService: stock goes UP through
/// StockService, the product's purchase rate moves to this bill's rate, and a
/// paid-now amount becomes a supplier payment (payable balance drops).
/// </summary>
public partial class PurchaseViewModel : ViewModelBase
{
    private readonly ProductService _products;
    private readonly SupplierService _suppliers;
    private readonly PurchaseService _purchases;
    private readonly SessionService _session;

    public ObservableCollection<PurchaseLine> Lines { get; } = new();
    public ObservableCollection<Product> SearchResults { get; } = new();
    public ObservableCollection<Supplier> SupplierOptions { get; } = new();
    public ObservableCollection<Purchase> History { get; } = new();
    public ObservableCollection<PurchaseItem> DetailItems { get; } = new();

    public string[] PaymentMethods { get; } = { "Cash", "Card", "Bank", "JazzCash", "EasyPaisa" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _newSupplierName = "";
    [ObservableProperty] private string _supplierBillNo = "";
    [ObservableProperty] private DateTime _purchaseDate = DateTime.Today;
    [ObservableProperty] private string _discountText = "0";
    [ObservableProperty] private string _paidText = "";
    [ObservableProperty] private string _paidMethod = "Cash";
    [ObservableProperty] private Purchase? _selectedPurchase;

    [ObservableProperty] private long _subtotalPaisa;
    [ObservableProperty] private long _grandTotalPaisa;
    [ObservableProperty] private long _balancePayablePaisa;

    public string SubtotalText => MoneyFormatter.Format(SubtotalPaisa);
    public string GrandTotalText => MoneyFormatter.Format(GrandTotalPaisa);
    public string BalancePayableText => MoneyFormatter.Format(BalancePayablePaisa);

    public PurchaseViewModel(LocalizationService loc, ProductService products,
                             SupplierService suppliers, PurchaseService purchases,
                             SessionService session)
        : base(loc)
    {
        _products = products; _suppliers = suppliers; _purchases = purchases; _session = session;

        LoadSuppliers();
        Lines.CollectionChanged += LinesChanged;
        LoadHistory();
    }

    private void LoadSuppliers()
    {
        SupplierOptions.Clear();
        foreach (var s in _suppliers.GetAll()) SupplierOptions.Add(s);
    }

    // ---------- totals plumbing (same pattern as Billing) ----------
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (PurchaseLine l in e.NewItems) l.PropertyChanged += LineChanged;
        if (e.OldItems != null)
            foreach (PurchaseLine l in e.OldItems) l.PropertyChanged -= LineChanged;
        Recalculate();
    }

    private void LineChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    partial void OnDiscountTextChanged(string value) => Recalculate();
    partial void OnPaidTextChanged(string value) => Recalculate();

    private void Recalculate()
    {
        SubtotalPaisa = Lines.Sum(l => l.LineTotalPaisa);
        long disc = MoneyFormatter.ParseRupeesOrZero(DiscountText);
        long net = SubtotalPaisa - disc;
        GrandTotalPaisa = net < 0 ? 0 : net;
        long paid = MoneyFormatter.ParseRupeesOrZero(PaidText);
        BalancePayablePaisa = GrandTotalPaisa > paid ? GrandTotalPaisa - paid : 0;

        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(BalancePayableText));
    }

    // ---------- search & add (same behaviour as Billing) ----------
    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var p in _products.GetAll(value).Take(8)) SearchResults.Add(p);
    }

    public void OnSearchEnter()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        var byBarcode = _products.GetByBarcode(SearchText);
        if (byBarcode != null) { AddProduct(byBarcode); SearchText = ""; return; }
        if (SearchResults.Count == 1) { AddProduct(SearchResults[0]); SearchText = ""; return; }
        if (SearchResults.Count == 0) Warn(S("NoProductFoundMsg"));
    }

    [RelayCommand]
    private void AddProduct(Product? product)
    {
        if (product == null) return;
        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing != null) existing.Qty += 1;
        else Lines.Add(PurchaseLine.FromProduct(product));
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(PurchaseLine? line)
    {
        if (line != null) Lines.Remove(line);
    }

    // ---------- inline new supplier ----------
    [RelayCommand]
    private void AddSupplier()
    {
        if (string.IsNullOrWhiteSpace(NewSupplierName)) { Warn(S("SupplierNameRequiredMsg")); return; }
        long id = _suppliers.Save(new Supplier { Name = NewSupplierName.Trim(), IsActive = 1 });
        NewSupplierName = "";
        LoadSuppliers();
        SelectedSupplier = SupplierOptions.FirstOrDefault(s => s.Id == id);
        Info(S("SupplierSaved"));
    }

    // ---------- save ----------
    [RelayCommand]
    private void SavePurchase()
    {
        if (SelectedSupplier == null) { Warn(S("SelectSupplierMsg")); return; }
        if (Lines.Count == 0) { Warn(S("EmptyBillMsg")); return; }

        long paid = MoneyFormatter.ParseRupeesOrZero(PaidText);
        if (paid > GrandTotalPaisa) paid = GrandTotalPaisa; // never "pay" more than the bill here

        var purchase = new Purchase
        {
            SupplierId = SelectedSupplier.Id,
            SupplierBillNo = string.IsNullOrWhiteSpace(SupplierBillNo) ? null : SupplierBillNo.Trim(),
            SubtotalPaisa = SubtotalPaisa,
            DiscountPaisa = MoneyFormatter.ParseRupeesOrZero(DiscountText),
            TotalPaisa = GrandTotalPaisa,
            PaidPaisa = paid,
            UserId = _session.CurrentUser?.Id,
            // The date chosen on screen decides created_at (a GRN is often
            // entered a day or two after the goods arrived).
            CreatedAt = PurchaseDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };

        try
        {
            var saved = _purchases.SavePurchase(purchase, Lines.Select(l => l.ToPurchaseItem()).ToList(),
                                                PaidMethod, _session.CurrentUser?.Id);
            Info($"{S("PurchaseSavedMsg")} — {MoneyFormatter.Format(saved.TotalPaisa)}");
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            return;
        }

        ClearForm();
        LoadHistory();
    }

    private void ClearForm()
    {
        Lines.Clear();
        SupplierBillNo = "";
        DiscountText = "0";
        PaidText = "";
        PaidMethod = "Cash";
        PurchaseDate = DateTime.Today;
        Recalculate();
    }

    // ---------- history ----------
    private void LoadHistory()
    {
        History.Clear();
        foreach (var p in _purchases.GetAll()) History.Add(p);
    }

    partial void OnSelectedPurchaseChanged(Purchase? value)
    {
        DetailItems.Clear();
        if (value == null) return;
        var detail = _purchases.GetDetail(value.Id);
        if (detail == null) return;
        foreach (var i in detail.Items) DetailItems.Add(i);
    }
}
