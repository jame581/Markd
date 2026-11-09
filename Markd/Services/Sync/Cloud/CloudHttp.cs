using System.Net;
using System.Text.Json;
using Markd.Core.Sync;

namespace Markd.Services.Sync.Cloud;

/// <summary>A cloud API response, read in full.</summary>
internal sealed record CloudResponse(HttpStatusCode Status, byte[] Body, string? ETag, Uri? Location);

/// <summary>HTTP and JSON plumbing shared by the cloud sync providers.</summary>
internal static class CloudHttp
{
    /// <summary>
    /// Sends the request as is (disposing it) and reads the whole response. Never adds a token. Transport failures and
    /// HttpClient's own timeout become <see cref="SyncOfflineException"/>; the caller's cancellation passes through.
    /// </summary>
    public static async Task<CloudResponse> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using (request)
            {
                using var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsByteArrayAsync(ct);
                var etag = response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;
                return new CloudResponse(response.StatusCode, body, etag, response.Headers.Location);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient's own timeout, not the caller.
            throw new SyncOfflineException(null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncOfflineException(null, ex);
        }
    }

    public static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and <= 299;

    /// <summary>The JSON object in the body, or an empty object when there is none or it is not JSON.</summary>
    public static JsonElement Parse(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.Length == 0 ? "{}"u8.ToArray() : body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document.RootElement.Clone();
        }
        catch (JsonException)
        {
        }

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    public static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
