using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Products / Inventory screen: searchable list on the left, add/edit form on the right.
/// Rates are edited as Rupees text and converted to paisa on save.
/// Purchase rate is hidden for Cashier (IsAdmin gates the form field AND the grid column).
/// </summary>
public partial class ProductsViewModel : ViewModelBase
{
    private readonly ProductService _products;
    private readonly SessionService _session;

    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<Brand> Brands { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<ProductUnit> Units { get; } = new();

    public bool IsAdmin => _session.IsAdmin;
    public string[] Locations { get; } = { "Shop", "Godown" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Product? _selectedProduct;

    // ----- Edit form fields -----
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private Brand? _formBrand;
    [ObservableProperty] private string _formNewBrand = "";
    [ObservableProperty] private Category? _formCategory;
    [ObservableProperty] private ProductUnit? _formUnit;
    [ObservableProperty] private string _formModelCode = "";
    [ObservableProperty] private string _formBarcode = "";
    [ObservableProperty] private string _formFinish = "";
    [ObservableProperty] private string _formSize = "";
    [ObservableProperty] private string _formPurchaseText = "0";
    [ObservableProperty] private string _formRetailText = "0";
    [ObservableProperty] private string _formTradeText = "0";
    [ObservableProperty] private string _formWholesaleText = "0";
    [ObservableProperty] private string _formLowStockText = "2";
    [ObservableProperty] private string _formLocation = "Shop";
    [ObservableProperty] private string _formOpeningStockText = "0";
    [ObservableProperty] private bool _formIsActive = true;
    [ObservableProperty] private bool _isEditingExisting;

    // ----- Quick price update -----
    [ObservableProperty] private string _quickPriceText = "";

    // ----- Bulk price update (Phase 2, Admin only) -----
    public string[] BulkPriceFields { get; } = { "Retail", "Trade", "Wholesale", "Purchase" };
    [ObservableProperty] private Brand? _bulkBrand;
    [ObservableProperty] private string _bulkField = "Retail";
    [ObservableProperty] private string _bulkPercentText = "";
    [ObservableProperty] private int _bulkAffectedCount;

    partial void OnBulkBrandChanged(Brand? value)
        => BulkAffectedCount = value == null ? 0 : _products.CountByBrand(value.Id);

    private long _editingId;

    public ProductsViewModel(LocalizationService loc, ProductService products, SessionService session)
        : base(loc)
    {
        _products = products;
        _session = session;
        LoadLookups();
        LoadProducts();
        NewForm();
    }

    private void LoadLookups()
    {
        Brands.Clear(); foreach (var b in _products.GetBrands()) Brands.Add(b);
        Categories.Clear(); foreach (var c in _products.GetCategories()) Categories.Add(c);
        Units.Clear(); foreach (var u in _products.GetUnits()) Units.Add(u);
    }

    [RelayCommand]
    private void LoadProducts()
    {
        Products.Clear();
        foreach (var p in _products.GetAll(SearchText, activeOnly: false)) Products.Add(p);
    }

    partial void OnSearchTextChanged(string value) => LoadProducts();

    partial void OnSelectedProductChanged(Product? value)
    {
        if (value != null) FillForm(value);
    }

    [RelayCommand]
    private void NewForm()
    {
        _editingId = 0;
        IsEditingExisting = false;
        FormName = ""; FormBrand = null; FormNewBrand = ""; FormCategory = null; FormUnit = null;
        FormModelCode = ""; FormBarcode = ""; FormFinish = ""; FormSize = "";
        FormPurchaseText = "0"; FormRetailText = "0"; FormTradeText = "0"; FormWholesaleText = "0";
        FormLowStockText = "2"; FormLocation = "Shop"; FormOpeningStockText = "0"; FormIsActive = true;
    }

    private void FillForm(Product p)
    {
        _editingId = p.Id;
        IsEditingExisting = true;
        FormName = p.Name;
        FormBrand = Brands.FirstOrDefault(b => b.Id == p.BrandId);
        FormNewBrand = "";
        FormCategory = Categories.FirstOrDefault(c => c.Id == p.CategoryId);
        FormUnit = Units.FirstOrDefault(u => u.Id == p.UnitId);
        FormModelCode = p.ModelCode ?? "";
        FormBarcode = p.Barcode ?? "";
        FormFinish = p.Finish ?? "";
        FormSize = p.Size ?? "";
        FormPurchaseText = MoneyFormatter.ToRupeesText(p.PurchaseRatePaisa);
        FormRetailText = MoneyFormatter.ToRupeesText(p.RetailRatePaisa);
        FormTradeText = MoneyFormatter.ToRupeesText(p.TradeRatePaisa);
        FormWholesaleText = MoneyFormatter.ToRupeesText(p.WholesaleRatePaisa);
        FormLowStockText = p.LowStockLevel.ToString("0.##");
        FormLocation = p.Location;
        FormOpeningStockText = "0";
        FormIsActive = p.IsActive == 1;
    }

    [RelayCommand]
    private void SaveProduct()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            Warn(S("Name"));
            return;
        }

        // Brand: a typed new brand name wins, otherwise the selected brand.
        long? brandId = FormBrand?.Id;
        if (!string.IsNullOrWhiteSpace(FormNewBrand))
            brandId = _products.GetOrCreateBrand(FormNewBrand);

        var p = new Product
        {
            Id = _editingId,
            Name = FormName.Trim(),
            BrandId = brandId,
            CategoryId = FormCategory?.Id,
            UnitId = FormUnit?.Id,
            ModelCode = NullIfEmpty(FormModelCode),
            Barcode = NullIfEmpty(FormBarcode),
            Finish = NullIfEmpty(FormFinish),
            Size = NullIfEmpty(FormSize),
            PurchaseRatePaisa = MoneyFormatter.ParseRupeesOrZero(FormPurchaseText),
            RetailRatePaisa = MoneyFormatter.ParseRupeesOrZero(FormRetailText),
            TradeRatePaisa = MoneyFormatter.ParseRupeesOrZero(FormTradeText),
            WholesaleRatePaisa = MoneyFormatter.ParseRupeesOrZero(FormWholesaleText),
            LowStockLevel = double.TryParse(FormLowStockText, out double lvl) ? lvl : 2,
            Location = FormLocation,
            IsActive = FormIsActive ? 1 : 0
        };

        // Opening stock only applies when CREATING a product (stock rule: via movement).
        double opening = _editingId == 0 && double.TryParse(FormOpeningStockText, out double oq) ? oq : 0;

        try
        {
            _products.Save(p, opening, _session.CurrentUser?.Id);
        }
        catch (Exception ex)
        {
            // Most common: duplicate barcode (UNIQUE in schema).
            Warn(ex.Message);
            return;
        }

        Info(S("ProductSaved"));
        LoadLookups();
        LoadProducts();
        NewForm();
    }

    [RelayCommand]
    private void QuickPrice()
    {
        if (SelectedProduct == null) return;
        long paisa = MoneyFormatter.ParseRupeesOrZero(QuickPriceText);
        _products.UpdateRetailPrice(SelectedProduct.Id, paisa);
        LoadProducts();
    }

    /// <summary>
    /// Phase 2 — Bulk price update for one whole brand. Admin only (a cashier
    /// must never move prices in bulk). A bilingual confirmation is shown
    /// BEFORE anything changes, naming the brand, the price field, the % and
    /// how many products will change.
    /// </summary>
    [RelayCommand]
    private void ApplyBulkPrice()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (BulkBrand == null) { Warn(S("SelectBrandMsg")); return; }
        if (!double.TryParse(BulkPercentText, System.Globalization.NumberStyles.Number,
                             System.Globalization.CultureInfo.InvariantCulture, out double percent)
            || percent == 0)
        {
            Warn(S("BulkPercentRequiredMsg"));
            return;
        }

        int count = _products.CountByBrand(BulkBrand.Id);
        if (count == 0) { Warn(S("SelectBrandMsg")); return; }

        string confirm = string.Format(S("BulkPriceConfirmMsg"),
                                       BulkField, BulkBrand.Name, percent, count);
        var answer = System.Windows.MessageBox.Show(
            confirm, S("BulkPriceUpdate"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        int affected = _products.BulkUpdatePrices(BulkBrand.Id, BulkField, percent,
                                                  _session.CurrentUser?.Id);
        Info(string.Format(S("BulkPriceDoneMsg"), affected));
        BulkAffectedCount = _products.CountByBrand(BulkBrand.Id);
        LoadProducts();
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
