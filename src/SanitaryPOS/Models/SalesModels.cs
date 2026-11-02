using CommunityToolkit.Mvvm.ComponentModel;

namespace SanitaryPOS.Models;

public class Sale
{
    public long Id { get; set; }
    public long BillNo { get; set; }
    public long? CustomerId { get; set; }
    public string PriceLevel { get; set; } = "Retail";    // Retail|Trade|Wholesale
    public long SubtotalPaisa { get; set; }
    public long ItemDiscountPaisa { get; set; }
    public long BillDiscountPaisa { get; set; }
    public long TaxPaisa { get; set; }                    // separate field: FBR-ready
    public long TotalPaisa { get; set; }
    public long PaidPaisa { get; set; }                   // paid at bill time; Total-Paid = udhaar
    public string PaymentMethod { get; set; } = "Cash";   // Cash|Card|Bank|JazzCash|EasyPaisa|Credit|Mixed
    public string Status { get; set; } = "Completed";     // Completed|Held|Cancelled
    public string? CancelReason { get; set; }
    public long? QuotationId { get; set; }
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";

    // Display helpers
    public string? CustomerName { get; set; }
    public long BalancePaisa => TotalPaisa - PaidPaisa;
}

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public double Qty { get; set; }
    public long RatePaisa { get; set; }
    public long DiscountPaisa { get; set; }
    public long LineTotalPaisa { get; set; }
}

public class Quotation
{
    public long Id { get; set; }
    public long QuotationNo { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerMobileSnapshot { get; set; }
    public string PriceLevel { get; set; } = "Retail";
    public long SubtotalPaisa { get; set; }
    public long DiscountPaisa { get; set; }
    public long TotalPaisa { get; set; }
    public long ValidDays { get; set; } = 7;
    public string Status { get; set; } = "Open";          // Open|Converted|Expired|Cancelled
    public long? ConvertedSaleId { get; set; }
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";

    public string DisplayCustomer => CustomerNameSnapshot ?? "";
}

public class QuotationItem
{
    public long Id { get; set; }
    public long QuotationId { get; set; }
    public long ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public double Qty { get; set; }
    public long RatePaisa { get; set; }
    public long DiscountPaisa { get; set; }
    public long LineTotalPaisa { get; set; }
}

/// <summary>
/// One editable line on the Billing and Quotation screens (shared logic).
/// Discount can be typed as a % or as Rs — the screen has a mode selector;
/// DiscountPaisa is always the final paisa value used in totals.
/// </summary>
public partial class BillLine : ObservableObject
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public double AvailableStock { get; set; }

    [ObservableProperty]
    private double _qty = 1;

    [ObservableProperty]
    private long _ratePaisa;

    /// <summary>"Percent" or "Rs"</summary>
    [ObservableProperty]
    private string _discountMode = "Rs";

    /// <summary>The number the user typed: a percent (e.g. 10) or rupees (e.g. 250).</summary>
    [ObservableProperty]
    private double _discountValue;

    public long GrossPaisa => (long)Math.Round(Qty * RatePaisa);

    public long DiscountPaisa
    {
        get
        {
            long d = DiscountMode == "Percent"
                ? (long)Math.Round(GrossPaisa * DiscountValue / 100.0)
                : (long)Math.Round(DiscountValue * 100.0);
            if (d < 0) d = 0;
            if (d > GrossPaisa) d = GrossPaisa;
            return d;
        }
    }

    public long LineTotalPaisa => GrossPaisa - DiscountPaisa;

    // Rate shown/edited in the grid as rupees (header already implies Rs in printouts;
    // on screen the column header is "Rate (Rs)").
    public double RateRupees
    {
        get => RatePaisa / 100.0;
        set => RatePaisa = (long)Math.Round(value * 100.0);
    }

    partial void OnQtyChanged(double value) => NotifyTotals();
    partial void OnRatePaisaChanged(long value) => NotifyTotals();
    partial void OnDiscountModeChanged(string value) => NotifyTotals();
    partial void OnDiscountValueChanged(double value) => NotifyTotals();

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(GrossPaisa));
        OnPropertyChanged(nameof(DiscountPaisa));
        OnPropertyChanged(nameof(LineTotalPaisa));
        OnPropertyChanged(nameof(RateRupees));
    }

    public SaleItem ToSaleItem() => new()
    {
        ProductId = ProductId,
        ProductNameSnapshot = ProductName,
        Qty = Qty,
        RatePaisa = RatePaisa,
        DiscountPaisa = DiscountPaisa,
        LineTotalPaisa = LineTotalPaisa
    };

    public QuotationItem ToQuotationItem() => new()
    {
        ProductId = ProductId,
        ProductNameSnapshot = ProductName,
        Qty = Qty,
        RatePaisa = RatePaisa,
        DiscountPaisa = DiscountPaisa,
        LineTotalPaisa = LineTotalPaisa
    };

    public static BillLine FromProduct(Product p, string priceLevel) => new()
    {
        ProductId = p.Id,
        ProductName = p.Name,
        AvailableStock = p.StockQty,
        Qty = 1,
        RatePaisa = p.RateFor(priceLevel)
    };
}
