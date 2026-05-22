using FluentAssertions;
using ZapretManager.Lists;

namespace ZapretManager.Tests.Lists;

/// <summary>
/// Tests for <see cref="ListMerger"/> — deduplication and merge logic.
/// </summary>
public class ListMergerTests : IDisposable
{
    private readonly string _tempDir;

    public ListMergerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zapret_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile(string name = "test.txt") => Path.Combine(_tempDir, name);

    // ── Merge() ──────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_NewFile_ReturnsNewLines()
    {
        var path = TempFile("nonexistent.txt"); // file does not exist
        var newLines = new[] { "example.com", "discord.com" };

        var result = ListMerger.Merge(path, newLines);

        result.Should().Contain("example.com").And.Contain("discord.com");
    }

    [Fact]
    public void Merge_ExistingFile_PreservesOldLines()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "old.com", "existing.com" });

        var result = ListMerger.Merge(path, new[] { "new.com" });

        result.Should().Contain("old.com")
              .And.Contain("existing.com")
              .And.Contain("new.com");
    }

    [Fact]
    public void Merge_RemovesDuplicates_CaseInsensitive()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "Example.COM", "discord.com" });

        var result = ListMerger.Merge(path, new[] { "example.com", "DISCORD.COM", "new.com" });

        result.Count(l => l.Trim().Equals("example.com", StringComparison.OrdinalIgnoreCase))
              .Should().Be(1, because: "duplicates (case-insensitive) should be removed");
        result.Should().Contain("new.com");
    }

    [Fact]
    public void Merge_PreservesComments()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "# my comment", "example.com" });

        var result = ListMerger.Merge(path, new[] { "new.com" });

        result.Should().Contain("# my comment");
    }

    [Fact]
    public void Merge_DeduplicatesComments()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "# comment" });

        var result = ListMerger.Merge(path, new[] { "# comment" });

        result.Count(l => l == "# comment").Should().Be(1,
            because: "same comment appearing twice should be deduplicated");
    }

    [Fact]
    public void Merge_EmptyLines_NotDuplicated()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "", "example.com", "" });

        var result = ListMerger.Merge(path, new[] { "", "new.com" });

        result.Count(string.IsNullOrEmpty).Should().BeLessOrEqualTo(2,
            because: "blank lines should be deduplicated as comments");
    }

    // ── ReplaceWithOrigin() ───────────────────────────────────────────────────

    [Fact]
    public void ReplaceWithOrigin_OriginEntriesAlwaysPresent()
    {
        var path = TempFile();
        File.WriteAllLines(path, new[] { "local-only.com", "shared.com" });

        var origin = new List<string> { "shared.com", "new-from-origin.com" };
        var result = ListMerger.ReplaceWithOrigin(path, origin);

        // Origin entries must always be in result
        result.Should().Contain("shared.com",
            because: "entries in origin must appear in the result");
        result.Should().Contain("new-from-origin.com",
            because: "new origin entries must be added");
        // Local-only entries are preserved as user additions (by design)
        result.Should().Contain("local-only.com",
            because: "locally-added entries not in origin are kept as user additions");
    }


    [Fact]
    public void ReplaceWithOrigin_PreservesUserAddedEntries()
    {
        var path = TempFile();
        // "user-added.com" is in local but NOT in origin → keep it
        File.WriteAllLines(path, new[] { "origin.com", "user-added.com" });

        var result = ListMerger.ReplaceWithOrigin(path, new[] { "origin.com" });

        result.Should().Contain("user-added.com",
            because: "user-only entries should be preserved");
    }

    // ── WriteUtf8() ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteUtf8_CreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "sub", "dir", "list.txt");

        ListMerger.WriteUtf8(nested, new[] { "example.com" });

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void WriteUtf8_WritesWithoutBom()
    {
        var path = TempFile();
        ListMerger.WriteUtf8(path, new[] { "example.com" });

        var bytes = File.ReadAllBytes(path);
        // UTF-8 BOM is EF BB BF — first 3 bytes must NOT be the BOM sequence
        if (bytes.Length >= 3)
        {
            var hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            hasBom.Should().BeFalse(because: "file should be UTF-8 without BOM");
        }
    }

    [Fact]
    public void WriteUtf8_RoundTrip_PreservesContent()
    {
        var path = TempFile();
        var lines = new[] { "discord.com", "youtube.com", "# comment" };

        ListMerger.WriteUtf8(path, lines);
        var read = File.ReadAllLines(path, System.Text.Encoding.UTF8);

        read.Should().BeEquivalentTo(lines);
    }
}
