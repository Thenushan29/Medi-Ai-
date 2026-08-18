using MediTrail.Api.Services;

namespace MediTrail.GoldenRunner.Traps;

/// <summary>
/// The object-storage port (§14.2) backed by a scratch directory.
///
/// This is the one thing the trap harness does not take from the deployed configuration. Supabase
/// Storage holds patient documents; a verification run that wrote sixteen of them into the live
/// bucket, uploaded twice per invocation, would leave real PHI behind for a test. Everything the
/// pipeline actually reasons with — the extractor, the merge, the rule checks, the cross-check —
/// is the production implementation, and the worker still round-trips every file through this
/// interface exactly as it does through Supabase.
/// </summary>
internal sealed class LocalDiskStorage(string root) : IStorageService
{
    public async Task<string> UploadAsync(
        string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);

        return path;
    }

    public string GetUrl(string path) => new Uri(Resolve(path)).AbsoluteUri;

    public Task<byte[]> DownloadAsync(string path, CancellationToken ct = default) =>
        File.ReadAllBytesAsync(Resolve(path), ct);

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        File.Delete(Resolve(path));
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var directory = Resolve(prefix);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    public Task ProbeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        return Task.CompletedTask;
    }

    private string Resolve(string path) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
}
