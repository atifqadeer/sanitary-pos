using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Suppliers screen — the mirror of Customers/Khata, on the money-we-owe side:
/// supplier list + add/edit form, the selected supplier's payable ledger, and
/// a Pay Supplier panel (writes a payments OUT row via PaymentService).
/// Header shows the shop's TOTAL payable to all suppliers.
/// </summary>
public partial class SuppliersViewModel : ViewModelBase
{
    private readonly SupplierService _suppliers;
    private readonly PaymentService _payments;
    private readonly SessionService _session;

    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<LedgerEntry> Ledger { get; } = new();

    public string[] PaymentMethods { get; } = { "Cash", "Card", "Bank", "JazzCash", "EasyPaisa" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _totalPayableText = "Rs 0";

    // ----- form -----
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formContactPerson = "";
    [ObservableProperty] private string _formMobile = "";
    [ObservableProperty] private string _formAddress = "";
    [ObservableProperty] private string _formCity = "";
    [ObservableProperty] private string _formOpeningText = "0";
    private long _editingId;

    // ----- pay supplier -----
    [ObservableProperty] private string _paymentAmountText = "";
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _paymentNote = "";

    public string BalanceText => SelectedSupplier == null
        ? "Rs 0" : MoneyFormatter.Format(SelectedSupplier.BalancePaisa);

    public SuppliersViewModel(LocalizationService loc, SupplierService suppliers,
                              PaymentService payments, SessionService session)
        : base(loc)
    {
        _suppliers = suppliers; _payments = payments; _session = session;
        LoadSuppliers();
    }

    [RelayCommand]
    private void LoadSuppliers()
    {
        Suppliers.Clear();
        foreach (var s in _suppliers.GetAll(SearchText)) Suppliers.Add(s);
        TotalPayableText = MoneyFormatter.Format(_suppliers.GetTotalPayable());
    }

    partial void OnSearchTextChanged(string value) => LoadSuppliers();

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        if (value != null) FillForm(value);
        LoadLedger();
        OnPropertyChanged(nameof(BalanceText));
    }

    private void FillForm(Supplier s)
    {
        _editingId = s.Id;
        FormName = s.Name;
        FormContactPerson = s.ContactPerson ?? "";
        FormMobile = s.Mobile ?? "";
        FormAddress = s.Address ?? "";
        FormCity = s.City ?? "";
        FormOpeningText = MoneyFormatter.ToRupeesText(s.OpeningBalancePaisa);
    }

    [RelayCommand]
    private void NewSupplier()
    {
        _editingId = 0;
        FormName = ""; FormContactPerson = ""; FormMobile = "";
        FormAddress = ""; FormCity = ""; FormOpeningText = "0";
    }

    [RelayCommand]
    private void SaveSupplier()
    {
        if (string.IsNullOrWhiteSpace(FormName)) { Warn(S("SupplierNameRequiredMsg")); return; }
        var s = new Supplier
        {
            Id = _editingId,
            Name = FormName.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(FormContactPerson) ? null : FormContactPerson.Trim(),
            Mobile = string.IsNullOrWhiteSpace(FormMobile) ? null : FormMobile.Trim(),
            Address = string.IsNullOrWhiteSpace(FormAddress) ? null : FormAddress.Trim(),
            City = string.IsNullOrWhiteSpace(FormCity) ? null : FormCity.Trim(),
            OpeningBalancePaisa = MoneyFormatter.ParseRupeesOrZero(FormOpeningText),
            IsActive = 1
        };
        long id = _suppliers.Save(s);
        Info(S("SupplierSaved"));
        LoadSuppliers();
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == id);
    }

    private void LoadLedger()
    {
        Ledger.Clear();
        if (SelectedSupplier == null) return;
        foreach (var e in _suppliers.GetLedger(SelectedSupplier.Id)) Ledger.Add(e);
    }

    [RelayCommand]
    private void PaySupplier()
    {
        if (SelectedSupplier == null) return;
        long amount = MoneyFormatter.ParseRupeesOrZero(PaymentAmountText);
        if (amount <= 0) { Warn(S("PaymentAmount")); return; }

        _payments.PaySupplier(SelectedSupplier.Id, amount, PaymentMethod,
                              string.IsNullOrWhiteSpace(PaymentNote) ? null : PaymentNote,
                              _session.CurrentUser?.Id);
        Info(S("SupplierPaymentSavedMsg"));
        PaymentAmountText = "";
        PaymentNote = "";

        long id = SelectedSupplier.Id;
        LoadSuppliers();
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == id);
    }
}
