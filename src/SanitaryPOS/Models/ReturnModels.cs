using CommunityToolkit.Mvvm.ComponentModel;

namespace SanitaryPOS.Models;

// Property names match schema.sql column names (returns / return_items).
// Class is called SaleReturn because the table is named "returns" and a plain
// class named "Return" would read badly in C# code.

public class SaleReturn
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long? CustomerId { get; set; }
    public long TotalRefundPaisa { get; set; }
    public string SettleMethod { get; set; } = "Cash";   // Cash | Khata | Exchange
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";

    // Display helpers (joined)
    public long BillNo { get; set; }
    public string? CustomerName { get; set; }
}

public class SaleReturnItem
{
    public long Id { get; set; }
    public long ReturnId { get; set; }
    public long ProductId { get; set; }
    public double Qty { get; set; }
    public long RatePaisa { get; set; }                  // effective per-unit refund rate (after line discount)
    public long IsDamaged { get; set; }                  // 1 = damaged_qty only, 0 = back to sellable stock
    public long LineTotalPaisa { get; set; }

    // Display helper (joined)
    public string? ProductName { get; set; }
}

/// <summary>
/// One editable line on the Returns screen, built from an original sale item.
/// ReturnQty is what the cashier types; it can never exceed MaxReturnQty
/// (sold qty − qty already returned on earlier returns for the same bill).
/// Refund uses the EFFECTIVE unit rate: the original line total after its
/// line discount, divided by the sold qty — so a discounted item is not
/// refunded at full price. Bill-level discount is not split over lines
/// (documented in PHASE2.md).
/// </summary>
public partial class ReturnLine : ObservableObject
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public double SoldQty { get; set; }
    public double AlreadyReturnedQty { get; set; }
    public long EffectiveRatePaisa { get; set; }

    public double MaxReturnQty => SoldQty - AlreadyReturnedQty;

    [ObservableProperty]
    private double _returnQty;

    /// <summary>"Good" = back to sellable stock, "Damaged" = damaged_qty only.</summary>
    [ObservableProperty]
    private string _condition = "Good";

    public bool IsDamaged => Condition == "Damaged";

    public long RefundPaisa => (long)Math.Round(ReturnQty * EffectiveRatePaisa);

    partial void OnReturnQtyChanged(double value) => OnPropertyChanged(nameof(RefundPaisa));
    partial void OnConditionChanged(string value) => OnPropertyChanged(nameof(IsDamaged));

    public SaleReturnItem ToReturnItem() => new()
    {
        ProductId = ProductId,
        Qty = ReturnQty,
        RatePaisa = EffectiveRatePaisa,
        IsDamaged = IsDamaged ? 1 : 0,
        LineTotalPaisa = RefundPaisa
    };
}
