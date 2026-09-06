using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// How a product is counted. Cosmetics are almost entirely "Piece", so this is
/// a lookup rather than a conversion system: there is deliberately no factor,
/// no base unit and no unit arithmetic.
///
/// If a product is ever bought in cartons and sold in pieces, that is a
/// purchase-unit conversion on the purchase line, not a change here.
/// </summary>
public class UnitOfMeasure : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whole units only. Pieces cannot be sold in halves; grams can.
    /// </summary>
    public bool AllowsFractions { get; set; }

    public bool IsActive { get; set; } = true;
}
