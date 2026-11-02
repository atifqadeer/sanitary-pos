using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Phase 3 — Users &amp; Roles screen. ADMIN ONLY: the sidebar button is
/// hidden for Cashier and MainViewModel refuses to build this screen for a
/// Cashier; the commands below ALSO guard on IsAdmin (defence in depth,
/// same pattern as the Phase 2 profit report).
/// Sections: user list, edit selected (name/role, activate/deactivate,
/// admin password reset), create new user, and Change MY Password —
/// every password box on this screen is a PasswordBox in the view, copied
/// into these properties by code-behind (the Login screen pattern).
/// The service throws localization KEYS for guard failures; Error() below
/// translates them (Loc.Get returns the text itself when it is not a key,
/// so plain exception messages also survive).
/// </summary>
public partial class UsersViewModel : ViewModelBase
{
    private readonly UserService _users;
    private readonly SessionService _session;
    private readonly MainViewModel _main;

    public ObservableCollection<User> Users { get; } = new();
    public string[] Roles { get; } = { "Admin", "Cashier" };

    public bool IsAdmin => _session.IsAdmin;

    [ObservableProperty] private User? _selectedUser;

    // ----- edit selected -----
    [ObservableProperty] private string _editFullName = "";
    [ObservableProperty] private string _editRole = "Cashier";
    [ObservableProperty] private string _resetPassword = "";

    // ----- new user -----
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newFullName = "";
    [ObservableProperty] private string _newRole = "Cashier";

    // ----- change my password -----
    [ObservableProperty] private string _myCurrentPassword = "";
    [ObservableProperty] private string _myNewPassword = "";
    [ObservableProperty] private string _myConfirmPassword = "";

    public string ToggleActiveText => SelectedUser?.IsActive == 1 ? S("Deactivate") : S("Activate");

    public UsersViewModel(LocalizationService loc, UserService users,
                          SessionService session, MainViewModel main)
        : base(loc)
    {
        _users = users; _session = session; _main = main;
        LoadUsers();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(ToggleActiveText));
    }

    private void Error(Exception ex) => Warn(S(ex.Message));

    partial void OnSelectedUserChanged(User? value)
    {
        EditFullName = value?.FullName ?? "";
        EditRole = value?.Role ?? "Cashier";
        ResetPassword = "";
        OnPropertyChanged(nameof(ToggleActiveText));
    }

    private void LoadUsers(long? keepSelectedId = null)
    {
        Users.Clear();
        foreach (var u in _users.GetAll()) Users.Add(u);
        SelectedUser = Users.FirstOrDefault(u => u.Id == keepSelectedId) ?? Users.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshUsers() => LoadUsers(SelectedUser?.Id);

    [RelayCommand]
    private void CreateUser()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword))
        { Warn(S("UsernameRequiredMsg")); return; }
        if (NewPassword.Length < 4) { Warn(S("PasswordTooShortMsg")); return; }
        try
        {
            long id = _users.CreateUser(NewUsername, NewPassword,
                                        string.IsNullOrWhiteSpace(NewFullName) ? NewUsername : NewFullName,
                                        NewRole);
            Info(S("UserSavedMsg"));
            NewUsername = ""; NewPassword = ""; NewFullName = ""; NewRole = "Cashier";
            LoadUsers(id);
        }
        catch (Exception ex) { Error(ex); }
    }

    [RelayCommand]
    private void SaveUserEdit()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (SelectedUser == null) return;
        try
        {
            _users.UpdateUser(SelectedUser.Id, EditFullName, EditRole);
            Info(S("UserSavedMsg"));
            LoadUsers(SelectedUser.Id);
        }
        catch (Exception ex) { Error(ex); }
    }

    [RelayCommand]
    private void ToggleActive()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (SelectedUser == null) return;
        try
        {
            bool target = SelectedUser.IsActive != 1;
            _users.SetActive(SelectedUser.Id, target, _session.CurrentUser?.Id ?? 0);
            LoadUsers(SelectedUser.Id);
        }
        catch (Exception ex) { Error(ex); }
    }

    [RelayCommand]
    private void ResetPasswordCmd()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (SelectedUser == null) return;
        if (ResetPassword.Length < 4) { Warn(S("PasswordTooShortMsg")); return; }
        _users.ResetPassword(SelectedUser.Id, ResetPassword);
        ResetPassword = "";
        Info(S("PasswordChangedMsg"));
    }

    /// <summary>
    /// Change the LOGGED-IN user's own password (current password verified
    /// in UserService). The same flow exists on the Settings screen so a
    /// Cashier — who never sees this screen — can change theirs too.
    /// </summary>
    [RelayCommand]
    private void ChangeMyPassword()
    {
        var me = _session.CurrentUser;
        if (me == null) return;
        if (MyNewPassword.Length < 4) { Warn(S("PasswordTooShortMsg")); return; }
        if (MyNewPassword != MyConfirmPassword) { Warn(S("PasswordMismatchMsg")); return; }
        if (!_users.ChangeOwnPassword(me.Id, MyCurrentPassword, MyNewPassword))
        { Warn(S("WrongCurrentPasswordMsg")); return; }
        MyCurrentPassword = ""; MyNewPassword = ""; MyConfirmPassword = "";
        Info(S("PasswordChangedMsg"));
        // If this was the seeded admin leaving admin123 behind, the shell
        // warning banner can go away right now (no restart needed).
        _main.RefreshPasswordWarningPublic();
    }
}
