using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Finance;

/// <summary>
/// What money was spent on.
///
/// Reference data with a small seeded starting set, editable because no list
/// written in advance survives contact with a real business - "Facebook
/// boosting" and "photographer" are not categories anybody would have guessed.
///
/// <see cref="IsCostOfSale"/> is the only structural distinction, and it earns
/// its place: packaging and courier charges rise with every parcel and belong
/// against gross margin, while rent does not. Without the flag, "are we making
/// money on a product?" and "are we making money as a business?" collapse into
/// the same wrong number.
/// </summary>
public class ExpenseCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>What belongs here, for whoever is choosing from the list.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// True when the cost rises with the volume sold - packaging, courier
    /// charges, payment fees. False for the costs of being open at all.
    /// </summary>
    public bool IsCostOfSale { get; set; }

    /// <summary>
    /// Seeded rows cannot be deleted or renamed away; they can only be
    /// deactivated. Otherwise an upgrade re-seeds a category somebody
    /// deliberately renamed, and last year's expenses split across two names.
    /// </summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
