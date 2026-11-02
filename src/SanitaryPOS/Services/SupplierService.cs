using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Helpers;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>
/// Suppliers + their payable khata. Mirror of CustomerService on the other side:
/// PAYABLE BALANCE RULE (one place, used everywhere):
/// balance = opening balance (what we already owed them when the software started)
///         + sum of ALL purchase totals
///         - sum of payments given (payments table, party Supplier, direction OUT).
/// A positive balance means WE owe the supplier.
///
/// Note on purchases.paid_paisa: the amount paid at GRN time is stored on the
/// purchase row for display AND written as a payments OUT row (with its method)
/// by PurchaseService. The balance rule above only reads the payments table for
/// money out, so the GRN payment is counted exactly once.
/// </summary>
public class SupplierService
{
    private readonly DatabaseService _db;
    public SupplierService(DatabaseService db) => _db = db;

    public List<Supplier> GetAll(string? search = null)
    {
        using var conn = _db.CreateConnection();
        string sql = "SELECT * FROM suppliers WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(search))
            sql += " AND (name LIKE @q OR mobile LIKE @q)";
        sql += " ORDER BY name";
        var list = conn.Query<Supplier>(sql, new { q = "%" + (search ?? "") + "%" }).ToList();
        foreach (var s in list) s.BalancePaisa = ComputeBalance(conn, s);
        return list;
    }

    public Supplier? GetById(long id)
    {
        using var conn = _db.CreateConnection();
        var s = conn.QueryFirstOrDefault<Supplier>("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (s != null) s.BalancePaisa = ComputeBalance(conn, s);
        return s;
    }

    public long Save(Supplier s)
    {
        using var conn = _db.CreateConnection();
        if (s.Id == 0)
        {
            return conn.ExecuteScalar<long>(
                @"INSERT INTO suppliers (name, contact_person, mobile, address, city,
                                         opening_balance_paisa, is_active)
                  VALUES (@Name, @ContactPerson, @Mobile, @Address, @City,
                          @OpeningBalancePaisa, @IsActive);
                  SELECT last_insert_rowid();", s);
        }
        conn.Execute(
            @"UPDATE suppliers SET name=@Name, contact_person=@ContactPerson, mobile=@Mobile,
                address=@Address, city=@City, opening_balance_paisa=@OpeningBalancePaisa,
                is_active=@IsActive
              WHERE id=@Id", s);
        return s.Id;
    }

    public long ComputeBalance(Microsoft.Data.Sqlite.SqliteConnection conn, Supplier s)
    {
        long purchases = conn.ExecuteScalar<long>(
            "SELECT COALESCE(SUM(total_paisa), 0) FROM purchases WHERE supplier_id = @id",
            new { id = s.Id });
        long paidOut = conn.ExecuteScalar<long>(
            @"SELECT COALESCE(SUM(amount_paisa), 0) FROM payments
              WHERE party_type = 'Supplier' AND party_id = @id AND direction = 'OUT'",
            new { id = s.Id });
        return s.OpeningBalancePaisa + purchases - paidOut;
    }

    public long GetBalance(long supplierId)
    {
        using var conn = _db.CreateConnection();
        var s = conn.QueryFirstOrDefault<Supplier>(
            "SELECT * FROM suppliers WHERE id = @id", new { id = supplierId });
        return s == null ? 0 : ComputeBalance(conn, s);
    }

    /// <summary>Total payable to all suppliers (Suppliers screen header figure).</summary>
    public long GetTotalPayable()
    {
        using var conn = _db.CreateConnection();
        var suppliers = conn.Query<Supplier>("SELECT * FROM suppliers WHERE is_active = 1").ToList();
        long total = 0;
        foreach (var s in suppliers)
        {
            long bal = ComputeBalance(conn, s);
            if (bal > 0) total += bal;
        }
        return total;
    }

    /// <summary>
    /// Chronological supplier khata, same LedgerEntry shape as the customer khata:
    /// Debit column = amount that increased what we owe (opening + purchases),
    /// Credit column = payments given. Running balance = payable balance.
    /// </summary>
    public List<LedgerEntry> GetLedger(long supplierId)
    {
        using var conn = _db.CreateConnection();
        var s = conn.QueryFirstOrDefault<Supplier>(
            "SELECT * FROM suppliers WHERE id = @id", new { id = supplierId });
        var entries = new List<LedgerEntry>();
        if (s == null) return entries;

        long running = 0;
        if (s.OpeningBalancePaisa != 0)
        {
            running += s.OpeningBalancePaisa;
            entries.Add(new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(s.CreatedAt),
                Description = "Opening Balance / ابتدائی بقایا",
                DebitPaisa = s.OpeningBalancePaisa,
                RunningBalancePaisa = running
            });
        }

        var purchases = conn.Query<Purchase>(
            "SELECT * FROM purchases WHERE supplier_id = @id ORDER BY created_at, id",
            new { id = supplierId }).ToList();
        var pays = conn.Query<Payment>(
            @"SELECT * FROM payments WHERE party_type = 'Supplier' AND party_id = @id
              AND direction = 'OUT' ORDER BY created_at, id",
            new { id = supplierId }).ToList();

        var merged = new List<(string Date, long Seq, LedgerEntry Entry)>();
        foreach (var p in purchases)
        {
            merged.Add((p.CreatedAt, p.Id, new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(p.CreatedAt),
                Description = $"Purchase / خریداری" +
                                (string.IsNullOrWhiteSpace(p.SupplierBillNo) ? "" : $" (Bill {p.SupplierBillNo})"),
                DebitPaisa = p.TotalPaisa
            }));
        }
        foreach (var p in pays)
        {
            merged.Add((p.CreatedAt, p.Id, new LedgerEntry
            {
                DateText = DateHelper.FormatDbDate(p.CreatedAt),
                Description = $"Payment Given ({p.PaymentMethod}) / ادائیگی",
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
