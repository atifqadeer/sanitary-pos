using System.Windows;

namespace SanitaryPOS.Localization;

/// <summary>
/// Swaps the UI language instantly: replaces the Strings.* ResourceDictionary
/// in App resources (all views use DynamicResource, so they update live) and
/// flips the main window FlowDirection (LTR English / RTL Urdu) + font.
/// </summary>
public class LocalizationService
{
    private Window? _mainWindow;

    public string CurrentLanguage { get; private set; } = "ur";
    public bool IsUrdu => CurrentLanguage == "ur";

    public event EventHandler? LanguageChanged;

    public void AttachMainWindow(Window window)
    {
        _mainWindow = window;
        ApplyToWindow();
    }

    public void SetLanguage(string language)
    {
        CurrentLanguage = language == "en" ? "en" : "ur";

        var app = Application.Current;
        if (app != null)
        {
            var merged = app.Resources.MergedDictionaries;
            // Find and remove the old language dictionary (it is the one whose
            // Source contains "Strings.")
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                string src = merged[i].Source?.ToString() ?? "";
                if (src.Contains("Strings.")) merged.RemoveAt(i);
            }
            merged.Add(new ResourceDictionary
            {
                Source = new Uri($"Localization/Strings.{CurrentLanguage}.xaml", UriKind.Relative)
            });
        }

        ApplyToWindow();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Read one string from the current dictionary (for messages in code).</summary>
    public string Get(string key)
    {
        if (Application.Current != null &&
            Application.Current.TryFindResource(key) is string s)
            return s;
        return key;
    }

    private void ApplyToWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.FlowDirection = IsUrdu ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // Urdu: Noto Nastaliq Urdu first (install the free font on the shop PC),
        // fall back to Segoe UI which can still render Urdu script.
        _mainWindow.FontFamily = new System.Windows.Media.FontFamily(
            IsUrdu ? "Noto Nastaliq Urdu, Noto Naskh Arabic, Segoe UI" : "Segoe UI");
    }
}
