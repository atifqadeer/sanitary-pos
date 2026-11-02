using System.Windows;
using System.Windows.Controls;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

/// <summary>
/// Settings code-behind (moved out of SimpleViews.cs in Phase 3):
/// 1. Role rule — the whole backup area (folder, Backup Now, auto-backup)
///    is Admin-only, so the panel is collapsed for a Cashier; the shop
///    info above it stays visible. Same pattern as ProductsView hiding
///    purchase rates. BackupNow itself stays harmless if ever reached:
///    it only copies the database to the configured folder.
/// 2. PasswordBoxes cannot bind — the three "Change MY Password" boxes
///    copy their text into the ViewModel, exactly like LoginView does.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && !vm.IsAdmin)
            BackupPanel.Visibility = Visibility.Collapsed;
    }

    private void MyCurrentBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.MyCurrentPassword = MyCurrentBox.Password;
    }

    private void MyNewBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.MyNewPassword = MyNewBox.Password;
    }

    private void MyConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.MyConfirmPassword = MyConfirmBox.Password;
    }
}
