using System.Windows;
using System.Windows.Controls;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Purchase rate is an Admin secret: hide the grid column AND the form
        // field when a Cashier is logged in. (Data still exists in the model —
        // this is a shop-counter rule, not a security boundary. See PHASE1.md.)
        if (DataContext is ProductsViewModel vm && !vm.IsAdmin)
        {
            PurchaseColumn.Visibility = Visibility.Collapsed;
            PurchaseRatePanel.Visibility = Visibility.Collapsed;
        }
    }
}
