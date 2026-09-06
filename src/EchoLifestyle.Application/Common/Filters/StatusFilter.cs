namespace EchoLifestyle.Application.Common.Filters;

/// <summary>
/// Which records a list should include, for anything that can be active or not.
///
/// An explicit three-way filter rather than a "show inactive" flag: a checkbox
/// left unticked hides rows silently, so a row disappearing after an edit reads
/// as a bug rather than as a filter doing its job. Shared across every admin
/// list so the behaviour is identical everywhere.
/// </summary>
public enum StatusFilter
{
    Active = 0,
    Inactive = 1,
    All = 2,
}
