using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Finance;

/// <summary>
/// Somebody who owns part of this business.
///
/// A separate record rather than a name typed onto a transaction, and separate
/// from the user account, because the three answer different questions. The
/// user account is who signed in. The partner is whose money it was. Those come
/// apart the first time one partner pays for something from the other's bKash,
/// and they come apart permanently if a partner ever leaves while their capital
/// account stays open.
///
/// Two rows today. The reason it is a table is that
/// "what has each of us actually put in?" is the one financial question a
/// two-person business asks constantly and can never reconstruct afterwards
/// from a free-text note.
/// </summary>
public class Partner : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Their staff account, when they have one. Optional: a silent partner puts
    /// money in and never signs in, and a partner who leaves keeps their capital
    /// history after the account is deactivated.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Their share, for reference only. Nothing calculates from it - profit
    /// distribution is a decision the partners make, not a formula the system
    /// applies - but it is the number somebody looks for on this screen.
    /// </summary>
    public decimal? OwnershipPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public string? Notes { get; set; }
}
