using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.Services;

/// <summary>
/// Supabase Storage over its REST API. Round 1 uses a public bucket, so <see cref="GetUrl"/> returns
/// a direct public URL; the production path (private bucket + signed URLs) is noted in §16.3.
/// </summary>
public sealed class SupabaseStorageService(
    HttpClient http,
    IOptions<SupabaseOptions> options,
    ILogger<SupabaseStorageService> logger) : IStorageService
{
    private readonly SupabaseOptions _options = options.Value;

    public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/storage/v1/object/{_options.Bucket}/{path}")
        {
            Content = body
        };
        // Original files are immutable (§12.2) — refuse to silently overwrite an existing object.
        request.Headers.Add("x-upsert", "false");

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new StorageException($"Upload of '{path}' failed ({(int)response.StatusCode}): {detail}");
        }

        logger.LogInformation("Uploaded {Path} to bucket {Bucket}", path, _options.Bucket);
        return path;
    }

    public string GetUrl(string path) =>
        _options.BucketIsPublic
            ? $"{_options.Url.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{path}"
            : $"{_options.Url.TrimEnd('/')}/storage/v1/object/{_options.Bucket}/{path}";

    public async Task<byte[]> DownloadAsync(string path, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/storage/v1/object/{_options.Bucket}/{path}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new StorageException($"Download of '{path}' failed ({(int)response.StatusCode}): {detail}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/storage/v1/object/{_options.Bucket}/{path}", ct);

        // Already gone is the desired end state, not an error.
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Delete of {Path} failed ({Status}): {Detail}", path, (int)response.StatusCode, detail);
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var listRequest = new { prefix, limit = 1000, offset = 0 };
        var listResponse = await http.PostAsJsonAsync($"/storage/v1/object/list/{_options.Bucket}", listRequest, ct);

        if (!listResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Could not list objects under {Prefix}; files may be orphaned", prefix);
            return;
        }

        var payload = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (payload.ValueKind != JsonValueKind.Array) return;

        var names = payload.EnumerateArray()
            .Select(o => o.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => $"{prefix.TrimEnd('/')}/{n}")
            .ToArray();

        if (names.Length == 0) return;

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/storage/v1/object/{_options.Bucket}")
        {
            Content = JsonContent.Create(new { prefixes = names })
        };
        var deleteResponse = await http.SendAsync(deleteRequest, ct);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Bulk delete under {Prefix} failed ({Status})", prefix, (int)deleteResponse.StatusCode);
        }
    }

    public async Task ProbeAsync(CancellationToken ct = default)
    {
        // Getting the bucket requires a key authorised for it, so this distinguishes
        // "bucket missing" from "wrong key" — the two mistakes people actually make.
        var response = await http.GetAsync($"/storage/v1/bucket/{_options.Bucket}", ct);

        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var detail = await response.Content.ReadAsStringAsync(ct);

        throw new StorageException(status switch
        {
            401 or 403 =>
                $"Storage rejected the key ({status}). Supabase uses a separate secret key for " +
                "server access — a publishable/anon key cannot reach the bucket.",
            404 =>
                $"Bucket '{_options.Bucket}' does not exist. Create it under Storage, or change " +
                "Supabase:Bucket to match an existing one.",
            _ => $"Storage probe failed ({status}): {detail}"
        });
    }
}

public sealed class StorageException(string message) : Exception(message);
