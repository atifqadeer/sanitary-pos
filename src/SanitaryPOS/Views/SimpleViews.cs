using System.Windows.Controls;

namespace SanitaryPOS.Views;

// Code-behind for the views that need no logic of their own.
// Each .xaml file has x:Class pointing here by file name — see the matching
// partial classes below (WPF requires one partial class per x:Class).

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
}

public partial class PlaceholderView : UserControl
{
    public PlaceholderView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
