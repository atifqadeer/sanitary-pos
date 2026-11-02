using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Data;
using SanitaryPOS.Localization;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Shell ViewModel: sidebar navigation + header language toggle.
/// Navigation simply swaps CurrentView; App.xaml DataTemplates pick the View.
/// Phase 2/3 screens appear in the sidebar as placeholders on purpose,
/// so the shop staff learn one layout from day one.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly SessionService _session;
    private readonly ProductService _products;
    private readonly CustomerService _customers;
    private readonly PaymentService _payments;
    private readonly SaleService _sales;
    private readonly QuotationService _quotations;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _currentUserText = "";
    [ObservableProperty] private string _shopNameText = "";

    public bool IsAdmin => _session.IsAdmin;
    public bool IsUrdu => Loc.IsUrdu;

    private readonly LoginViewModel _loginVm;

    public MainViewModel(DatabaseService db, SettingsService settings, SessionService session,
                         LocalizationService loc, ProductService products, CustomerService customers,
                         PaymentService payments, SaleService sales, QuotationService quotations)
        : base(loc)
    {
        _db = db; _settings = settings; _session = session; _products = products;
        _customers = customers; _payments = payments; _sales = sales; _quotations = quotations;

        _loginVm = new LoginViewModel(loc, db, session);
        _loginVm.LoggedIn += user =>
        {
            IsLoggedIn = true;
            CurrentUserText = $"{user.FullName} ({user.Role})";
            OnPropertyChanged(nameof(IsAdmin));
            RefreshShopName();
            Navigate("Dashboard");
        };

        Loc.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsUrdu));
            if (CurrentView is ViewModelBase vm) vm.OnLanguageChanged();
        };

        RefreshShopName();
        CurrentView = _loginVm;
    }

    private void RefreshShopName()
    {
        string en = _settings.Get("shop_name_en", "My Sanitary Store");
        string ur = _settings.Get("shop_name_ur", "");
        ShopNameText = string.IsNullOrWhiteSpace(ur) ? en : $"{en}  •  {ur}";
    }

    [RelayCommand]
    private void SetLanguage(string lang) => Loc.SetLanguage(lang);

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        IsLoggedIn = false;
        CurrentUserText = "";
        CurrentView = _loginVm;
    }

    [RelayCommand]
    private void Navigate(string target)
    {
        if (!IsLoggedIn) return;
        CurrentView = target switch
        {
            "Dashboard" => new DashboardViewModel(Loc, _sales, _customers, _products, _session),
            "Billing" => new BillingViewModel(Loc, _products, _customers, _sales, _settings, _session),
            "Quotation" => new QuotationsViewModel(Loc, _products, _customers, _quotations, _settings, _session),
            "Products" => new ProductsViewModel(Loc, _products, _session),
            "Customers" => new CustomersViewModel(Loc, _customers, _payments, _session),
            "Settings" => new SettingsViewModel(Loc, _settings, _db, this),
            "Purchase" => new PlaceholderViewModel(Loc, S("NavPurchase"), S("Phase2")),
            "Suppliers" => new PlaceholderViewModel(Loc, S("NavSuppliers"), S("Phase2")),
            "Returns" => new PlaceholderViewModel(Loc, S("NavReturns"), S("Phase2")),
            "Reports" => new PlaceholderViewModel(Loc, S("NavReports"), S("Phase2")),
            "Users" => new PlaceholderViewModel(Loc, S("NavUsers"), S("Phase3")),
            _ => CurrentView
        };
        // When Settings saves a new shop name, header must refresh: SettingsViewModel
        // calls back into RefreshShopNamePublic after saving.
    }

    public void RefreshShopNamePublic() => RefreshShopName();
}

/// <summary>Placeholder for Phase 2/3 screens (navigation stub only).</summary>
public partial class PlaceholderViewModel : ViewModelBase
{
    public string Title { get; }
    public string Phase { get; }

    public PlaceholderViewModel(LocalizationService loc, string title, string phase) : base(loc)
    {
        Title = title;
        Phase = phase;
    }
}
