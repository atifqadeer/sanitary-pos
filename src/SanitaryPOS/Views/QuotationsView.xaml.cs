using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SanitaryPOS.Models;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

public partial class QuotationsView : UserControl
{
    public QuotationsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private QuotationsViewModel? Vm => DataContext as QuotationsViewModel;

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
