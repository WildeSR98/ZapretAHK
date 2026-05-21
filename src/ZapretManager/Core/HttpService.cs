using System.Net.Http;

namespace ZapretManager.Core;

/// <summary>
/// Centralized HTTP client factory. Avoids creating multiple HttpClient instances.
/// </summary>
public static class HttpService
{
    /// <summary>Standard HttpClient with default settings.</summary>
    public static HttpClient Client { get; } = CreateClient(validateSsl: true);

    /// <summary>Unsafe HttpClient that skips SSL validation (for diagnostics only).</summary>
    public static HttpClient UnsafeClient { get; } = CreateClient(validateSsl: false);

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
