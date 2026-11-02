using Dapper;
using SanitaryPOS.Data;
using SanitaryPOS.Models;

namespace SanitaryPOS.Services;

public class ProductService
{
    private readonly DatabaseService _db;
    private readonly StockService _stock;

    public ProductService(DatabaseService db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    private const string SelectWithJoins = @"
        SELECT p.*,
               b.name  AS brand_name,
               c.name_en AS category_name,
               u.name_en AS unit_name,
               COALESCE(s.qty, 0) AS stock_qty
        FROM products p
        LEFT JOIN brands b     ON b.id = p.brand_id
        LEFT JOIN categories c ON c.id = p.category_id
        LEFT JOIN units u      ON u.id = p.unit_id
        LEFT JOIN stock s      ON s.product_id = p.id";

    public List<Product> GetAll(string? search = null, bool activeOnly = true)
    {
        using var conn = _db.CreateConnection();
        string sql = SelectWithJoins + " WHERE 1 = 1";
        if (activeOnly) sql += " AND p.is_active = 1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += @" AND (p.name LIKE @q OR p.barcode LIKE @q OR p.model_code LIKE @q OR b.name LIKE @q)";
        }
        sql += " ORDER BY p.name";
        return conn.Query<Product>(sql, new { q = "%" + (search ?? "") + "%" }).ToList();
    }

    public Product? GetByBarcode(string barcode)
    {
        using var conn = _db.CreateConnection();
        return conn.QueryFirstOrDefault<Product>(
            SelectWithJoins + " WHERE p.barcode = @barcode AND p.is_active = 1",
            new { barcode = barcode.Trim() });
    }

    public Product? GetById(long id)
    {
        using var conn = _db.CreateConnection();
        return conn.QueryFirstOrDefault<Product>(
            SelectWithJoins + " WHERE p.id = @id", new { id });
    }

    /// <summary>
    /// Insert or update a product. For a NEW product, openingStockQty (if &gt; 0)
    /// is written as an "Opening" stock movement — never as a direct stock edit.
    /// Returns the product id.
    /// </summary>
    public long Save(Product p, double openingStockQty, long? userId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        long id;
        if (p.Id == 0)
        {
            id = conn.ExecuteScalar<long>(
                @"INSERT INTO products (name, brand_id, category_id, model_code, barcode, finish, size,
                                        unit_id, purchase_rate_paisa, retail_rate_paisa, trade_rate_paisa,
                                        wholesale_rate_paisa, low_stock_level, location, is_active)
                  VALUES (@Name, @BrandId, @CategoryId, @ModelCode, @Barcode, @Finish, @Size,
                          @UnitId, @PurchaseRatePaisa, @RetailRatePaisa, @TradeRatePaisa,
                          @WholesaleRatePaisa, @LowStockLevel, @Location, @IsActive);
                  SELECT last_insert_rowid();", p, tx);

            // Make sure a stock row exists even when opening qty is 0.
            conn.Execute("INSERT OR IGNORE INTO stock (product_id, qty) VALUES (@id, 0)", new { id }, tx);

            if (openingStockQty != 0)
                _stock.ApplyMovement(conn, tx, id, "Opening", openingStockQty, null, null, userId, "Opening stock");
        }
        else
        {
            conn.Execute(
                @"UPDATE products SET name=@Name, brand_id=@BrandId, category_id=@CategoryId,
                    model_code=@ModelCode, barcode=@Barcode, finish=@Finish, size=@Size, unit_id=@UnitId,
                    purchase_rate_paisa=@PurchaseRatePaisa, retail_rate_paisa=@RetailRatePaisa,
                    trade_rate_paisa=@TradeRatePaisa, wholesale_rate_paisa=@WholesaleRatePaisa,
                    low_stock_level=@LowStockLevel, location=@Location, is_active=@IsActive
                  WHERE id=@Id", p, tx);
            id = p.Id;
        }

        tx.Commit();
        return id;
    }

    /// <summary>Quick price edit from the product list (retail rate only).</summary>
    public void UpdateRetailPrice(long productId, long retailRatePaisa)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE products SET retail_rate_paisa = @r WHERE id = @id",
                     new { r = retailRatePaisa, id = productId });
    }

    /// <summary>
    /// Phase 2 — Bulk price update: change ONE price field (Purchase / Retail /
    /// Trade / Wholesale) for ALL active products of one brand by a percentage
    /// (+5 = 5% increase, -10 = 10% decrease), in ONE transaction.
    /// Rounding: new rate is rounded to the nearest paisa. Called from the
    /// Products screen, which is responsible for the Admin check + confirmation.
    /// Returns how many products were updated.
    /// </summary>
    public int BulkUpdatePrices(long brandId, string priceField, double percent, long? userId)
    {
        string column = priceField switch
        {
            "Purchase" => "purchase_rate_paisa",
            "Trade" => "trade_rate_paisa",
            "Wholesale" => "wholesale_rate_paisa",
            _ => "retail_rate_paisa"
        };

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        // Column name comes from the fixed switch above (never user text), so
        // building it into the SQL is safe; values stay parameterised.
        int affected = conn.Execute(
            $@"UPDATE products
               SET {column} = CAST(ROUND({column} * (1.0 + @percent / 100.0)) AS INTEGER)
               WHERE brand_id = @brandId AND is_active = 1",
            new { percent, brandId }, tx);
        tx.Commit();
        return affected;
    }

    /// <summary>How many active products a bulk price update would touch (preview count).</summary>
    public int CountByBrand(long brandId)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM products WHERE brand_id = @brandId AND is_active = 1",
            new { brandId });
    }

    public List<Product> GetLowStock()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Product>(
            SelectWithJoins +
            " WHERE p.is_active = 1 AND COALESCE(s.qty,0) <= p.low_stock_level ORDER BY s.qty")
            .ToList();
    }

    // ----- Lookup lists for the product form -----

    public List<Brand> GetBrands()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Brand>("SELECT * FROM brands WHERE is_active = 1 ORDER BY name").ToList();
    }

    /// <summary>Type a new brand name in the form and it is created if missing. Returns its id.</summary>
    public long GetOrCreateBrand(string name)
    {
        using var conn = _db.CreateConnection();
        long? id = conn.QueryFirstOrDefault<long?>(
            "SELECT id FROM brands WHERE name = @n", new { n = name.Trim() });
        if (id.HasValue) return id.Value;
        return conn.ExecuteScalar<long>(
            "INSERT INTO brands (name) VALUES (@n); SELECT last_insert_rowid();", new { n = name.Trim() });
    }

    public List<Category> GetCategories()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Category>("SELECT * FROM categories WHERE is_active = 1 ORDER BY name_en").ToList();
    }

    public List<ProductUnit> GetUnits()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<ProductUnit>("SELECT * FROM units ORDER BY name_en").ToList();
    }
}
