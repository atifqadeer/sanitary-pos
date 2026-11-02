using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>
/// Phase 3 — Day-End Closing figures. Everything is derived live from the
/// same tables the rest of the app writes (sales, payments, returns) for
/// ONE local date ('yyyy-MM-dd', compared with date(created_at) like the
/// Phase 2 reports). The only thing stored is the DayClosing row itself.
///
/// ============ EXPECTED CASH FORMULA (read before changing) ============
///   Expected Cash
///     = cash taken at bill time on Completed bills whose payment_method
///       is 'Cash'                       (sales.paid_paisa, Cash rows only)
///     + cash receipts from customers     (payments IN, method 'Cash',
///                                         EXCLUDING rows whose note starts
///                                         "Return adjustment" — Phase 2
///                                         writes khata-settled returns as
///                                         IN/'Cash' payments, but no real
///                                         cash moved on those)
///     − cash given to suppliers          (payments OUT, method 'Cash' —
///                                         includes a GRN's paid-now row)
///     − cash refunds                     (returns settled with
///                                         settle_method 'Cash')
///
/// What is deliberately NOT in Expected Cash, and why:
/// - 'Mixed' bills: paid_paisa is one lump with no recorded split between
///   cash and other methods, so it cannot be counted exactly. The amount
///   is surfaced separately (DaySummary.MixedPaidPaisa) and named on
///   screen + Z-report as NOT counted — the counter settles it by hand.
/// - 'Credit' bills: paid is normally 0 (udhaar); any part-payment typed
///   on a Credit bill is also not split by method — same treatment.
/// - Card/Bank/JazzCash/EasyPaisa money never enters the drawer.
/// - There is NO opening float in this system: Expected Cash is the day's
///   cash MOVEMENT. If the shop keeps a fixed float in the drawer, the
///   counted cash — and therefore the difference — includes that float.
///   This is printed on the screen and in PHASE3.md, not hidden.
/// ======================================================================
/// </summary>
public class DayEndService
{
    private readonly DatabaseService _db;
    public DayEndService(DatabaseService db) => _db = db;

    public DaySummary GetSummary(string dateDb)
    {
        using var conn = _db.CreateConnection();
        var s = new DaySummary { DateDb = dateDb };

        s.MethodRows = conn.Query<DayMethodRow>(
            @"SELECT payment_method AS payment_method, COUNT(*) AS bill_count,
                     COALESCE(SUM(total_paisa), 0) AS total_paisa,
                     COALESCE(SUM(paid_paisa), 0) AS paid_paisa
              FROM sales
              WHERE status = 'Completed' AND date(created_at) = date(@d)
              GROUP BY payment_method ORDER BY total_paisa DESC",
            new { d = dateDb }).ToList();
        // Keep the Phase 2 row shape filled too (Reports reuse).
        s.SalesByMethod = s.MethodRows.Select(m => new SalesByMethodRow
        {
            PaymentMethod = m.PaymentMethod,
            BillCount = m.BillCount,
            TotalPaisa = m.TotalPaisa
        }).ToList();

        s.BillCount = s.MethodRows.Sum(m => m.BillCount);
        s.TotalSalesPaisa = s.MethodRows.Sum(m => m.TotalPaisa);
        s.CashSalesPaidPaisa = s.MethodRows
            .Where(m => m.PaymentMethod == "Cash").Sum(m => m.PaidPaisa);
        s.MixedPaidPaisa = s.MethodRows
            .Where(m => m.PaymentMethod == "Mixed").Sum(m => m.PaidPaisa);

        // Real customer receipts only: return-adjustment rows (khata-settled
        // returns) are bookkeeping, not money received — see formula above.
        const string receiptFilter =
            @"party_type = 'Customer' AND direction = 'IN'
              AND date(created_at) = date(@d)
              AND (note IS NULL OR note NOT LIKE 'Return adjustment%')";
        s.CustomerReceiptsPaisa = conn.ExecuteScalar<long>(
            $"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments WHERE {receiptFilter}",
            new { d = dateDb });
        s.CustomerCashReceiptsPaisa = conn.ExecuteScalar<long>(
            $"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments WHERE {receiptFilter} AND payment_method = 'Cash'",
            new { d = dateDb });

        const string supplierFilter =
            "party_type = 'Supplier' AND direction = 'OUT' AND date(created_at) = date(@d)";
        s.SupplierPaymentsPaisa = conn.ExecuteScalar<long>(
            $"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments WHERE {supplierFilter}",
            new { d = dateDb });
        s.SupplierCashPaymentsPaisa = conn.ExecuteScalar<long>(
            $"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments WHERE {supplierFilter} AND payment_method = 'Cash'",
            new { d = dateDb });

        s.ReturnsTotalPaisa = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(total_refund_paisa), 0) FROM returns
              WHERE date(created_at) = date(@d)", new { d = dateDb });
        s.CashRefundsPaisa = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(total_refund_paisa), 0) FROM returns
              WHERE date(created_at) = date(@d) AND settle_method = 'Cash'", new { d = dateDb });

        s.ExpectedCashPaisa = s.CashSalesPaidPaisa
                            + s.CustomerCashReceiptsPaisa
                            - s.SupplierCashPaymentsPaisa
                            - s.CashRefundsPaisa;
        return s;
    }

    public DayClosing? GetClosing(string dateDb)
    {
        using var conn = _db.CreateConnection();
        return conn.QueryFirstOrDefault<DayClosing>(
            @"SELECT c.*, u.full_name AS closed_by_name FROM day_closings c
              LEFT JOIN users u ON u.id = c.user_id
              WHERE c.closing_date = @d", new { d = dateDb });
    }

    /// <summary>
    /// Save the closing for a date. closing_date is UNIQUE — closing the
    /// same date twice is refused (the screen shows the saved record
    /// instead of the Close button in that case).
    /// </summary>
    public DayClosing SaveClosing(DayClosing closing)
    {
        if (GetClosing(closing.ClosingDate) != null)
            throw new InvalidOperationException("AlreadyClosedMsg");
        closing.DifferencePaisa = closing.CountedPaisa - closing.ExpectedPaisa;
        using var conn = _db.CreateConnection();
        closing.Id = conn.ExecuteScalar<long>(
            @"INSERT INTO day_closings (closing_date, expected_paisa, counted_paisa,
                                        difference_paisa, note, user_id)
              VALUES (@ClosingDate, @ExpectedPaisa, @CountedPaisa,
                      @DifferencePaisa, @Note, @UserId);
              SELECT last_insert_rowid();", closing);
        return GetClosing(closing.ClosingDate) ?? closing;
    }

    public List<DayClosing> GetHistory()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<DayClosing>(
            @"SELECT c.*, u.full_name AS closed_by_name FROM day_closings c
              LEFT JOIN users u ON u.id = c.user_id
              ORDER BY c.closing_date DESC").ToList();
    }
}
