using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Helpers;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

public class CustomerService
{
    private readonly DatabaseService _db;
    public CustomerService(DatabaseService db) => _db = db;

    public List<Customer> GetAll(string? search = null)
    {
        using var conn = _db.CreateConnection();
        string sql = "SELECT * FROM customers WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(search))
            sql += " AND (name LIKE @q OR mobile LIKE @q)";
        sql += " ORDER BY name";
        var list = conn.Query<Customer>(sql, new { q = "%" + (search ?? "") + "%" }).ToList();
        foreach (var c in list) c.BalancePaisa = ComputeBalance(conn, c);
        return list;
    }

    public Customer? GetById(long id)
    {
        using var conn = _db.CreateConnection();
        var c = conn.QueryFirstOrDefault<Customer>("SELECT * FROM customers WHERE id = @id", new { id });
        if (c != null) c.BalancePaisa = ComputeBalance(conn, c);
        return c;
    }

    public long Save(Customer c)
    {
        using var conn = _db.CreateConnection();
        if (c.Id == 0)
        {
            return conn.ExecuteScalar<long>(
                @"INSERT INTO customers (name, mobile, address, city, customer_type,
                                         credit_limit_paisa, opening_balance_paisa, is_active)
                  VALUES (@Name, @Mobile, @Address, @City, @CustomerType,
                          @CreditLimitPaisa, @OpeningBalancePaisa, @IsActive);
                  SELECT last_insert_rowid();", c);
        }
        conn.Execute(
            @"UPDATE customers SET name=@Name, mobile=@Mobile, address=@Address, city=@City,
                customer_type=@CustomerType, credit_limit_paisa=@CreditLimitPaisa,
                opening_balance_paisa=@OpeningBalancePaisa, is_active=@IsActive
              WHERE id=@Id", c);
        return c.Id;
    }

    /// <summary>
    /// KHATA BALANCE RULE (one place, used everywhere):
    /// balance = opening balance
    ///         + sum over Completed bills of (total - paid at bill time)
    ///         - sum of later payments received (payments table, IN).
    /// A positive balance means the customer owes the shop.
    /// </summary>
    public long ComputeBalance(Microsoft.Data.Sqlite.SqliteConnection conn, Customer c)
    {
        long unpaidOnBills = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(total_paisa - paid_paisa), 0) FROM sales
              WHERE customer_id = @id AND status = 'Completed'", new { id = c.Id });
        long laterPayments = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments
              WHERE party_type = 'Customer' AND party_id = @id AND direction = 'IN'",
            new { id = c.Id });
        return c.OpeningBalancePaisa + unpaidOnBills - laterPayments;
    }

    public long GetBalance(long customerId)
    {
        using var conn = _db.CreateConnection();
        var c = conn.QueryFirstOrDefault<Customer>(
            "SELECT * FROM customers WHERE id = @id", new { id = customerId });
        return c == null ? 0 : ComputeBalance(conn, c);
    }

    /// <summary>Total udhaar all customers owe us (dashboard figure).</summary>
    public long GetTotalReceivable()
    {
        using var conn = _db.CreateConnection();
        var customers = conn.Query<Customer>("SELECT * FROM customers WHERE is_active = 1").ToList();
        long total = 0;
        foreach (var c in customers)
        {
            long bal = ComputeBalance(conn, c);
            if (bal > 0) total += bal;
        }
        return total;
    }

    /// <summary>Chronological khata: opening, each bill (debit), each payment (credit), running balance.</summary>
    public List<LedgerEntry> GetLedger(long customerId)
    {
        using var conn = _db.CreateConnection();
        var c = conn.QueryFirstOrDefault<Customer>(
            "SELECT * FROM customers WHERE id = @id", new { id = customerId });
        var entries = new List<LedgerEntry>();
        if (c == null) return entries;

        long running = 0;
        if (c.OpeningBalancePaisa != 0)
        {
            running += c.OpeningBalancePaisa;
            entries.Add(new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(c.CreatedAt),
                Description = "Opening Balance / ابتدائی بقایا",
                DebitPaisa = c.OpeningBalancePaisa,
                RunningBalancePaisa = running
            });
        }

        // Merge bills and payments in date order in memory (small shop = small data).
        var bills = conn.Query<Sale>(
            "SELECT * FROM sales WHERE customer_id = @id AND status = 'Completed' ORDER BY created_at, id",
            new { id = customerId }).ToList();
        var pays = conn.Query<Payment>(
            @"SELECT * FROM payments WHERE party_type = 'Customer' AND party_id = @id
              AND direction = 'IN' ORDER BY created_at, id",
            new { id = customerId }).ToList();

        var merged = new List<(string Date, long Seq, LedgerEntry Entry)>();
        foreach (var b in bills)
        {
            long due = b.TotalPaisa - b.PaidPaisa;
            if (due == 0) continue; // fully paid bills do not affect khata
            merged.Add((b.CreatedAt, b.Id, new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(b.CreatedAt),
                Description = $"Bill #{b.BillNo} / بل",
                DebitPaisa = due
            }));
        }
        foreach (var p in pays)
        {
            merged.Add((p.CreatedAt, p.Id, new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(p.CreatedAt),
                Description = $"Payment Received ({p.PaymentMethod}) / وصولی",
                CreditPaisa = p.AmountPaisa
            }));
        }

        foreach (var m in merged.OrderBy(x => x.Date).ThenBy(x => x.Seq))
        {
            running += m.Entry.DebitPaisa - m.Entry.CreditPaisa;
            m.Entry.RunningBalancePaisa = running;
            entries.Add(m.Entry);
        }
        return entries;
    }
}

/// <summary>Payments received from customers (وصولی) and payments given to suppliers (Phase 2).</summary>
public class PaymentService
{
    private readonly DatabaseService _db;
    public PaymentService(DatabaseService db) => _db = db;

    public long ReceiveCustomerPayment(long customerId, long amountPaisa, string method,
                                       string? note, long? userId, long? saleId = null)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<long>(
            @"INSERT INTO payments (party_type, party_id, direction, amount_paisa,
                                    payment_method, sale_id, note, user_id)
              VALUES ('Customer', @customerId, 'IN', @amountPaisa, @method, @saleId, @note, @userId);
              SELECT last_insert_rowid();",
            new { customerId, amountPaisa, method, saleId, note, userId });
    }

    public List<Payment> GetForCustomer(long customerId)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Payment>(
            @"SELECT * FROM payments WHERE party_type = 'Customer' AND party_id = @id
              ORDER BY created_at DESC, id DESC", new { id = customerId }).ToList();
    }

    /// <summary>Phase 2: record money given TO a supplier (direction OUT). Drops their payable balance.</summary>
    public long PaySupplier(long supplierId, long amountPaisa, string method,
                            string? note, long? userId)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<long>(
            @"INSERT INTO payments (party_type, party_id, direction, amount_paisa,
                                    payment_method, note, user_id)
              VALUES ('Supplier', @supplierId, 'OUT', @amountPaisa, @method, @note, @userId);
              SELECT last_insert_rowid();",
            new { supplierId, amountPaisa, method, note, userId });
    }

    public List<Payment> GetForSupplier(long supplierId)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Payment>(
            @"SELECT * FROM payments WHERE party_type = 'Supplier' AND party_id = @id
              ORDER BY created_at DESC, id DESC", new { id = supplierId }).ToList();
    }
}
