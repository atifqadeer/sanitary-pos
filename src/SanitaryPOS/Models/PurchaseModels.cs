using CommunityToolkit.Mvvm.ComponentModel;

namespace SanitaryPOS.Models;

// Property names match schema.sql column names (purchases / purchase_items).

public class Purchase
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public string? SupplierBillNo { get; set; }         // the bill number printed on the supplier's own bill
    public long SubtotalPaisa { get; set; }
    public long DiscountPaisa { get; set; }
    public long TotalPaisa { get; set; }
    public long PaidPaisa { get; set; }                 // paid at GRN time (also written as a payments OUT row — see PurchaseService)
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";

    // Display helper (joined)
    public string? SupplierName { get; set; }
}

public class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseId { get; set; }
    public long ProductId { get; set; }
    public double Qty { get; set; }
    public long PurchaseRatePaisa { get; set; }
    public long LineTotalPaisa { get; set; }

    // Display helper (joined)
    public string? ProductName { get; set; }
}

/// <summary>
/// One editable line on the Purchase (GRN) screen. Mirrors BillLine on the
/// sales side, but simpler: purchase lines have no per-line discount —
/// discount is given once on the whole supplier bill.
/// </summary>
public partial class PurchaseLine : ObservableObject
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public double AvailableStock { get; set; }

    [ObservableProperty]
    private double _qty = 1;

    [ObservableProperty]
    private long _purchaseRatePaisa;

    public long LineTotalPaisa => (long)Math.Round(Qty * PurchaseRatePaisa);

    /// <summary>Rate shown/edited in the grid as rupees.</summary>
    public double RateRupees
    {
        get => PurchaseRatePaisa / 100.0;
        set => PurchaseRatePaisa = (long)Math.Round(value * 100.0);
    }

    partial void OnQtyChanged(double value) => NotifyTotals();
    partial void OnPurchaseRatePaisaChanged(long value) => NotifyTotals();

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(LineTotalPaisa));
        OnPropertyChanged(nameof(RateRupees));
    }

    public PurchaseItem ToPurchaseItem() => new()
    {
        ProductId = ProductId,
        Qty = Qty,
        PurchaseRatePaisa = PurchaseRatePaisa,
        LineTotalPaisa = LineTotalPaisa
    };

    public static PurchaseLine FromProduct(Product p) => new()
    {
        ProductId = p.Id,
        ProductName = p.Name,
        AvailableStock = p.StockQty,
        Qty = 1,
        // A GRN normally comes in at the last known purchase rate; cashier can edit it.
        PurchaseRatePaisa = p.PurchaseRatePaisa
    };
}
