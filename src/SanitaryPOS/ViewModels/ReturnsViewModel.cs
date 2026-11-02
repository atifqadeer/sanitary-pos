using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>Carries an exchange credit from Returns to the Billing screen.</summary>
public class BillingPrefill
{
    public Customer? Customer { get; set; }
    public long CreditPaisa { get; set; }
    public long BillNo { get; set; }
}

/// <summary>
/// Returns &amp; Exchange screen.
/// Type a bill number → the original sale loads → type the return qty per item
/// (screen blocks more than sold − already returned), mark each line Good or
/// Damaged, choose how the refund is settled, Save.
/// Exchange is deliberately simple (see PHASE2.md): the return is saved, then
/// the ExchangeReady event asks the shell to open Billing for the same customer
/// with the credit amount shown as a status message — the new bill is a normal
/// bill, and the counter settles the credit against it by hand.
/// </summary>
public partial class ReturnsViewModel : ViewModelBase
{
    private readonly ReturnService _returns;
    private readonly CustomerService _customers;
    private readonly SessionService _session;

    public ObservableCollection<ReturnLine> Lines { get; } = new();
    public ObservableCollection<SaleReturn> History { get; } = new();

    public string[] Conditions { get; } = { "Good", "Damaged" };
    public string[] SettleMethods { get; } = { "Cash", "Khata", "Exchange" };

    [ObservableProperty] private string _billNoText = "";
    [ObservableProperty] private string _saleInfoText = "";
    [ObservableProperty] private string _selectedSettleMethod = "Cash";

    private Sale? _loadedSale;

    /// <summary>Raised after an Exchange return is saved; the shell opens Billing with this prefill.</summary>
    public event Action<BillingPrefill>? ExchangeReady;

    public long RefundTotalPaisa => Lines.Sum(l => l.RefundPaisa);
    public string RefundTotalText => MoneyFormatter.Format(RefundTotalPaisa);
    public bool HasLoadedSale => _loadedSale != null;

    public ReturnsViewModel(LocalizationService loc, ReturnService returns,
                            CustomerService customers, SessionService session)
        : base(loc)
    {
        _returns = returns; _customers = customers; _session = session;
        Lines.CollectionChanged += (_, _) => HookLines();
        LoadHistory();
    }

    private void HookLines()
    {
        foreach (var l in Lines)
        {
            l.PropertyChanged -= LineChanged;
            l.PropertyChanged += LineChanged;
        }
        RefreshRefund();
    }

    private void LineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshRefund();

    private void RefreshRefund()
    {
        OnPropertyChanged(nameof(RefundTotalPaisa));
        OnPropertyChanged(nameof(RefundTotalText));
    }

    [RelayCommand]
    private void LoadBill()
    {
        Lines.Clear();
        _loadedSale = null;
        SaleInfoText = "";
        OnPropertyChanged(nameof(HasLoadedSale));

        if (!long.TryParse(BillNoText.Trim(), out long billNo))
        {
            Warn(S("BillNotFoundMsg"));
            return;
        }

        var detail = _returns.GetSaleByBillNo(billNo);
        if (detail == null)
        {
            Warn(S("BillNotFoundMsg"));
            return;
        }

        _loadedSale = detail.Sale;
        string customer = detail.Sale.CustomerName ?? S("WalkIn");
        SaleInfoText = $"#{detail.Sale.BillNo} — {customer} — {DateHelper.FormatDbDate(detail.Sale.CreatedAt)} — {MoneyFormatter.Format(detail.Sale.TotalPaisa)}";

        foreach (var item in detail.Items)
        {
            double already = _returns.GetAlreadyReturnedQty(detail.Sale.Id, item.ProductId);
            // Effective refund rate: what the customer actually paid per piece
            // after the line discount — never the full shelf rate.
            long effectiveRate = item.Qty > 0
                ? (long)Math.Round(item.LineTotalPaisa / item.Qty)
                : item.RatePaisa;
            Lines.Add(new ReturnLine
            {
                ProductId = item.ProductId,
                ProductName = item.ProductNameSnapshot,
                SoldQty = item.Qty,
                AlreadyReturnedQty = already,
                EffectiveRatePaisa = effectiveRate,
                ReturnQty = 0,
                Condition = "Good"
            });
        }
        HookLines();
        OnPropertyChanged(nameof(HasLoadedSale));
    }

    [RelayCommand]
    private void SaveReturn()
    {
        if (_loadedSale == null) { Warn(S("LoadBillFirstMsg")); return; }

        foreach (var l in Lines)
        {
            if (l.ReturnQty < 0 || l.ReturnQty > l.MaxReturnQty + 0.0001)
            {
                Warn(S("ReturnQtyErrorMsg"));
                return;
            }
        }
        if (Lines.All(l => l.ReturnQty <= 0)) { Warn(S("ReturnQtyErrorMsg")); return; }

        ReturnResult result;
        try
        {
            result = _returns.SaveReturn(
                _loadedSale,
                Lines.Where(l => l.ReturnQty > 0).Select(l => l.ToReturnItem()).ToList(),
                SelectedSettleMethod,
                _session.CurrentUser?.Id);
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            return;
        }

        long billNo = _loadedSale.BillNo;
        Info($"{S("ReturnSavedMsg")} — {MoneyFormatter.Format(result.RefundPaisa)}");
        LoadHistory();

        if (result.SettleMethod == "Exchange")
        {
            // Keep it simple: hand the credit to Billing. The return stays
            // loaded here until the shell replaces this screen.
            Customer? customer = result.CustomerId.HasValue
                ? _customers.GetById(result.CustomerId.Value)
                : null;
            ExchangeReady?.Invoke(new BillingPrefill
            {
                Customer = customer,
                CreditPaisa = result.RefundPaisa,
                BillNo = billNo
            });
            return;
        }

        // Reset for the next return.
        BillNoText = "";
        Lines.Clear();
        _loadedSale = null;
        SaleInfoText = "";
        OnPropertyChanged(nameof(HasLoadedSale));
        RefreshRefund();
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var r in _returns.GetHistory()) History.Add(r);
    }
}
