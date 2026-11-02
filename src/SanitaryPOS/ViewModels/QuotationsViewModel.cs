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
/// Quotation screen: top half = editor (same line logic as billing), bottom
/// half = saved quotations with Print (A4), Convert to Bill, and — Phase 2 —
/// Edit: an OPEN quotation loads back into the editor and saving UPDATES it
/// (same quotation number, no duplicate). Converted quotations cannot be edited.
/// </summary>
public partial class QuotationsViewModel : ViewModelBase
{
    private readonly ProductService _products;
    private readonly CustomerService _customers;
    private readonly QuotationService _quotations;
    private readonly SettingsService _settings;
    private readonly SessionService _session;

    public ObservableCollection<BillLine> Lines { get; } = new();
    public ObservableCollection<Product> SearchResults { get; } = new();
    public ObservableCollection<Customer> CustomerOptions { get; } = new();
    public ObservableCollection<Quotation> SavedQuotations { get; } = new();

    public string[] PriceLevels { get; } = { "Retail", "Trade", "Wholesale" };
    public string[] DiscountModes { get; } = { "Rs", "Percent" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private string _guestName = "";
    [ObservableProperty] private string _guestMobile = "";
    [ObservableProperty] private string _selectedPriceLevel = "Retail";
    [ObservableProperty] private string _billDiscountText = "0";
    [ObservableProperty] private Quotation? _selectedQuotation;

    [ObservableProperty] private long _subtotalPaisa;
    [ObservableProperty] private long _itemDiscountPaisa;
    [ObservableProperty] private long _grandTotalPaisa;

    public string SubtotalText => MoneyFormatter.Format(SubtotalPaisa);
    public string GrandTotalText => MoneyFormatter.Format(GrandTotalPaisa);
    public bool CanConvert => SelectedQuotation?.Status == "Open";
    public bool CanEdit => SelectedQuotation?.Status == "Open";

    /// <summary>0 = editor holds a NEW quotation; &gt; 0 = editing that saved quotation.</summary>
    private long _editingQuotationId;
    public bool IsEditing => _editingQuotationId > 0;

    public QuotationsViewModel(LocalizationService loc, ProductService products, CustomerService customers,
                               QuotationService quotations, SettingsService settings, SessionService session)
        : base(loc)
    {
        _products = products; _customers = customers; _quotations = quotations;
        _settings = settings; _session = session;

        CustomerOptions.Add(new Customer { Id = 0, Name = S("WalkIn") });
        foreach (var c in customers.GetAll()) CustomerOptions.Add(c);
        SelectedCustomer = CustomerOptions[0];

        Lines.CollectionChanged += LinesChanged;
        LoadSaved();
    }

    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BillLine l in e.NewItems) l.PropertyChanged += (_, _) => Recalculate();
        Recalculate();
    }

    partial void OnBillDiscountTextChanged(string value) => Recalculate();

    partial void OnSelectedQuotationChanged(Quotation? value)
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanEdit));
    }

    private void Recalculate()
    {
        SubtotalPaisa = Lines.Sum(l => l.GrossPaisa);
        ItemDiscountPaisa = Lines.Sum(l => l.DiscountPaisa);
        long billDisc = MoneyFormatter.ParseRupeesOrZero(BillDiscountText);
        long net = SubtotalPaisa - ItemDiscountPaisa - billDisc;
        GrandTotalPaisa = net < 0 ? 0 : net;
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(GrandTotalText));
    }

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
        else Lines.Add(BillLine.FromProduct(product, SelectedPriceLevel));
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(BillLine? line)
    {
        if (line != null) Lines.Remove(line);
    }

    [RelayCommand]
    private void NewQuotation()
    {
        _editingQuotationId = 0;
        OnPropertyChanged(nameof(IsEditing));
        Lines.Clear();
        GuestName = ""; GuestMobile = ""; BillDiscountText = "0";
        SelectedCustomer = CustomerOptions[0];
        Recalculate();
    }

    /// <summary>Phase 2 — Load the selected OPEN quotation back into the editor for editing.</summary>
    [RelayCommand]
    private void EditQuotation()
    {
        if (SelectedQuotation == null || SelectedQuotation.Status != "Open") return;
        var detail = _quotations.GetDetail(SelectedQuotation.Id);
        if (detail == null) return;
        var q = detail.Quotation;

        NewQuotation();
        _editingQuotationId = q.Id;
        OnPropertyChanged(nameof(IsEditing));

        SelectedPriceLevel = q.PriceLevel;
        if (q.CustomerId.HasValue)
        {
            SelectedCustomer = CustomerOptions.FirstOrDefault(c => c.Id == q.CustomerId.Value)
                               ?? CustomerOptions[0];
            GuestName = ""; GuestMobile = "";
        }
        else
        {
            SelectedCustomer = CustomerOptions[0];
            GuestName = q.CustomerNameSnapshot ?? "";
            GuestMobile = q.CustomerMobileSnapshot ?? "";
        }
        // In schema terms DiscountPaisa on a quotation is the BILL discount only
        // (line discounts live on the items), exactly as SaveQuotation writes it.
        BillDiscountText = MoneyFormatter.ToRupeesText(q.DiscountPaisa);

        foreach (var i in detail.Items)
        {
            var p = _products.GetById(i.ProductId);
            Lines.Add(new BillLine
            {
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                AvailableStock = p?.StockQty ?? 0,
                Qty = i.Qty,
                RatePaisa = i.RatePaisa,
                DiscountMode = "Rs",
                DiscountValue = i.DiscountPaisa / 100.0
            });
        }
        Recalculate();
    }

    [RelayCommand]
    private void SaveQuotation()
    {
        if (Lines.Count == 0) { Warn(S("EmptyBillMsg")); return; }

        bool savedCustomer = SelectedCustomer != null && SelectedCustomer.Id > 0;
        var q = new Quotation
        {
            Id = _editingQuotationId, // 0 = new, > 0 = update the loaded quotation
            CustomerId = savedCustomer ? SelectedCustomer!.Id : null,
            CustomerNameSnapshot = savedCustomer ? SelectedCustomer!.Name
                                   : (string.IsNullOrWhiteSpace(GuestName) ? null : GuestName.Trim()),
            CustomerMobileSnapshot = savedCustomer ? SelectedCustomer!.Mobile
                                   : (string.IsNullOrWhiteSpace(GuestMobile) ? null : GuestMobile.Trim()),
            PriceLevel = SelectedPriceLevel,
            SubtotalPaisa = SubtotalPaisa,
            DiscountPaisa = MoneyFormatter.ParseRupeesOrZero(BillDiscountText),
            TotalPaisa = GrandTotalPaisa,
            ValidDays = long.TryParse(_settings.Get("quotation_valid_days", "7"), out long vd) ? vd : 7,
            Status = "Open",
            UserId = _session.CurrentUser?.Id
        };

        var items = Lines.Select(l => l.ToQuotationItem()).ToList();
        if (_editingQuotationId > 0)
        {
            try
            {
                _quotations.UpdateQuotation(q, items);
            }
            catch (Exception ex)
            {
                Warn(ex.Message);
                return;
            }
            Info($"{S("QuotationUpdatedMsg")} — {MoneyFormatter.Format(q.TotalPaisa)}");
        }
        else
        {
            var saved = _quotations.SaveQuotation(q, items);
            Info($"{S("QuotationSavedMsg")}  #{saved.QuotationNo} — {MoneyFormatter.Format(saved.TotalPaisa)}");
        }
        LoadSaved();
        NewQuotation();
    }

    [RelayCommand]
    private void PrintQuotation()
    {
        if (SelectedQuotation == null) return;
        var detail = _quotations.GetDetail(SelectedQuotation.Id);
        if (detail == null) return;
        string? customerName = detail.Quotation.CustomerId.HasValue
            ? _customers.GetById(detail.Quotation.CustomerId.Value)?.Name
            : null;
        new QuotationPrintService().PrintQuotation(
            detail.Quotation, detail.Items, _settings.GetAll(), customerName,
            _settings.Get("a4_printer", ""));
    }

    [RelayCommand]
    private void ConvertToBill()
    {
        if (SelectedQuotation == null || SelectedQuotation.Status != "Open") return;
        try
        {
            // Phase 1 rule: conversion = full cash payment of the quotation total.
            // For udhaar conversion, convert here then record part-payment at Billing —
            // simpler: the sale is Cash/paid-in-full. See PHASE1.md.
            _quotations.ConvertToBill(SelectedQuotation.Id, "Cash",
                                      SelectedQuotation.TotalPaisa, _session.CurrentUser?.Id);
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            return;
        }
        Info(S("ConvertedMsg"));
        LoadSaved();
    }

    private void LoadSaved()
    {
        SavedQuotations.Clear();
        foreach (var q in _quotations.GetAll()) SavedQuotations.Add(q);
    }
}
