namespace EchoLifestyle.Application.Common.Files;

/// <summary>
/// Somewhere to put uploaded files.
///
/// The interface exists so that moving product photography behind a CDN or an
/// object store later is one implementation, not a change to every screen that
/// stores a path. Callers never choose a file name - the store does, from
/// content it has verified - so nothing a browser sent can decide where a file
/// lands.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Writes the content and returns the storage-relative path to reference it
    /// by, for example "/uploads/products/2026/09/6f1c....jpg".
    ///
    /// Never an absolute URL: the host changes, and stored absolute URLs would
    /// all have to be rewritten when it does.
    /// </summary>
    Task<string> SaveAsync(
        Stream content,
        string folder,
        FileKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a file previously returned by <see cref="SaveAsync"/>. Paths
    /// that fall outside the store are ignored rather than followed.
    /// </summary>
    Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// The file types the system accepts, and the extension each is stored under.
///
/// A closed set rather than a content-type string: the caller states what it
/// believes it has, the store proves it from the bytes, and nothing in between
/// can widen what is allowed.
/// </summary>
public enum FileKind
{
    Jpeg = 1,
    Png = 2,
    WebP = 3,
}
