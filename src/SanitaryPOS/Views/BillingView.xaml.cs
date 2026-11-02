using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SanitaryPOS.Models;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

public partial class BillingView : UserControl
{
    public BillingView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private BillingViewModel? Vm => DataContext as BillingViewModel;

    /// <summary>Barcode scanners type the code and send Enter — same as a cashier pressing Enter after typing.</summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.OnSearchEnter();
        SearchBox.Focus(); // keep focus ready for the next scan
        e.Handled = true;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is Product p)
        {
            Vm?.AddProductCommand.Execute(p);
            SearchBox.Focus();
        }
    }
}
