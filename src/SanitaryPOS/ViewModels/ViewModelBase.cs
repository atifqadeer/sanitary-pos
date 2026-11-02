using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SanitaryPOS.Localization;

namespace SanitaryPOS.ViewModels;

/// <summary>Common helpers for all ViewModels: localized strings + simple message boxes.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected LocalizationService Loc { get; }

    protected ViewModelBase(LocalizationService loc) => Loc = loc;

    protected string S(string key) => Loc.Get(key);

    protected void Info(string message)
        => MessageBox.Show(message, S("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    protected void Warn(string message)
        => MessageBox.Show(message, S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Call after the language changes so computed display properties refresh.</summary>
    public virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}
