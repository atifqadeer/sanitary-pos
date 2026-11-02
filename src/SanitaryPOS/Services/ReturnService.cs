using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>Result of saving a return — the Exchange flow needs the credit amount.</summary>
public class ReturnResult
{
    public long ReturnId { get; set; }
    public long RefundPaisa { get; set; }
    public string SettleMethod { get; set; } = "Cash";
    public long? CustomerId { get; set; }
}

public class ReturnService
{
    private readonly DatabaseService _db;
    private readonly StockService _stock;

    public ReturnService(DatabaseService db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>Find a Completed sale by its printed bill number (Returns screen entry point).</summary>
    public SaleDetail? GetSaleByBillNo(long billNo)
    {
        using var conn = _db.CreateConnection();
        var sale = conn.QueryFirstOrDefault<Sale>(
            @"SELECT s.*, c.name AS customer_name FROM sales s
              LEFT JOIN customers c ON c.id = s.customer_id
              WHERE s.bill_no = @n AND s.status = 'Completed'", new { n = billNo });
        if (sale == null) return null;
        var items = conn.Query<SaleItem>(
            "SELECT * FROM sale_items WHERE sale_id = @id ORDER BY id", new { id = sale.Id }).ToList();
        return new SaleDetail { Sale = sale, Items = items };
    }

    /// <summary>How much of one product on one sale was already returned in earlier returns.</summary>
    public double GetAlreadyReturnedQty(long saleId, long productId)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<double>(
            @"SELECT COALESCE(SUM(ri.qty), 0) FROM return_items ri
              JOIN returns r ON r.id = ri.return_id
              WHERE r.sale_id = @saleId AND ri.product_id = @productId",
            new { saleId, productId });
    }

    /// <summary>
    /// Save a return in ONE transaction:
    /// - returns row + return_items rows
    /// - stock comes back through StockService: GOOD items as a 'SaleReturn'
    ///   movement into sellable qty; DAMAGED items as a 'Damage' movement,
    ///   which StockService routes to damaged_qty ONLY (never sellable).
    /// - Settle method 'Khata': the refund is written as a payments row
    ///   (Customer / IN, method 'Cash', note says return adjustment, sale_id set)
    ///   so the customer's khata balance drops by the refund — the balance
    ///   formula in CustomerService stays the single source of truth.
    ///   'Cash' = money handed back at the counter (no khata row).
    ///   'Exchange' = no khata row either; the caller carries the credit amount
    ///   to the Billing screen (see ReturnResult).
    /// Qty validation (return qty &lt;= sold − already returned) is re-checked
    /// here inside the transaction, not only on screen.
    /// </summary>
    public ReturnResult SaveReturn(Sale sale, IReadOnlyList<SaleReturnItem> items,
                                   string settleMethod, long? userId)
    {
        var lines = items.Where(i => i.Qty > 0).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("No return quantity entered.");

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        foreach (var line in lines)
        {
            double sold = conn.ExecuteScalar<double>(
                @"SELECT COALESCE(qty, 0) FROM sale_items
                  WHERE sale_id = @saleId AND product_id = @productId",
                new { saleId = sale.Id, productId = line.ProductId }, tx);
            double already = conn.ExecuteScalar<double>(
                @"SELECT COALESCE(SUM(ri.qty), 0) FROM return_items ri
                  JOIN returns r ON r.id = ri.return_id
                  WHERE r.sale_id = @saleId AND ri.product_id = @productId",
                new { saleId = sale.Id, productId = line.ProductId }, tx);
            if (line.Qty > sold - already + 0.0001)
                throw new InvalidOperationException(
                    "Return quantity is more than the remaining returnable quantity on this bill.");
        }

        long refund = lines.Sum(i => i.LineTotalPaisa);

        long returnId = conn.ExecuteScalar<long>(
            @"INSERT INTO returns (sale_id, customer_id, total_refund_paisa, settle_method, user_id)
              VALUES (@SaleId, @CustomerId, @Refund, @SettleMethod, @UserId);
              SELECT last_insert_rowid();",
            new
            {
                SaleId = sale.Id,
                CustomerId = sale.CustomerId,
                Refund = refund,
                SettleMethod = settleMethod,
                UserId = userId
            }, tx);

        foreach (var line in lines)
        {
            line.ReturnId = returnId;
            conn.Execute(
                @"INSERT INTO return_items (return_id, product_id, qty, rate_paisa,
                                            is_damaged, line_total_paisa)
                  VALUES (@ReturnId, @ProductId, @Qty, @RatePaisa, @IsDamaged, @LineTotalPaisa)",
                line, tx);

            if (line.IsDamaged == 1)
            {
                // 'Damage' movement: StockService puts this into damaged_qty only.
                _stock.ApplyMovement(conn, tx, line.ProductId, "Damage", line.Qty,
                                     "Return", returnId, userId,
                                     $"Damaged return, Bill #{sale.BillNo}");
            }
            else
            {
                _stock.ApplyMovement(conn, tx, line.ProductId, "SaleReturn", line.Qty,
                                     "Return", returnId, userId,
                                     $"Return, Bill #{sale.BillNo}");
            }
        }

        if (settleMethod == "Khata" && sale.CustomerId.HasValue && refund > 0)
        {
            conn.Execute(
                @"INSERT INTO payments (party_type, party_id, direction, amount_paisa,
                                        payment_method, sale_id, note, user_id)
                  VALUES ('Customer', @customerId, 'IN', @refund, 'Cash', @saleId, @note, @userId)",
                new
                {
                    customerId = sale.CustomerId.Value,
                    refund,
                    saleId = sale.Id,
                    note = $"Return adjustment (Return #{returnId}, Bill #{sale.BillNo})",
                    userId
                }, tx);
        }

        tx.Commit();
        return new ReturnResult
        {
            ReturnId = returnId,
            RefundPaisa = refund,
            SettleMethod = settleMethod,
            CustomerId = sale.CustomerId
        };
    }

    /// <summary>Returns history, newest first.</summary>
    public List<SaleReturn> GetHistory()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<SaleReturn>(
            @"SELECT r.*, s.bill_no AS bill_no, c.name AS customer_name FROM returns r
              JOIN sales s ON s.id = r.sale_id
              LEFT JOIN customers c ON c.id = r.customer_id
              ORDER BY r.created_at DESC, r.id DESC").ToList();
    }
}
