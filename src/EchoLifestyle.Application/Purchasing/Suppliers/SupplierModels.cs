namespace EchoLifestyle.Application.Purchasing.Suppliers;

public class SupplierListItem
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? City { get; set; }

    public bool IsImporter { get; set; }

    public string CurrencyCode { get; set; } = "BDT";

    public int PaymentTermDays { get; set; }

    public bool IsActive { get; set; }
}

public class SupplierDetail
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public bool IsImporter { get; set; }

    public string CurrencyCode { get; set; } = "BDT";

    public int PaymentTermDays { get; set; }

    public string? Bin { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Batches received from this supplier. Drives whether deleting is offered
    /// - a supplier that has delivered stock is part of its history.
    /// </summary>
    public int BatchCount { get; set; }
}

public class SaveSupplierRequest
{
    /// <summary>Blank generates the next sequential code.</summary>
    public string? Code { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public bool IsImporter { get; set; }

    public string CurrencyCode { get; set; } = "BDT";

    public int PaymentTermDays { get; set; }

    public string? Bin { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Supplier id and name, for the purchase screens' dropdown.</summary>
public class SupplierOption
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsImporter { get; set; }

    public string CurrencyCode { get; set; } = "BDT";

    public bool IsActive { get; set; }

    public string Display => $"{Code} - {Name}";
}
