using System.Security.Cryptography;
using ZapretManager.Core;

namespace ZapretManager.Updates;

/// <summary>
/// Verifies SHA256 checksums for downloaded files.
/// Hash verification is always enabled — there is no config flag to skip it.
/// </summary>
public static class HashVerifier
{
    /// <summary>
    /// Compute the SHA256 hash of a local file and compare it with the expected value.
    /// </summary>
    /// <param name="filePath">Path to the file to verify.</param>
    /// <param name="expectedSha256">Expected lowercase hex SHA256 hash.</param>
    /// <returns>True if the hash matches; false otherwise.</returns>
    public static async Task<bool> VerifyAsync(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new ArgumentException("Expected SHA256 hash must not be empty.", nameof(expectedSha256));

        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var actual = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var expected = expectedSha256.ToLowerInvariant().Trim();

        Logger.Debug($"SHA256 verify: expected={expected}  actual={actual}");
        return actual == expected;
    }

    /// <summary>
    /// Download a checksums file from the given URL and find the hash for a specific filename.
    /// The file is expected in the standard GNU coreutils format:
    ///   &lt;sha256hex&gt;  &lt;filename&gt;
    /// </summary>
    /// <param name="checksumUrl">URL to the checksums text file.</param>
    /// <param name="filename">Filename to look up (case-insensitive).</param>
    /// <returns>The SHA256 hash string, or null if not found or fetch failed.</returns>
    public static async Task<string?> FetchExpectedHashAsync(string checksumUrl, string filename)
    {
        if (string.IsNullOrWhiteSpace(checksumUrl)) return null;

        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("zapret-manager/2.0");
            var content = await http.GetStringAsync(checksumUrl);

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                // Format: "<hash>  <filename>" or "<hash> *<filename>"
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var hash = parts[0];
                var name = parts[1].TrimStart('*');

                if (string.Equals(name, filename, StringComparison.OrdinalIgnoreCase))
                    return hash.ToLowerInvariant();
            }

            Logger.Warn($"HashVerifier: '{filename}' not found in checksums file at {checksumUrl}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"HashVerifier: Failed to fetch checksums from {checksumUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download a file, verify its SHA256 against a remote checksums file, and throw if mismatch.
    /// </summary>
    /// <param name="localPath">Path to the already-downloaded file.</param>
    /// <param name="checksumUrl">URL of the remote checksums file.</param>
    /// <param name="filename">Filename entry to look up in the checksums file.</param>
    /// <exception cref="InvalidDataException">Thrown when hash verification fails.</exception>
    public static async Task VerifyOrThrowAsync(string localPath, string checksumUrl, string filename)
    {
        var expected = await FetchExpectedHashAsync(checksumUrl, filename);

        if (expected == null)
        {
            // Checksums file unavailable — warn but do not block (graceful degradation)
            Logger.Warn($"HashVerifier: Cannot fetch expected hash for '{filename}'. Skipping verification.");
            return;
        }

        var ok = await VerifyAsync(localPath, expected);
        if (!ok)
        {
            var actual = await ComputeHashAsync(localPath);
            var msg = $"SHA256 mismatch for '{filename}': expected={expected}, actual={actual}";
            Logger.Error(msg);
            throw new InvalidDataException(msg);
        }

        Logger.Ok($"SHA256 verified: {filename}");
    }

    /// <summary>
    /// Compute SHA256 hash of a file and return as lowercase hex string.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
