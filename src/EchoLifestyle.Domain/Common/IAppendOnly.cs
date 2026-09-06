namespace EchoLifestyle.Domain.Common;

/// <summary>
/// Marks a record that may only ever be inserted.
///
/// The stock ledger is the truth about what the business owns; a balance is a
/// projection of it. An edited ledger row would silently change history and
/// every figure derived from it, with nothing to show that it happened - so
/// updates and deletes are refused by the save interceptor rather than left to
/// discipline. Corrections are reversing entries.
/// </summary>
public interface IAppendOnly
{
}
