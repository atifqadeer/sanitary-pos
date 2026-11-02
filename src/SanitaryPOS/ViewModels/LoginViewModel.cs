using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Data;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly SessionService _session;

    /// <summary>Raised after a successful login; MainViewModel listens and shows the shell.</summary>
    public event Action<User>? LoggedIn;

    [ObservableProperty] private string _username = "admin";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _errorText = "";

    public LoginViewModel(LocalizationService loc, DatabaseService db, SessionService session)
        : base(loc)
    {
        _db = db;
        _session = session;
    }

    [RelayCommand]
    private void Login()
    {
        ErrorText = "";
        var user = _db.Authenticate(Username, Password);
        if (user == null)
        {
            ErrorText = S("LoginFailed");
            return;
        }
        _session.SignIn(user);
        Password = "";
        LoggedIn?.Invoke(user);
    }
}
