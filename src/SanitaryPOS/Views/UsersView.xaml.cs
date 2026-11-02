using System.Windows;
using System.Windows.Controls;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

/// <summary>
/// Users screen code-behind: PasswordBox cannot bind, so each box copies
/// its text into the ViewModel on PasswordChanged — the exact pattern
/// LoginView established in Phase 1. Nothing else lives here.
/// </summary>
public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    private void ResetPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.ResetPassword = ResetPasswordBox.Password;
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.NewPassword = NewPasswordBox.Password;
    }

    private void MyCurrentBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.MyCurrentPassword = MyCurrentBox.Password;
    }

    private void MyNewBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.MyNewPassword = MyNewBox.Password;
    }

    private void MyConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.MyConfirmPassword = MyConfirmBox.Password;
    }
}
