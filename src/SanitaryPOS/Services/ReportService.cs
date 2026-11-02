using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

/// <summary>
/// All Phase 2 reports. Date filters are 'yyyy-MM-dd' strings compared with
/// date(created_at), the same style as SaleService.GetTodayStats.
/// Only Completed sales are ever counted.
/// </summary>
public class ReportService
{
    private readonly DatabaseService _db;
    public ReportService(DatabaseService db) => _db = db;

    public List<SalesByDateRow> SalesByDate(string fromDate, string toDate)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<SalesByDateRow>(
            @"SELECT date(created_at) AS date_db, COUNT(*) AS bill_count,
                     COALESCE(SUM(total_paisa), 0) AS total_paisa
              FROM sales
              WHERE status = 'Completed'
                AND date(created_at) >= date(@fromDate) AND date(created_at) <= date(@toDate)
              GROUP BY date(created_at) ORDER BY date(created_at)",
            new { fromDate, toDate }).ToList();
    }

    public List<SalesByMethodRow> SalesByPaymentMethod(string fromDate, string toDate)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<SalesByMethodRow>(
            @"SELECT payment_method AS payment_method, COUNT(*) AS bill_count,
                     COALESCE(SUM(total_paisa), 0) AS total_paisa
              FROM sales
              WHERE status = 'Completed'
                AND date(created_at) >= date(@fromDate) AND date(created_at) <= date(@toDate)
              GROUP BY payment_method ORDER BY total_paisa DESC",
            new { fromDate, toDate }).ToList();
    }

    /// <summary>
    /// ESTIMATED profit, deliberately the same line-level formula as the
    /// Phase 1 dashboard (SaleService.GetTodayStats):
    /// profit = Σ (line total after line discount) − Σ (CURRENT purchase rate × qty).
    /// Bill-level discount and tax are NOT included — the report is labelled
    /// "Estimated Profit" in both languages for this reason.
    /// </summary>
    public List<ProfitByDateRow> ProfitByDate(string fromDate, string toDate)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<ProfitByDateRow>(
            @"SELECT date(s.created_at) AS date_db,
                     COALESCE(SUM(si.line_total_paisa), 0) AS sales_paisa,
                     COALESCE(SUM(CAST(COALESCE(p.purchase_rate_paisa, 0) * si.qty AS INTEGER)), 0) AS cost_paisa
              FROM sale_items si
              JOIN sales s ON s.id = si.sale_id
              LEFT JOIN products p ON p.id = si.product_id
              WHERE s.status = 'Completed'
                AND date(s.created_at) >= date(@fromDate) AND date(s.created_at) <= date(@toDate)
              GROUP BY date(s.created_at) ORDER BY date(s.created_at)",
            new { fromDate, toDate }).ToList();
    }

    public List<GroupSalesRow> SalesByBrand(string fromDate, string toDate)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<GroupSalesRow>(
            @"SELECT COALESCE(b.name, '-') AS group_name,
                     COALESCE(SUM(si.qty), 0) AS qty,
                     COALESCE(SUM(si.line_total_paisa), 0) AS amount_paisa
              FROM sale_items si
              JOIN sales s ON s.id = si.sale_id
              LEFT JOIN products p ON p.id = si.product_id
              LEFT JOIN brands b ON b.id = p.brand_id
              WHERE s.status = 'Completed'
                AND date(s.created_at) >= date(@fromDate) AND date(s.created_at) <= date(@toDate)
              GROUP BY COALESCE(b.name, '-') ORDER BY amount_paisa DESC",
            new { fromDate, toDate }).ToList();
    }

    public List<GroupSalesRow> SalesByCategory(string fromDate, string toDate)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<GroupSalesRow>(
            @"SELECT COALESCE(c.name_en, '-') AS group_name,
                     COALESCE(SUM(si.qty), 0) AS qty,
                     COALESCE(SUM(si.line_total_paisa), 0) AS amount_paisa
              FROM sale_items si
              JOIN sales s ON s.id = si.sale_id
              LEFT JOIN products p ON p.id = si.product_id
              LEFT JOIN categories c ON c.id = p.category_id
              WHERE s.status = 'Completed'
                AND date(s.created_at) >= date(@fromDate) AND date(s.created_at) <= date(@toDate)
              GROUP BY COALESCE(c.name_en, '-') ORDER BY amount_paisa DESC",
            new { fromDate, toDate }).ToList();
    }

    /// <summary>Current stock value per product (only products that hold stock or are active sellers).</summary>
    public List<StockValueRow> StockValue()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<StockValueRow>(
            @"SELECT p.name AS product_name, b.name AS brand_name,
                     COALESCE(s.qty, 0) AS qty,
                     CAST(COALESCE(s.qty, 0) * p.purchase_rate_paisa AS INTEGER) AS purchase_value_paisa,
                     CAST(COALESCE(s.qty, 0) * p.retail_rate_paisa AS INTEGER) AS retail_value_paisa
              FROM products p
              LEFT JOIN brands b ON b.id = p.brand_id
              LEFT JOIN stock s ON s.product_id = p.id
              WHERE p.is_active = 1 AND COALESCE(s.qty, 0) <> 0
              ORDER BY p.name").ToList();
    }
}
