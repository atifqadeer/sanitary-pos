using SanitaryPOS.Helpers;

namespace SanitaryPOS.Models;

// Phase 3 — Day-End Closing models.
// Property names match the day_closings table columns (see schema.sql /
// the CREATE TABLE IF NOT EXISTS migration in DatabaseService).

/// <summary>One saved day closing (one row per closed date, closing_date is UNIQUE).</summary>
public class DayClosing
{
    public long Id { get; set; }
    public string ClosingDate { get; set; } = "";       // 'yyyy-MM-dd' (local shop date)
    public long ExpectedPaisa { get; set; }             // expected cash at closing time (DayEndService formula)
    public long CountedPaisa { get; set; }              // cash physically counted in the drawer
    public long DifferencePaisa { get; set; }           // counted - expected
    public string? Note { get; set; }
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";

    // Display helpers (filled by DayEndService queries)
    public string? ClosedByName { get; set; }
    public string DateText => DateHelper.FormatDbDate(ClosingDate + " 00:00:00");
    public string CreatedText => DateHelper.FormatDateTime(DateHelper.ParseDb(CreatedAt));
}

/// <summary>
/// One day's figures, all re-derived live from sales / payments / returns
/// for the chosen date (DayEndService.GetSummary). Nothing here is stored —
/// the ONLY stored day-end row is DayClosing (what was counted + expected
/// at the moment of closing).
/// </summary>
public class DaySummary
{
    public string DateDb { get; set; } = "";            // 'yyyy-MM-dd'

    // Sales side
    public long TotalSalesPaisa { get; set; }           // SUM(total) of Completed bills
    public int BillCount { get; set; }
    public List<SalesByMethodRow> SalesByMethod { get; set; } = new();
    /// <summary>Paid-at-bill-time total per method is kept alongside: see Extended row below.</summary>
    public List<DayMethodRow> MethodRows { get; set; } = new();

    // Money in / out during the day (payments table)
    public long CustomerReceiptsPaisa { get; set; }     // payments IN (real receipts only — return adjustments excluded)
    public long CustomerCashReceiptsPaisa { get; set; } // ... of which Cash
    public long SupplierPaymentsPaisa { get; set; }     // payments OUT (incl. GRN paid-now rows)
    public long SupplierCashPaymentsPaisa { get; set; } // ... of which Cash

    // Returns
    public long ReturnsTotalPaisa { get; set; }         // all refunds settled that day
    public long CashRefundsPaisa { get; set; }          // ... of which settled in Cash

    // Cash expectation (the documented formula — see DayEndService)
    public long CashSalesPaidPaisa { get; set; }        // paid at bill time on Cash-method Completed bills
    public long MixedPaidPaisa { get; set; }            // paid on Mixed bills — NOT in expected cash (split unknown)
    public long ExpectedCashPaisa { get; set; }
}

/// <summary>Sales grouped by payment method for one day, with BOTH the bill
/// total and the amount actually paid at bill time (they differ on udhaar).</summary>
public class DayMethodRow
{
    public string PaymentMethod { get; set; } = "";
    public int BillCount { get; set; }
    public long TotalPaisa { get; set; }
    public long PaidPaisa { get; set; }
}
