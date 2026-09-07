using EmsPortal.Application.Abstractions.Storage;

namespace EmsPortal.Api.Storage;

/// <summary>
/// Local-disk <see cref="IFileStorage"/>: persists uploads under the host content root, at the
/// canonical path <see cref="StoragePaths"/> builds.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _contentRoot;

    public LocalFileStorage(IWebHostEnvironment environment)
        => _contentRoot = Path.GetFullPath(environment.ContentRootPath);

    public async Task<StoredFile> SaveAsync(StorageLocation location, string? originalFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var storedFileName = StoragePaths.FileNameFor(originalFileName, fileId);
        var relativeDir = StoragePaths.DirectoryFor(location);

        var absoluteDir = ResolveAbsolute(relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var absolutePath = Path.Combine(absoluteDir, storedFileName);
        await using (var stream = File.Create(absolutePath))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        return new StoredFile(fileId, $"{relativeDir}/{storedFileName}", storedFileName);
    }

    public Task<Stream?> OpenAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        if (!TryResolveAbsolute(storedPath, out var absolutePath) || !File.Exists(absolutePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(absolutePath));
    }

    public Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        if (TryResolveAbsolute(storedPath, out var absolutePath) && File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Absolute path for a stored relative path, or throws when it would land outside the content
    /// root.
    /// </summary>
    private string ResolveAbsolute(string storedPath)
        => TryResolveAbsolute(storedPath, out var absolutePath)
            ? absolutePath
            : throw new InvalidOperationException($"Storage path '{storedPath}' resolves outside the storage root.");

    private bool TryResolveAbsolute(string? storedPath, out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_contentRoot, storedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(_contentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        absolutePath = candidate;
        return true;
    }
}
