using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

public class QuotationDetail
{
    public Quotation Quotation { get; set; } = new();
    public List<QuotationItem> Items { get; set; } = new();
}

public class QuotationService
{
    private readonly DatabaseService _db;
    private readonly StockService _stock;

    public QuotationService(DatabaseService db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    public List<Quotation> GetAll(string? status = null)
    {
        using var conn = _db.CreateConnection();
        string sql = "SELECT * FROM quotations";
        if (!string.IsNullOrWhiteSpace(status)) sql += " WHERE status = @status";
        sql += " ORDER BY created_at DESC, id DESC";
        return conn.Query<Quotation>(sql, new { status }).ToList();
    }

    /// <summary>Save a quotation + items. NO stock is touched by a quotation.</summary>
    public Quotation SaveQuotation(Quotation q, IReadOnlyList<QuotationItem> items)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        q.QuotationNo = conn.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(quotation_no), 0) + 1 FROM quotations", null, tx);
        q.Id = conn.ExecuteScalar<long>(
            @"INSERT INTO quotations (quotation_no, customer_id, customer_name_snapshot,
                                      customer_mobile_snapshot, price_level, subtotal_paisa,
                                      discount_paisa, total_paisa, valid_days, status, user_id)
              VALUES (@QuotationNo, @CustomerId, @CustomerNameSnapshot,
                      @CustomerMobileSnapshot, @PriceLevel, @SubtotalPaisa,
                      @DiscountPaisa, @TotalPaisa, @ValidDays, @Status, @UserId);
              SELECT last_insert_rowid();", q, tx);

        foreach (var item in items)
        {
            item.QuotationId = q.Id;
            conn.Execute(
                @"INSERT INTO quotation_items (quotation_id, product_id, product_name_snapshot,
                                               qty, rate_paisa, discount_paisa, line_total_paisa)
                  VALUES (@QuotationId, @ProductId, @ProductNameSnapshot,
                          @Qty, @RatePaisa, @DiscountPaisa, @LineTotalPaisa)", item, tx);
        }

        tx.Commit();
        return q;
    }

    /// <summary>
    /// Phase 2 — Update an existing OPEN quotation: head fields are updated and
    /// its items are replaced (deleted + re-inserted) in ONE transaction.
    /// The quotation number is NOT changed. Converted/Cancelled quotations
    /// cannot be edited — this throws, and the screen checks first too.
    /// </summary>
    public void UpdateQuotation(Quotation q, IReadOnlyList<QuotationItem> items)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        string status = conn.QueryFirstOrDefault<string>(
            "SELECT status FROM quotations WHERE id = @id", new { id = q.Id }, tx) ?? "";
        if (status != "Open")
            throw new InvalidOperationException("Only an Open quotation can be edited.");

        conn.Execute(
            @"UPDATE quotations SET customer_id=@CustomerId,
                customer_name_snapshot=@CustomerNameSnapshot,
                customer_mobile_snapshot=@CustomerMobileSnapshot,
                price_level=@PriceLevel, subtotal_paisa=@SubtotalPaisa,
                discount_paisa=@DiscountPaisa, total_paisa=@TotalPaisa,
                valid_days=@ValidDays
              WHERE id=@Id", q, tx);

        conn.Execute("DELETE FROM quotation_items WHERE quotation_id = @id", new { id = q.Id }, tx);

        foreach (var item in items)
        {
            item.QuotationId = q.Id;
            conn.Execute(
                @"INSERT INTO quotation_items (quotation_id, product_id, product_name_snapshot,
                                               qty, rate_paisa, discount_paisa, line_total_paisa)
                  VALUES (@QuotationId, @ProductId, @ProductNameSnapshot,
                          @Qty, @RatePaisa, @DiscountPaisa, @LineTotalPaisa)", item, tx);
        }

        tx.Commit();
    }

    public QuotationDetail? GetDetail(long quotationId)
    {
        using var conn = _db.CreateConnection();
        var q = conn.QueryFirstOrDefault<Quotation>(
            "SELECT * FROM quotations WHERE id = @id", new { id = quotationId });
        if (q == null) return null;
        var items = conn.Query<QuotationItem>(
            "SELECT * FROM quotation_items WHERE quotation_id = @id ORDER BY id",
            new { id = quotationId }).ToList();
        return new QuotationDetail { Quotation = q, Items = items };
    }

    /// <summary>
    /// ONE-CLICK convert: creates a Completed sale from an Open quotation, cuts
    /// stock, and marks the quotation Converted — all in one transaction.
    /// The quotation's discount becomes the sale's bill discount; tax is 0 on
    /// converted sales in Phase 1 (enable tax at billing time if ever needed).
    /// Returns the new sale id.
    /// </summary>
    public long ConvertToBill(long quotationId, string paymentMethod, long paidPaisa, long? userId)
    {
        var detail = GetDetail(quotationId)
                     ?? throw new InvalidOperationException("Quotation not found.");
        var q = detail.Quotation;
        if (q.Status != "Open")
            throw new InvalidOperationException("Only an Open quotation can be converted.");

        var sale = new Sale
        {
            CustomerId = q.CustomerId,
            PriceLevel = q.PriceLevel,
            SubtotalPaisa = q.SubtotalPaisa,
            ItemDiscountPaisa = detail.Items.Sum(i => i.DiscountPaisa),
            BillDiscountPaisa = q.DiscountPaisa,
            TaxPaisa = 0,
            TotalPaisa = q.TotalPaisa,
            PaidPaisa = paidPaisa,
            PaymentMethod = paymentMethod,
            Status = "Completed",
            QuotationId = q.Id,
            UserId = userId
        };

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        sale.BillNo = conn.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(bill_no), 0) + 1 FROM sales", null, tx);
        sale.Id = conn.ExecuteScalar<long>(
            @"INSERT INTO sales (bill_no, customer_id, price_level, subtotal_paisa, item_discount_paisa,
                                 bill_discount_paisa, tax_paisa, total_paisa, paid_paisa, payment_method,
                                 status, quotation_id, user_id)
              VALUES (@BillNo, @CustomerId, @PriceLevel, @SubtotalPaisa, @ItemDiscountPaisa,
                      @BillDiscountPaisa, @TaxPaisa, @TotalPaisa, @PaidPaisa, @PaymentMethod,
                      @Status, @QuotationId, @UserId);
              SELECT last_insert_rowid();", sale, tx);

        foreach (var qi in detail.Items)
        {
            var item = new SaleItem
            {
                SaleId = sale.Id,
                ProductId = qi.ProductId,
                ProductNameSnapshot = qi.ProductNameSnapshot,
                Qty = qi.Qty,
                RatePaisa = qi.RatePaisa,
                DiscountPaisa = qi.DiscountPaisa,
                LineTotalPaisa = qi.LineTotalPaisa
            };
            conn.Execute(
                @"INSERT INTO sale_items (sale_id, product_id, product_name_snapshot, qty,
                                          rate_paisa, discount_paisa, line_total_paisa)
                  VALUES (@SaleId, @ProductId, @ProductNameSnapshot, @Qty,
                          @RatePaisa, @DiscountPaisa, @LineTotalPaisa)", item, tx);

            _stock.ApplyMovement(conn, tx, item.ProductId, "Sale", -item.Qty,
                                 "Sale", sale.Id, userId, $"Bill #{sale.BillNo} (from Quotation #{q.QuotationNo})");
        }

        conn.Execute(
            @"UPDATE quotations SET status = 'Converted', converted_sale_id = @saleId
              WHERE id = @id", new { saleId = sale.Id, id = q.Id }, tx);

        tx.Commit();
        return sale.Id;
    }
}
