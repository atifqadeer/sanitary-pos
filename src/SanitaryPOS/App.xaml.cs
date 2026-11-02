using System.Windows;
using SanitaryPOS.Data;
using SanitaryPOS.Localization;
using SanitaryPOS.Services;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS;

/// <summary>
/// App start: create/open the SQLite database, build the simple service set,
/// apply the saved default language, then show the main window (login first).
/// No dependency-injection container in Phase 1 — services are plain objects
/// passed to MainViewModel. Simple over complex.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var db = new DatabaseService();
            db.Initialize(); // creates sanitarypos.db + schema + admin user on first run

            var settings = new SettingsService(db);
            var session = new SessionService();
            var localization = new LocalizationService();

            var stock = new StockService(db);
            var products = new ProductService(db, stock);
            var customers = new CustomerService(db);
            var payments = new PaymentService(db);
            var sales = new SaleService(db, stock);
            var quotations = new QuotationService(db, stock);

            // Language saved in Settings (default_language). Fall back to Urdu,
            // because most counter staff read Urdu better than English.
            string lang = settings.Get("default_language", "ur");
            localization.SetLanguage(lang == "en" ? "en" : "ur");

            var mainVm = new MainViewModel(db, settings, session, localization,
                                           products, customers, payments, sales, quotations);
            var window = new MainWindow { DataContext = mainVm };
            localization.AttachMainWindow(window);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Database could not be opened.\n" + ex.Message,
                            "Sanitary POS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
