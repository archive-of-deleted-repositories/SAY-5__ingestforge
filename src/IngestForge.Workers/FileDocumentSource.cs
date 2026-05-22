using Microsoft.Extensions.Options;

namespace IngestForge.Workers;

/// <summary>
/// Reads <c>file://</c> URIs (or bare paths) from disk, rooted under
/// <see cref="WorkerOptions.DocumentRoot"/>. Path traversal outside the
/// root is rejected.
/// </summary>
public sealed class FileDocumentSource : IDocumentSource
{
    private readonly string _root;

    public FileDocumentSource(IOptions<WorkerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.Value.DocumentRoot);
    }

    public Task<Stream> OpenAsync(string sourceUri, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);

        var path = sourceUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(sourceUri).LocalPath
            : sourceUri;

        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_root, path);
        }

        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"source_uri resolves outside document root: {sourceUri}");
        }

        Stream stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}
