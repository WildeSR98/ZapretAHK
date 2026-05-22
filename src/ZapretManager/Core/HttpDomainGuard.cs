using ZapretManager.Core;

namespace ZapretManager.Core;

/// <summary>
/// Validates outgoing HTTP request URLs against a strict domain whitelist.
/// Only requests to known trusted domains are permitted.
/// </summary>
public static class HttpDomainGuard
{
    private static readonly HashSet<string> _allowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "codeload.github.com",           // GitHub archive downloads
        "hyperion-cs.github.io",         // DPI checker suite
        "speed.cloudflare.com",          // Speed test
        "api.cloudflare.com",
    };

    /// <summary>
    /// Validate that the given URL points to an allowed domain.
    /// </summary>
    /// <param name="url">The full URL to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if the host is not on the whitelist.</exception>
    public static void Validate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        Uri uri;
        try { uri = new Uri(url); }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException($"Malformed URL: {url}", ex);
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Only HTTPS is allowed. Got scheme '{uri.Scheme}' for URL: {url}");
        }

        if (!_allowedDomains.Contains(uri.Host))
        {
            Logger.Warn($"HttpDomainGuard: blocked request to non-whitelisted host '{uri.Host}'");
            throw new InvalidOperationException(
                $"Host '{uri.Host}' is not in the allowed domain list. URL: {url}");
        }
    }

    /// <summary>
    /// Returns true if the URL is valid and the host is on the whitelist.
    /// Does not throw — safe for conditional checks.
    /// </summary>
    public static bool IsAllowed(string url)
    {
        try { Validate(url); return true; }
        catch (Exception ex) { Logger.Error($"[HttpDomainGuard] {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Add a domain to the runtime whitelist (e.g. for custom mirror support).
    /// Does not persist between app restarts.
    /// </summary>
    public static void AddAllowedDomain(string domain)
    {
        _allowedDomains.Add(domain.ToLowerInvariant());
        Logger.Info($"HttpDomainGuard: added domain to whitelist: {domain}");
    }
}
