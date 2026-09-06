namespace EchoLifestyle.Domain.Security;

/// <summary>
/// Hard separation between back-office users and shoppers.
///
/// This is deliberately NOT expressed as a role. Roles are mutable data, so a
/// mis-assigned role would otherwise be the only thing standing between a
/// customer account and cost prices, margins, suppliers and partner capital.
/// Every back-office authorization policy requires Staff in addition to the
/// specific permission, so a customer account holding a wrongly granted role
/// is still refused.
/// </summary>
public enum UserType
{
    /// <summary>Back-office user: owners, managers, POS operators, warehouse staff.</summary>
    Staff = 1,

    /// <summary>Storefront shopper. Can never hold back-office permissions.</summary>
    Customer = 2,
}
