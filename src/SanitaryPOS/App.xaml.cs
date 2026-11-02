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
/// Phase 3: Initialize() also applies the startup migration (day_closings +
/// settings defaults); the AutoBackupService timer starts once the window
/// is up, and OnExit runs one catch-up backup if the day has none.
/// </summary>
public partial class App : Application
{
    private AutoBackupService? _autoBackup;

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

            // Phase 2 services
            var suppliers = new SupplierService(db);
            var purchases = new PurchaseService(db, stock);
            var returns = new ReturnService(db, stock);
            var reports = new ReportService(db);

            // Phase 3 services
            var users = new UserService(db);
            var dayEnd = new DayEndService(db);
            _autoBackup = new AutoBackupService(db, settings);

            // Language saved in Settings (default_language). Fall back to Urdu,
            // because most counter staff read Urdu better than English.
            string lang = settings.Get("default_language", "ur");
            localization.SetLanguage(lang == "en" ? "en" : "ur");

            var mainVm = new MainViewModel(db, settings, session, localization,
                                           products, customers, payments, sales, quotations,
                                           suppliers, purchases, returns, reports,
                                           users, dayEnd);
            var window = new MainWindow { DataContext = mainVm };
            localization.AttachMainWindow(window);
            window.Show();

            // Start the scheduled auto-backup (immediate catch-up check +
            // a check every minute). Its failures are swallowed inside.
            _autoBackup.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Database could not be opened.\n" + ex.Message,
                            "Sanitary POS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // One last chance for today's scheduled backup (e.g. the shop closed
        // before the set backup time). No-op when auto-backup is off or the
        // day already has its copy.
        try { _autoBackup?.RunExitCatchUp(); } catch { /* never block exit */ }
        base.OnExit(e);
    }
}
