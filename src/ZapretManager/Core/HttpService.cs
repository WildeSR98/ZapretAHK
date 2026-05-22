using System.Net.Http;
using ZapretManager.Core;

namespace ZapretManager.Core;

/// <summary>
/// Centralized HTTP client factory. Avoids creating multiple HttpClient instances.
/// </summary>
public static class HttpService
{
    /// <summary>Allowed outbound hosts (domain whitelist).</summary>
    private static readonly HashSet<string> _allowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "hyperion-cs.github.io",
        "cloudflare.com",
        "speed.cloudflare.com",
        "1.1.1.1",
    };

    /// <summary>Standard HttpClient with default settings.</summary>
    public static HttpClient Client { get; } = CreateClient(validateSsl: true);

    /// <summary>Unsafe HttpClient that skips SSL validation (for diagnostics only).</summary>
    public static HttpClient UnsafeClient { get; } = CreateClient(validateSsl: false);

    /// <summary>
    /// Validates that the URL targets an allowed domain.
    /// Throws <see cref="InvalidOperationException"/> for disallowed hosts.
    /// </summary>
    public static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL cannot be empty", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));
        var host = uri.Host.ToLowerInvariant();
        if (!_allowedDomains.Contains(host))
            throw new InvalidOperationException($"Disallowed host: {host}. Only GitHub and allowed CDN hosts are permitted.");
    }

    private static HttpClient CreateClient(bool validateSsl)
    {
        HttpClientHandler handler;
        if (validateSsl)
        {
            handler = new HttpClientHandler { AllowAutoRedirect = true };
        }
        else
        {
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
        }

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("zapret-manager/2.4");
        return client;
    }
}
