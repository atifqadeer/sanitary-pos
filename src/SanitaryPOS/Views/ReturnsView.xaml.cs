using System.Windows.Controls;
using System.Windows.Input;
using SanitaryPOS.ViewModels;

namespace SanitaryPOS.Views;

public partial class ReturnsView : UserControl
{
    public ReturnsView()
    {
        InitializeComponent();
        Loaded += (_, _) => BillNoBox.Focus();
    }

    private ReturnsViewModel? Vm => DataContext as ReturnsViewModel;

    /// <summary>Enter in the bill-number box loads the bill — same keyboard habit as barcode entry.</summary>
    private void BillNoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.LoadBillCommand.Execute(null);
        e.Handled = true;
    }
}
