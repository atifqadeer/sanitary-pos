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

// SettingsView moved to its own SettingsView.xaml.cs in Phase 3
// (it now needs Loaded logic + PasswordBox handlers).

// Phase 2 views with no code-behind logic of their own.

public partial class SuppliersView : UserControl
{
    public SuppliersView() => InitializeComponent();
}

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();
}

// Phase 3 view with no code-behind logic of its own.

public partial class DayEndView : UserControl
{
    public DayEndView() => InitializeComponent();
}
