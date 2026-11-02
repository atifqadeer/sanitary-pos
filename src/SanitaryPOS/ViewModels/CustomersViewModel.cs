using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Customers &amp; Khata screen: customer list + add/edit form on one side,
/// selected customer's ledger, balance and Receive Payment (وصولی) on the other.
/// </summary>
public partial class CustomersViewModel : ViewModelBase
{
    private readonly CustomerService _customers;
    private readonly PaymentService _payments;
    private readonly SessionService _session;

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<LedgerEntry> Ledger { get; } = new();

    public string[] CustomerTypes { get; } = { "WalkIn", "Regular", "Plumber", "Contractor" };
    public string[] PaymentMethods { get; } = { "Cash", "Card", "Bank", "JazzCash", "EasyPaisa" };

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Customer? _selectedCustomer;

    // ----- form -----
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formMobile = "";
    [ObservableProperty] private string _formAddress = "";
    [ObservableProperty] private string _formCity = "";
    [ObservableProperty] private string _formType = "Regular";
    [ObservableProperty] private string _formCreditLimitText = "0";
    [ObservableProperty] private string _formOpeningText = "0";
    private long _editingId;

    // ----- receive payment -----
    [ObservableProperty] private string _paymentAmountText = "";
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _paymentNote = "";

    public string BalanceText => SelectedCustomer == null
        ? "Rs 0" : MoneyFormatter.Format(SelectedCustomer.BalancePaisa);
    public bool ShowCreditWarning => SelectedCustomer?.IsOverCreditLimit == true;

    public CustomersViewModel(LocalizationService loc, CustomerService customers,
                              PaymentService payments, SessionService session)
        : base(loc)
    {
        _customers = customers; _payments = payments; _session = session;
        LoadCustomers();
    }

    [RelayCommand]
    private void LoadCustomers()
    {
        Customers.Clear();
        foreach (var c in _customers.GetAll(SearchText)) Customers.Add(c);
    }

    partial void OnSearchTextChanged(string value) => LoadCustomers();

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value != null) FillForm(value);
        LoadLedger();
        OnPropertyChanged(nameof(BalanceText));
        OnPropertyChanged(nameof(ShowCreditWarning));
    }

    private void FillForm(Customer c)
    {
        _editingId = c.Id;
        FormName = c.Name;
        FormMobile = c.Mobile ?? "";
        FormAddress = c.Address ?? "";
        FormCity = c.City ?? "";
        FormType = c.CustomerType;
        FormCreditLimitText = MoneyFormatter.ToRupeesText(c.CreditLimitPaisa);
        FormOpeningText = MoneyFormatter.ToRupeesText(c.OpeningBalancePaisa);
    }

    [RelayCommand]
    private void NewCustomer()
    {
        _editingId = 0;
        FormName = ""; FormMobile = ""; FormAddress = ""; FormCity = "";
        FormType = "Regular"; FormCreditLimitText = "0"; FormOpeningText = "0";
    }

    [RelayCommand]
    private void SaveCustomer()
    {
        if (string.IsNullOrWhiteSpace(FormName)) { Warn(S("CustomerName")); return; }
        var c = new Customer
        {
            Id = _editingId,
            Name = FormName.Trim(),
            Mobile = string.IsNullOrWhiteSpace(FormMobile) ? null : FormMobile.Trim(),
            Address = string.IsNullOrWhiteSpace(FormAddress) ? null : FormAddress.Trim(),
            City = string.IsNullOrWhiteSpace(FormCity) ? null : FormCity.Trim(),
            CustomerType = FormType,
            CreditLimitPaisa = MoneyFormatter.ParseRupeesOrZero(FormCreditLimitText),
            OpeningBalancePaisa = MoneyFormatter.ParseRupeesOrZero(FormOpeningText),
            IsActive = 1
        };
        long id = _customers.Save(c);
        Info(S("CustomerSaved"));
        LoadCustomers();
        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == id);
    }

    private void LoadLedger()
    {
        Ledger.Clear();
        if (SelectedCustomer == null) return;
        foreach (var e in _customers.GetLedger(SelectedCustomer.Id)) Ledger.Add(e);
    }

    [RelayCommand]
    private void ReceivePayment()
    {
        if (SelectedCustomer == null) return;
        long amount = MoneyFormatter.ParseRupeesOrZero(PaymentAmountText);
        if (amount <= 0) { Warn(S("PaymentAmount")); return; }

        _payments.ReceiveCustomerPayment(SelectedCustomer.Id, amount, PaymentMethod,
                                         string.IsNullOrWhiteSpace(PaymentNote) ? null : PaymentNote,
                                         _session.CurrentUser?.Id);
        Info(S("PaymentSavedMsg"));
        PaymentAmountText = "";
        PaymentNote = "";

        // Refresh list + ledger so the new balance shows everywhere.
        long id = SelectedCustomer.Id;
        LoadCustomers();
        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>Helper the Billing flow can reuse: would this new balance break the credit limit?</summary>
    public bool WouldExceedCreditLimit(Customer customer, long extraUdhaarPaisa)
        => customer.CreditLimitPaisa > 0 &&
           customer.BalancePaisa + extraUdhaarPaisa > customer.CreditLimitPaisa;
}
