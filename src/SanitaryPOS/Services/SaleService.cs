using Dapper;
using Microsoft.Data.Sqlite;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

public class SaleDetail
{
    public Sale Sale { get; set; } = new();
    public List<SaleItem> Items { get; set; } = new();
}

public class SaleService
{
    private readonly DatabaseService _db;
    private readonly StockService _stock;

    public SaleService(DatabaseService db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    private static long NextBillNo(SqliteConnection conn, SqliteTransaction tx)
        => conn.ExecuteScalar<long>("SELECT COALESCE(MAX(bill_no), 0) + 1 FROM sales", null, tx);

    /// <summary>
    /// Save a bill in ONE transaction: sales row + sale_items + (stock cut).
    /// Stock rule: stock is cut ONLY for Completed bills. A Held bill is just a
    /// parked record — no stock movement — and it is deleted when recalled, so
    /// stock can never be cut twice for the same goods.
    /// Udhaar rule: if Paid &lt; Total, the unpaid part stays as the customer's
    /// khata balance automatically (see CustomerService.ComputeBalance) — no
    /// extra row is written at bill time. Later receipts go in payments.
    /// </summary>
    public Sale SaveSale(Sale sale, IReadOnlyList<SaleItem> items)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        sale.BillNo = NextBillNo(conn, tx);
        sale.Id = conn.ExecuteScalar<long>(
            @"INSERT INTO sales (bill_no, customer_id, price_level, subtotal_paisa, item_discount_paisa,
                                 bill_discount_paisa, tax_paisa, total_paisa, paid_paisa, payment_method,
                                 status, quotation_id, user_id)
              VALUES (@BillNo, @CustomerId, @PriceLevel, @SubtotalPaisa, @ItemDiscountPaisa,
                      @BillDiscountPaisa, @TaxPaisa, @TotalPaisa, @PaidPaisa, @PaymentMethod,
                      @Status, @QuotationId, @UserId);
              SELECT last_insert_rowid();", sale, tx);

        foreach (var item in items)
        {
            item.SaleId = sale.Id;
            conn.Execute(
                @"INSERT INTO sale_items (sale_id, product_id, product_name_snapshot, qty,
                                          rate_paisa, discount_paisa, line_total_paisa)
                  VALUES (@SaleId, @ProductId, @ProductNameSnapshot, @Qty,
                          @RatePaisa, @DiscountPaisa, @LineTotalPaisa)", item, tx);

            if (sale.Status == "Completed")
            {
                _stock.ApplyMovement(conn, tx, item.ProductId, "Sale", -item.Qty,
                                     "Sale", sale.Id, sale.UserId, $"Bill #{sale.BillNo}");
            }
        }

        tx.Commit();
        return sale;
    }

    public List<Sale> GetHeldBills()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Sale>(
            @"SELECT s.*, c.name AS customer_name FROM sales s
              LEFT JOIN customers c ON c.id = s.customer_id
              WHERE s.status = 'Held' ORDER BY s.created_at").ToList();
    }

    /// <summary>Remove a held bill when it is recalled into the billing screen (its items move back to the editor).</summary>
    public void DeleteHeldSale(long saleId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute("DELETE FROM sale_items WHERE sale_id = @id", new { id = saleId }, tx);
        conn.Execute("DELETE FROM sales WHERE id = @id AND status = 'Held'", new { id = saleId }, tx);
        tx.Commit();
    }

    /// <summary>
    /// Phase 3 — Cancel a COMPLETED bill (Admin only; the screen asks for a
    /// reason first — see ReturnsViewModel). ONE transaction:
    /// - the sale stays in the table with status='Cancelled' + cancel_reason
    ///   (audit trail — bills are never deleted, per PLAN.md);
    /// - every item's stock comes back through StockService as a
    ///   'SaleReturn' movement (+qty), exactly like a full return;
    /// - the customer's khata fixes itself: CustomerService.ComputeBalance
    ///   only counts status='Completed' bills, so the cancelled bill's
    ///   unpaid share simply stops counting.
    /// Known edge (documented in PHASE3.md): a later payment received
    /// against this bill (payments row with sale_id set) is NOT reversed —
    /// it stays as a credit on the customer's khata for the owner to settle.
    /// Refusals (thrown as localization keys, the screen translates them):
    /// bill not Completed, or the bill already has returns recorded
    /// (cancelling it would double-restore stock / double-refund).
    /// </summary>
    public void CancelSale(long saleId, string reason, long? userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("CancelReasonRequiredMsg");

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        var sale = conn.QueryFirstOrDefault<Sale>(
            "SELECT * FROM sales WHERE id = @id", new { id = saleId }, tx);
        if (sale == null || sale.Status != "Completed")
            throw new InvalidOperationException("BillNotCancellableMsg");

        long returnsCount = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM returns WHERE sale_id = @id", new { id = saleId }, tx);
        if (returnsCount > 0)
            throw new InvalidOperationException("BillHasReturnsMsg");

        var items = conn.Query<SaleItem>(
            "SELECT * FROM sale_items WHERE sale_id = @id ORDER BY id",
            new { id = saleId }, tx).ToList();

        conn.Execute(
            @"UPDATE sales SET status = 'Cancelled', cancel_reason = @reason
              WHERE id = @id AND status = 'Completed'",
            new { id = saleId, reason = reason.Trim() }, tx);

        foreach (var item in items)
        {
            _stock.ApplyMovement(conn, tx, item.ProductId, "SaleReturn", item.Qty,
                                 "Sale", sale.Id, userId,
                                 $"Cancelled Bill #{sale.BillNo}: {reason.Trim()}");
        }

        tx.Commit();
    }

    public SaleDetail? GetSale(long saleId)
    {
        using var conn = _db.CreateConnection();
        var sale = conn.QueryFirstOrDefault<Sale>(
            @"SELECT s.*, c.name AS customer_name FROM sales s
              LEFT JOIN customers c ON c.id = s.customer_id WHERE s.id = @id", new { id = saleId });
        if (sale == null) return null;
        var items = conn.Query<SaleItem>(
            "SELECT * FROM sale_items WHERE sale_id = @id ORDER BY id", new { id = saleId }).ToList();
        return new SaleDetail { Sale = sale, Items = items };
    }

    public SaleDetail? GetLastCompletedSale()
    {
        using var conn = _db.CreateConnection();
        long? id = conn.QueryFirstOrDefault<long?>(
            "SELECT id FROM sales WHERE status = 'Completed' ORDER BY id DESC LIMIT 1");
        return id.HasValue ? GetSale(id.Value) : null;
    }

    /// <summary>Dashboard: today's completed sales total, bill count, approximate profit.</summary>
    public (long TotalPaisa, int BillCount, long ProfitApproxPaisa) GetTodayStats()
    {
        using var conn = _db.CreateConnection();
        long total = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(total_paisa), 0) FROM sales
              WHERE status = 'Completed' AND date(created_at) = date('now','localtime')");
        int count = conn.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM sales
              WHERE status = 'Completed' AND date(created_at) = date('now','localtime')");

        // Approx profit: (selling rate - CURRENT purchase rate) * qty - line discounts,
        // for today's completed bills. Purchase rate is not snapshotted on sale_items
        // in Phase 1, so if a purchase rate changes later this figure moves — this is
        // why the dashboard labels it "approx." (see PHASE1.md).
        long profit = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM( CAST((si.rate_paisa - COALESCE(p.purchase_rate_paisa, 0)) * si.qty AS INTEGER)
                                   - si.discount_paisa ), 0)
              FROM sale_items si
              JOIN sales s    ON s.id = si.sale_id
              LEFT JOIN products p ON p.id = si.product_id
              WHERE s.status = 'Completed' AND date(s.created_at) = date('now','localtime')");
        return (total, count, profit);
    }
}
