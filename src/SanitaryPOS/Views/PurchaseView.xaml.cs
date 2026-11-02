using System.Windows.Controls;
using System.Windows.Input;
using SanitaryPOS.Models;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

public partial class PurchaseView : UserControl
{
    public PurchaseView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private PurchaseViewModel? Vm => DataContext as PurchaseViewModel;

    /// <summary>Same as Billing: barcode scanners type + Enter, or Enter after a typed name.</summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.OnSearchEnter();
        SearchBox.Focus();
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
