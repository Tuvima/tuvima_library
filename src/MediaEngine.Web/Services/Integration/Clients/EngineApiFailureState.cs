using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Web.Services.Integration.Clients;

public sealed class EngineApiFailureState
{
    public string? LastError { get; private set; }
    public int? LastStatusCode { get; private set; }
    public string? LastFailedEndpoint { get; private set; }
    public string? LastFailureKind { get; private set; }

    public void Clear(string endpoint)
    {
        if (!string.Equals(LastFailedEndpoint, endpoint, StringComparison.Ordinal))
        {
            return;
        }

        LastError = null;
        LastStatusCode = null;
        LastFailedEndpoint = null;
        LastFailureKind = null;
    }

    public async Task RecordHttpFailureAsync(
        string endpoint,
        HttpResponseMessage response,
        ILogger logger,
        CancellationToken ct,
        bool logAsWarning = true)
    {
        var detail = await ReadProblemSummaryAsync(response, ct);
        LastStatusCode = (int)response.StatusCode;
        LastFailedEndpoint = endpoint;
        LastFailureKind = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "not_found",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "unauthorized",
            _ => "http_failure",
        };
        LastError = $"HTTP {(int)response.StatusCode}: {detail}";

        if (logAsWarning)
        {
            logger.LogWarning("{Endpoint} returned {Status}: {Detail}", endpoint, (int)response.StatusCode, detail);
        }
        else
        {
            logger.LogDebug("{Endpoint} returned {Status}: {Detail}", endpoint, (int)response.StatusCode, detail);
        }
    }

    public void RecordExceptionFailure(string endpoint, Exception ex, ILogger logger, bool logAsWarning = true)
    {
        LastStatusCode = null;
        LastFailedEndpoint = endpoint;
        LastFailureKind = ex is HttpRequestException ? "engine_unavailable" : "unexpected_failure";
        LastError = ex.Message;

        if (logAsWarning)
        {
            logger.LogWarning(ex, "{Endpoint} failed", endpoint);
        }
        else
        {
            logger.LogDebug(ex, "{Endpoint} failed", endpoint);
        }
    }

    public void RecordExceptionFailure(string endpoint, Exception ex)
    {
        LastStatusCode = null;
        LastFailedEndpoint = endpoint;
        LastFailureKind = ex is HttpRequestException ? "engine_unavailable" : "unexpected_failure";
        LastError = ex.Message;
    }

    public void RecordRawFailure(string endpoint, int statusCode, string detail)
    {
        LastStatusCode = statusCode;
        LastFailedEndpoint = endpoint;
        LastFailureKind = statusCode switch
        {
            404 => "not_found",
            401 or 403 => "unauthorized",
            _ => "http_failure",
        };
        LastError = $"HTTP {statusCode}: {detail}";
    }

    public void SetError(string? error)
    {
        LastError = error;
    }

    private static async Task<string> ReadProblemSummaryAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return response.ReasonPhrase ?? "Request failed";
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return raw;
            }

            var title = TryGetString(doc.RootElement, "title");
            var detail = TryGetString(doc.RootElement, "detail");
            var traceId = TryGetString(doc.RootElement, "traceId") ?? TryGetString(doc.RootElement, "trace_id");
            var parts = new[] { title, detail, string.IsNullOrWhiteSpace(traceId) ? null : $"Trace: {traceId}" }
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            var summary = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(summary) ? raw : summary;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
