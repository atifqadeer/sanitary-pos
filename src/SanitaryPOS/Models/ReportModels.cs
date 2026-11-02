namespace SanitaryPOS.Models;

// Small typed rows returned by ReportService. The Reports screen turns them
// into generic ReportLine rows for the on-screen grid, printing and CSV —
// one display path for every report keeps the code simple.

public class SalesByDateRow
{
    public string DateDb { get; set; } = "";             // yyyy-MM-dd from SQL
    public int BillCount { get; set; }
    public long TotalPaisa { get; set; }
}

public class SalesByMethodRow
{
    public string PaymentMethod { get; set; } = "";
    public int BillCount { get; set; }
    public long TotalPaisa { get; set; }
}

public class ProfitByDateRow
{
    public string DateDb { get; set; } = "";
    public long SalesPaisa { get; set; }                 // sum of line totals (after line discounts)
    public long CostPaisa { get; set; }                  // sum of CURRENT purchase rate x qty — ESTIMATED
    public long ProfitPaisa => SalesPaisa - CostPaisa;
}

public class GroupSalesRow
{
    public string GroupName { get; set; } = "";          // brand name or category name
    public double Qty { get; set; }
    public long AmountPaisa { get; set; }                // sum of line totals
}

public class StockValueRow
{
    public string ProductName { get; set; } = "";
    public string? BrandName { get; set; }
    public double Qty { get; set; }
    public long PurchaseValuePaisa { get; set; }         // qty x purchase rate
    public long RetailValuePaisa { get; set; }           // qty x retail rate
}

/// <summary>
/// One generic display row on the Reports screen (grid + print + CSV all use
/// these five text columns; unused columns stay empty).
/// </summary>
public class ReportLine
{
    public string C1 { get; set; } = "";
    public string C2 { get; set; } = "";
    public string C3 { get; set; } = "";
    public string C4 { get; set; } = "";
    public string C5 { get; set; } = "";
}
