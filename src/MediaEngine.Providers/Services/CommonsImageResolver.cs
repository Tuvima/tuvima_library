using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

public sealed class CommonsImageResolver
{
    private readonly ReconciliationProviderConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CommonsImageResolver> _logger;

    public CommonsImageResolver(
        ReconciliationProviderConfig config,
        IHttpClientFactory httpFactory,
        ILogger<CommonsImageResolver> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<string?> ResolveAndDownloadPersonImageAsync(
        string providerName,
        string commonsFilename,
        string personFolderPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commonsFilename) || string.IsNullOrWhiteSpace(personFolderPath))
            return null;

        try
        {
            var encodedName = Uri.EscapeDataString(commonsFilename.Replace(' ', '_'));
            var url = _config.Endpoints.CommonsFilePath + encodedName;
            var ext = Path.GetExtension(commonsFilename).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                ext = ".jpg";

            using var client = _httpFactory.CreateClient("headshot_download");
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var bytes = await BoundedHttpContent.ReadImageAsync(response.Content, ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            Directory.CreateDirectory(personFolderPath);
            var destPath = Path.Combine(personFolderPath, $"headshot{ext}");
            await BoundedHttpContent.WriteFileAtomicallyAsync(destPath, bytes, ct).ConfigureAwait(false);

            _logger.LogInformation("{Provider}: downloaded headshot to {Path}", providerName, destPath);
            return destPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: failed to download headshot '{Filename}'",
                providerName, commonsFilename);
            return null;
        }
    }
}
