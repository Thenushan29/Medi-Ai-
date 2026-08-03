namespace MediTrail.Api.Services;

/// <summary>
/// Object storage for original document files. Behind an interface so the Supabase implementation
/// can be swapped for local disk or blob storage by configuration (§14.2).
/// </summary>
public interface IStorageService
{
    /// <summary>Uploads bytes and returns the storage path that was written.</summary>
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>URL the browser can load the source image from, for the evidence viewer.</summary>
    string GetUrl(string path);

    /// <summary>Downloads the file back — used by the extraction worker.</summary>
    Task<byte[]> DownloadAsync(string path, CancellationToken ct = default);

    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Deletes every object under a prefix. Patient deletion must leave no orphaned files (§12.4).</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct = default);
}
