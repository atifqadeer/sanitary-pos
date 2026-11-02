using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

public class PurchaseDetail
{
    public Purchase Purchase { get; set; } = new();
    public List<PurchaseItem> Items { get; set; } = new();
}

public class PurchaseService
{
    private readonly DatabaseService _db;
    private readonly StockService _stock;

    public PurchaseService(DatabaseService db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>
    /// Save a purchase (GRN) in ONE transaction:
    /// - purchases row + purchase_items rows
    /// - stock INCREASES through StockService (movement type 'Purchase') — never a direct stock edit
    /// - if an item's purchase rate differs from the product's stored purchase rate,
    ///   the product's purchase_rate_paisa is UPDATED to the new rate (this GRN's
    ///   rate becomes the current cost — documented in PHASE2.md)
    /// - if PaidPaisa &gt; 0, a payments row (Supplier / OUT, with the given method)
    ///   is written in the same transaction, so the supplier payable drops.
    /// The created_at is taken from the purchase date chosen on screen
    /// (Purchase.CreatedAt must be set by the caller as 'yyyy-MM-dd HH:mm:ss').
    /// </summary>
    public Purchase SavePurchase(Purchase purchase, IReadOnlyList<PurchaseItem> items,
                                 string paidMethod, long? userId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        purchase.Id = conn.ExecuteScalar<long>(
            @"INSERT INTO purchases (supplier_id, supplier_bill_no, subtotal_paisa, discount_paisa,
                                     total_paisa, paid_paisa, user_id, created_at)
              VALUES (@SupplierId, @SupplierBillNo, @SubtotalPaisa, @DiscountPaisa,
                      @TotalPaisa, @PaidPaisa, @UserId, @CreatedAt);
              SELECT last_insert_rowid();", purchase, tx);

        foreach (var item in items)
        {
            item.PurchaseId = purchase.Id;
            conn.Execute(
                @"INSERT INTO purchase_items (purchase_id, product_id, qty,
                                              purchase_rate_paisa, line_total_paisa)
                  VALUES (@PurchaseId, @ProductId, @Qty, @PurchaseRatePaisa, @LineTotalPaisa)",
                item, tx);

            _stock.ApplyMovement(conn, tx, item.ProductId, "Purchase", item.Qty,
                                 "Purchase", purchase.Id, userId,
                                 $"Supplier bill {purchase.SupplierBillNo}");

            // Latest GRN rate becomes the product's current purchase rate.
            conn.Execute(
                @"UPDATE products SET purchase_rate_paisa = @rate
                  WHERE id = @id AND purchase_rate_paisa <> @rate",
                new { rate = item.PurchaseRatePaisa, id = item.ProductId }, tx);
        }

        if (purchase.PaidPaisa > 0)
        {
            conn.Execute(
                @"INSERT INTO payments (party_type, party_id, direction, amount_paisa,
                                        payment_method, note, user_id)
                  VALUES ('Supplier', @supplierId, 'OUT', @amount, @method, @note, @userId)",
                new
                {
                    supplierId = purchase.SupplierId,
                    amount = purchase.PaidPaisa,
                    method = paidMethod,
                    note = $"Paid on purchase (Bill {purchase.SupplierBillNo})",
                    userId
                }, tx);
        }

        tx.Commit();
        return purchase;
    }

    /// <summary>Purchase history, newest first. Optional supplier + date-range filters.</summary>
    public List<Purchase> GetAll(long? supplierId = null, string? fromDate = null, string? toDate = null)
    {
        using var conn = _db.CreateConnection();
        string sql = @"
            SELECT p.*, s.name AS supplier_name FROM purchases p
            JOIN suppliers s ON s.id = p.supplier_id WHERE 1 = 1";
        if (supplierId.HasValue) sql += " AND p.supplier_id = @supplierId";
        if (!string.IsNullOrWhiteSpace(fromDate)) sql += " AND date(p.created_at) >= date(@fromDate)";
        if (!string.IsNullOrWhiteSpace(toDate)) sql += " AND date(p.created_at) <= date(@toDate)";
        sql += " ORDER BY p.created_at DESC, p.id DESC";
        return conn.Query<Purchase>(sql, new { supplierId, fromDate, toDate }).ToList();
    }

    public PurchaseDetail? GetDetail(long purchaseId)
    {
        using var conn = _db.CreateConnection();
        var purchase = conn.QueryFirstOrDefault<Purchase>(
            @"SELECT p.*, s.name AS supplier_name FROM purchases p
              JOIN suppliers s ON s.id = p.supplier_id WHERE p.id = @id", new { id = purchaseId });
        if (purchase == null) return null;
        var items = conn.Query<PurchaseItem>(
            @"SELECT pi.*, pr.name AS product_name FROM purchase_items pi
              JOIN products pr ON pr.id = pi.product_id
              WHERE pi.purchase_id = @id ORDER BY pi.id", new { id = purchaseId }).ToList();
        return new PurchaseDetail { Purchase = purchase, Items = items };
    }
}
