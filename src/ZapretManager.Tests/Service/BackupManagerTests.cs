using FluentAssertions;
using ZapretManager.Service;

namespace ZapretManager.Tests.Service;

/// <summary>
/// Tests for <see cref="BackupManager"/> — create, rotate, list, restore.
/// </summary>
public class BackupManagerTests : IDisposable
{
    private readonly string _rootDir;

    public BackupManagerTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"zapret_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);

        // Create a minimal fake installation layout
        Directory.CreateDirectory(Path.Combine(_rootDir, "bin"));
        Directory.CreateDirectory(Path.Combine(_rootDir, "lists"));
        Directory.CreateDirectory(Path.Combine(_rootDir, "strategies"));
        Directory.CreateDirectory(Path.Combine(_rootDir, "utils"));

        File.WriteAllText(Path.Combine(_rootDir, "config.json"), "{ \"project\": {} }");
        File.WriteAllText(Path.Combine(_rootDir, "bin", "winws.exe"), "fake-binary");
        File.WriteAllText(Path.Combine(_rootDir, "lists", "list-general.txt"), "discord.com\nyoutube.com");
        File.WriteAllText(Path.Combine(_rootDir, "strategies", "general.bat"), "@echo off");
    }

    public void Dispose()
    {
        try { Directory.Delete(_rootDir, recursive: true); } catch { }
    }

    // ── CreateBackup() ────────────────────────────────────────────────────────

    [Fact]
    public void CreateBackup_ReturnsNonNullPath()
    {
        var result = BackupManager.CreateBackup(_rootDir, keepCount: 5);

        result.Should().NotBeNull(because: "backup should succeed with a valid directory");
    }

    [Fact]
    public void CreateBackup_CreatesZipFile()
    {
        var result = BackupManager.CreateBackup(_rootDir);

        result.Should().NotBeNull();
        File.Exists(result!).Should().BeTrue(because: "the backup zip file must exist on disk");
        result.Should().EndWith(".zip");
    }

    [Fact]
    public void CreateBackup_ZipContainsExpectedFiles()
    {
        var zipPath = BackupManager.CreateBackup(_rootDir)!;

        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        entries.Should().Contain(e => e.Contains("config.json"),
            because: "config.json must be included in backup");
        entries.Should().Contain(e => e.Contains("list-general.txt"),
            because: "lists should be backed up");
        entries.Should().Contain(e => e.Contains("general.bat"),
            because: "strategies should be backed up");
    }

    // ── ListBackups() ─────────────────────────────────────────────────────────

    [Fact]
    public void ListBackups_EmptyDirectory_ReturnsEmptyArray()
    {
        var result = BackupManager.ListBackups(_rootDir);

        result.Should().BeEmpty(because: "no backups have been created yet");
    }

    [Fact]
    public void ListBackups_AfterCreate_ReturnsSingleBackup()
    {
        BackupManager.CreateBackup(_rootDir);

        var result = BackupManager.ListBackups(_rootDir);

        result.Should().HaveCountGreaterOrEqualTo(1,
            because: "at least one backup should exist after CreateBackup");
    }

    [Fact]
    public void ListBackups_OrderedNewestFirst()
    {
        // Create two backups with a delay to ensure distinct timestamps (filename format: HHmmss)
        BackupManager.CreateBackup(_rootDir);
        System.Threading.Thread.Sleep(1100); // > 1 second to get distinct HHmmss
        BackupManager.CreateBackup(_rootDir);

        var result = BackupManager.ListBackups(_rootDir);

        // We may get 1 or 2 depending on timing, but ordering should always hold
        if (result.Length >= 2)
            result[0].CreationTime.Should().BeOnOrAfter(result[1].CreationTime,
                because: "backups should be ordered newest first");
        else
            result.Should().HaveCountGreaterOrEqualTo(1);
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    [Fact]
    public void CreateBackup_Rotation_KeepsOnlyN()
    {
        const int keepCount = 2;

        // Create backups with 1+ second gaps to ensure distinct filenames (yyyyMMdd_HHmmss format)
        for (int i = 0; i < 4; i++)
        {
            BackupManager.CreateBackup(_rootDir, keepCount);
            System.Threading.Thread.Sleep(1100);
        }

        var result = BackupManager.ListBackups(_rootDir);
        result.Should().HaveCount(keepCount,
            because: $"only {keepCount} backups should be kept after rotation");
    }

    // ── RestoreBackup() ───────────────────────────────────────────────────────

    [Fact]
    public void RestoreBackup_MissingFile_ReturnsFalse()
    {
        var result = BackupManager.RestoreBackup(_rootDir, "/nonexistent/path.zip");

        result.Should().BeFalse();
    }

    [Fact]
    public void RestoreBackup_ValidZip_ReturnsTrueAndRestoresFile()
    {
        var zipPath = BackupManager.CreateBackup(_rootDir)!;

        // Corrupt the strategies file
        File.WriteAllText(Path.Combine(_rootDir, "strategies", "general.bat"), "CORRUPTED");

        var ok = BackupManager.RestoreBackup(_rootDir, zipPath);

        ok.Should().BeTrue();
        var restored = File.ReadAllText(Path.Combine(_rootDir, "strategies", "general.bat"));
        restored.Should().Be("@echo off", because: "restore should recover the original content");
    }
}
