using FluentAssertions;
using ZapretManager.Updates;

namespace ZapretManager.Tests.Updates;

/// <summary>
/// Tests for <see cref="UpdateChecker.IsNewerVersion"/> version comparison logic.
/// </summary>
public class UpdateCheckerTests
{
    // ── IsNewerVersion() ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.5.1", "2.5.0", true)]   // patch bump
    [InlineData("2.6.0", "2.5.0", true)]   // minor bump
    [InlineData("3.0.0", "2.5.0", true)]   // major bump
    [InlineData("2.5.0", "2.5.0", false)]  // same version
    [InlineData("2.4.9", "2.5.0", false)]  // older version
    [InlineData("2.5.0", null, false)]     // local null → IsNewerVersion returns false (both need values)
    [InlineData(null, "2.5.0", false)]     // no remote → no update
    [InlineData(null, null, false)]        // both null → no update
    public void IsNewerVersion_ReturnsExpected(string? remote, string? local, bool expected)
    {
        var result = UpdateChecker.IsNewerVersion(remote, local);
        result.Should().Be(expected);
    }


    [Theory]
    [InlineData("v2.5.1", "2.5.0", true)]    // remote has 'v' prefix
    [InlineData("V2.5.1", "2.5.0", true)]    // uppercase V prefix
    // NOTE: "v2.5.0" stripped becomes "2.5.0" which == "2.5.1" stripped → false (not newer)
    [InlineData("2.5.0", "v2.5.0", false)]   // same after stripping prefix
    public void IsNewerVersion_HandlesVPrefix(string? remote, string? local, bool expected)
    {
        var result = UpdateChecker.IsNewerVersion(remote, local);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "2.5.0", false)]    // empty remote
    [InlineData("2.5.0", "", false)]    // empty local
    public void IsNewerVersion_HandlesInvalidVersions(string? remote, string? local, bool expected)
    {
        // Should not throw on malformed version strings
        var act = () => UpdateChecker.IsNewerVersion(remote, local);
        act.Should().NotThrow();

        var result = UpdateChecker.IsNewerVersion(remote, local);
        result.Should().Be(expected);
    }

}

/// <summary>
/// Tests for <see cref="HashVerifier"/> — SHA256 verification logic.
/// </summary>
public class HashVerifierTests : IDisposable
{
    private readonly string _tempDir;

    public HashVerifierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zapret_hash_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task VerifyAsync_CorrectHash_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "test.bin");
        var content = "hello world"u8.ToArray();
        await File.WriteAllBytesAsync(path, content);

        // Compute the expected hash dynamically
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant();

        var result = await HashVerifier.VerifyAsync(path, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WrongHash_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "test.bin");
        await File.WriteAllTextAsync(path, "hello world");

        var result = await HashVerifier.VerifyAsync(path, new string('0', 64));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_EmptyExpected_ThrowsArgumentException()
    {
        var path = Path.Combine(_tempDir, "test.bin");
        await File.WriteAllTextAsync(path, "content");

        var act = async () => await HashVerifier.VerifyAsync(path, "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_SameHash()
    {
        var path1 = Path.Combine(_tempDir, "a.txt");
        var path2 = Path.Combine(_tempDir, "b.txt");
        await File.WriteAllTextAsync(path1, "same content");
        await File.WriteAllTextAsync(path2, "same content");

        var hash1 = await HashVerifier.ComputeHashAsync(path1);
        var hash2 = await HashVerifier.ComputeHashAsync(path2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_DifferentHash()
    {
        var path1 = Path.Combine(_tempDir, "a.txt");
        var path2 = Path.Combine(_tempDir, "b.txt");
        await File.WriteAllTextAsync(path1, "content A");
        await File.WriteAllTextAsync(path2, "content B");

        var hash1 = await HashVerifier.ComputeHashAsync(path1);
        var hash2 = await HashVerifier.ComputeHashAsync(path2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_ReturnsLowercaseHex()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(path, "test");

        var hash = await HashVerifier.ComputeHashAsync(path);

        hash.Should().MatchRegex("^[0-9a-f]{64}$",
            because: "SHA256 should be 64 lowercase hex chars");
    }
}
