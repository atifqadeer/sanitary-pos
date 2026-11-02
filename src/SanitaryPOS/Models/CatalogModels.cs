namespace SanitaryPOS.Models;

// Property names match schema.sql column names (Dapper maps snake_case
// columns to PascalCase properties: Dapper's default underscored matching
// is enabled in DatabaseService).

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "Cashier";   // Admin | Cashier
    public long IsActive { get; set; } = 1;
    public string CreatedAt { get; set; } = "";
    public bool IsAdmin => Role == "Admin";
    /// <summary>Phase 3: bool view of IsActive for the Users grid checkbox column.</summary>
    public bool IsActiveBool => IsActive == 1;
}

public class Brand
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long IsActive { get; set; } = 1;
    public override string ToString() => Name;
}

public class Category
{
    public long Id { get; set; }
    public string NameEn { get; set; } = "";
    public string? NameUr { get; set; }
    public long IsActive { get; set; } = 1;
    public override string ToString() => NameEn;
}

public class ProductUnit
{
    public long Id { get; set; }
    public string NameEn { get; set; } = "";
    public string? NameUr { get; set; }
    public override string ToString() => NameEn;
}

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = "";              // always English
    public long? BrandId { get; set; }
    public long? CategoryId { get; set; }
    public string? ModelCode { get; set; }
    public string? Barcode { get; set; }
    public string? Finish { get; set; }                 // Chrome, Matte Black...
    public string? Size { get; set; }                   // 1/2", 3/4"...
    public long? UnitId { get; set; }

    // All rates in PAISA (see schema.sql)
    public long PurchaseRatePaisa { get; set; }
    public long RetailRatePaisa { get; set; }
    public long TradeRatePaisa { get; set; }
    public long WholesaleRatePaisa { get; set; }

    public double LowStockLevel { get; set; } = 2;
    public string Location { get; set; } = "Shop";      // Shop | Godown
    public long IsActive { get; set; } = 1;
    public string CreatedAt { get; set; } = "";

    // Joined display fields (filled by ProductService queries)
    public string? BrandName { get; set; }
    public string? CategoryName { get; set; }
    public string? UnitName { get; set; }
    public double StockQty { get; set; }
    public bool IsLowStock => StockQty <= LowStockLevel;

    /// <summary>Pick the selling rate for a price level: Retail | Trade | Wholesale.</summary>
    public long RateFor(string priceLevel) => priceLevel switch
    {
        "Trade" => TradeRatePaisa > 0 ? TradeRatePaisa : RetailRatePaisa,
        "Wholesale" => WholesaleRatePaisa > 0 ? WholesaleRatePaisa : RetailRatePaisa,
        _ => RetailRatePaisa
    };

    public override string ToString() => Name;
}

public class StockMovement
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string MovementType { get; set; } = "";  // Purchase|Sale|SaleReturn|PurchaseReturn|Damage|Adjustment|Opening
    public double Qty { get; set; }                 // + in, - out
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public string? Note { get; set; }
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";
}
