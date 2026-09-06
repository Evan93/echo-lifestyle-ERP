using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoLifestyle.Infrastructure.Files;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Absolute path files are written under. Set from the host - normally the
    /// web root - so this layer does not have to know what a web root is.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>URL prefix the stored paths are built from.</summary>
    public string PublicPrefix { get; set; } = "/uploads";

    /// <summary>Refused above this size, before anything is written.</summary>
    public long MaxBytes { get; set; } = 6 * 1024 * 1024;
}

/// <summary>
/// Files on the local disk, under the web root.
///
/// Right for one server and a few hundred product photographs. When the
/// storefront needs a CDN, that is a different IFileStorage and nothing else
/// changes.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<FileStorageOptions> options,
        IDateTimeProvider clock,
        ILogger<LocalFileStorage> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// The first bytes of each accepted format. Extensions and content-type
    /// headers are both supplied by the browser and neither is evidence of
    /// anything - a .jpg that is really a script is the oldest upload trick
    /// there is.
    /// </summary>
    private static readonly Dictionary<FileKind, byte[][]> Signatures = new()
    {
        [FileKind.Jpeg] = [[0xFF, 0xD8, 0xFF]],
        [FileKind.Png] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],

        // WebP is "RIFF" .... "WEBP"; the four size bytes in between are why
        // this one is checked in two pieces.
        [FileKind.WebP] = [[0x52, 0x49, 0x46, 0x46]],
    };

    private static readonly Dictionary<FileKind, string> Extensions = new()
    {
        [FileKind.Jpeg] = ".jpg",
        [FileKind.Png] = ".png",
        [FileKind.WebP] = ".webp",
    };

    public async Task<string> SaveAsync(
        Stream content,
        string folder,
        FileKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!Extensions.TryGetValue(kind, out var extension))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported file kind.");
        }

        // Buffered so the signature can be read and the length known before
        // anything touches the disk. Uploads are capped well below the point
        // where this matters.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("The uploaded file is empty.");
        }

        if (buffer.Length > _options.MaxBytes)
        {
            throw new InvalidOperationException(
                $"The file is larger than the {_options.MaxBytes / (1024 * 1024)} MB limit.");
        }

        buffer.Position = 0;

        if (!MatchesSignature(buffer, kind))
        {
            throw new InvalidOperationException(
                "That file is not the image type it claims to be.");
        }

        // Date-partitioned so no single directory grows without bound, and
        // named from a GUID so nothing the browser sent decides where the file
        // lands or what it is called.
        var business = _clock.BusinessNow;
        var relativeDirectory = Path.Combine(
            SanitiseSegment(folder),
            business.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            business.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));

        var fileName = Guid.NewGuid().ToString("N") + extension;

        var absoluteDirectory = Path.Combine(Root(), relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var absolutePath = Path.Combine(absoluteDirectory, fileName);

        buffer.Position = 0;

        await using (var file = new FileStream(
                         absolutePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await buffer.CopyToAsync(file, cancellationToken);
        }

        return $"{_options.PublicPrefix}/{relativeDirectory.Replace('\\', '/')}/{fileName}";
    }

    public Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return Task.CompletedTask;
        }

        var absolute = Resolve(storedPath);

        if (absolute is null)
        {
            // A path that resolves outside the store is either corrupt data or
            // an attempt at traversal. Neither is worth acting on, and both are
            // worth knowing about.
            _logger.LogWarning("Refused to delete {Path}: it is outside the file store.", storedPath);
            return Task.CompletedTask;
        }

        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (IOException ex)
        {
            // An orphaned file is untidy; a failed save because a file was
            // locked would be worse.
            _logger.LogWarning(ex, "Could not delete {Path}.", storedPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns a stored path back into an absolute one, or null when the result
    /// would escape the store.
    /// </summary>
    private string? Resolve(string storedPath)
    {
        if (!storedPath.StartsWith(_options.PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = storedPath[_options.PublicPrefix.Length..].TrimStart('/', '\\');

        var root = Path.GetFullPath(Root());
        var candidate = Path.GetFullPath(Path.Combine(root, relative));

        // The comparison is on the resolved paths, so "../../appsettings.json"
        // has already been collapsed by the time it is checked.
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private string Root()
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath))
        {
            throw new InvalidOperationException(
                "FileStorage:RootPath is not configured. The host sets this at startup.");
        }

        return Path.Combine(_options.RootPath, _options.PublicPrefix.TrimStart('/', '\\'));
    }

    private static bool MatchesSignature(Stream content, FileKind kind)
    {
        var header = new byte[12];
        var read = content.Read(header, 0, header.Length);

        if (read < 4)
        {
            return false;
        }

        if (!Signatures.TryGetValue(kind, out var expected))
        {
            return false;
        }

        var matchesPrefix = expected.Any(signature =>
            read >= signature.Length && header.Take(signature.Length).SequenceEqual(signature));

        if (!matchesPrefix)
        {
            return false;
        }

        // "RIFF" alone is also AVI and WAV, so WebP needs its second marker.
        if (kind == FileKind.WebP)
        {
            return read >= 12
                   && header[8] == 0x57 && header[9] == 0x45
                   && header[10] == 0x42 && header[11] == 0x50;
        }

        return true;
    }

    /// <summary>
    /// Folder names come from code, never from a request - this exists so that
    /// stays true even if someone wires one up carelessly later.
    /// </summary>
    private static string SanitiseSegment(string segment)
    {
        var cleaned = new string(segment
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            .ToArray());

        return cleaned.Length == 0 ? "misc" : cleaned;
    }
}
