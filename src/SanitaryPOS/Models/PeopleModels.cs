namespace SanitaryPOS.Models;

public class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string CustomerType { get; set; } = "Regular"; // WalkIn|Regular|Plumber|Contractor
    public long CreditLimitPaisa { get; set; }
    public long OpeningBalancePaisa { get; set; }         // old udhaar when software starts
    public long IsActive { get; set; } = 1;
    public string CreatedAt { get; set; } = "";

    // Filled by CustomerService for list display
    public long BalancePaisa { get; set; }
    public bool IsOverCreditLimit => CreditLimitPaisa > 0 && BalancePaisa > CreditLimitPaisa;

    public override string ToString() => string.IsNullOrWhiteSpace(Mobile) ? Name : $"{Name} ({Mobile})";
}

/// <summary>Supplier model exists for Phase 2 (Purchases/Suppliers screens are placeholders in Phase 1).</summary>
public class Supplier
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContactPerson { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public long OpeningBalancePaisa { get; set; }
    public long IsActive { get; set; } = 1;
    public string CreatedAt { get; set; } = "";
    public override string ToString() => Name;
}

public class Payment
{
    public long Id { get; set; }
    public string PartyType { get; set; } = "Customer";   // Customer | Supplier
    public long PartyId { get; set; }
    public string Direction { get; set; } = "IN";         // IN = customer paid us, OUT = we paid supplier
    public long AmountPaisa { get; set; }
    public string PaymentMethod { get; set; } = "Cash";   // Cash|Card|Bank|JazzCash|EasyPaisa
    public long? SaleId { get; set; }
    public string? Note { get; set; }
    public long? UserId { get; set; }
    public string CreatedAt { get; set; } = "";
}

/// <summary>One row of a customer's khata (ledger) view.</summary>
public class LedgerEntry
{
    public string DateText { get; set; } = "";            // DD-MM-YYYY for display
    public string Description { get; set; } = "";
    public long DebitPaisa { get; set; }                  // bill amount (customer owes more)
    public long CreditPaisa { get; set; }                 // payment received (customer owes less)
    public long RunningBalancePaisa { get; set; }
}
