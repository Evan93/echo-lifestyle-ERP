using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Inventory.Counts;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Inventory;

/// <summary>Starting a count: where, how much of it, and when.</summary>
public class StartCountFormModel
{
    [Required(ErrorMessage = "Choose a warehouse.")]
    [Display(Name = "Warehouse")]
    public long WarehouseId { get; set; }

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Display(Name = "Counting on")]
    [DataType(DataType.Date)]
    public DateOnly? CountDate { get; set; }

    [Display(Name = "Count")]
    public StockCountScope Scope { get; set; } = StockCountScope.Everything;

    [Display(Name = "Which one")]
    public long? ScopeId { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IReadOnlyList<WarehouseOption> Warehouses { get; set; } = [];

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    public IReadOnlyList<CountScopeOption> Brands { get; set; } = [];

    public IReadOnlyList<CountScopeOption> Categories { get; set; } = [];

    public StartCountRequest ToRequest() => new()
    {
        WarehouseId = WarehouseId,
        BranchId = BranchId,
        CountDate = CountDate,
        Scope = Scope,
        ScopeId = Scope == StockCountScope.Everything ? null : ScopeId,
        Notes = Notes,
    };
}

/// <summary>
/// The sheet coming back from the browser.
///
/// Every line is posted, counted or not, so that clearing a figure back to blank
/// works. A blank is a real state - "nobody has been to that shelf" - and is not
/// the same as counting zero.
/// </summary>
public class CountSheetFormModel
{
    public long Id { get; set; }

    public List<CountSheetRow> Rows { get; set; } = [];

    public IReadOnlyList<CountEntryInput> ToEntries() =>
        Rows.Select(r => new CountEntryInput
        {
            LineId = r.LineId,
            CountedQuantity = r.CountedQuantity,
            Notes = r.Notes,
        })
        .ToList();
}

public class CountSheetRow
{
    public long LineId { get; set; }

    [Range(0, 9999999, ErrorMessage = "A shelf cannot hold a negative number.")]
    public decimal? CountedQuantity { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
