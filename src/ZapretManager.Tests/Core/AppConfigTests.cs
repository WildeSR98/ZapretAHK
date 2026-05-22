using FluentAssertions;
using ZapretManager.Core;

namespace ZapretManager.Tests.Core;

/// <summary>
/// Tests for <see cref="AppConfig"/> loading behaviour.
/// </summary>
public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zapret_cfg_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Load() ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var cfg = AppConfig.Load(_tempDir);

        cfg.Should().NotBeNull();
        cfg.Project.Should().NotBeNull();
        cfg.Repositories.Should().NotBeNull();
        cfg.Features.Should().NotBeNull();
    }

    [Fact]
    public void Load_ValidJson_DeserializesCorrectly()
    {
        var json = """
        {
          "project": { "name": "Test", "version": "1.2.3" },
          "features": { "verbose_logging": true, "parallel_downloads": false }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);

        var cfg = AppConfig.Load(_tempDir);

        cfg.Project.Version.Should().Be("1.2.3");
        cfg.Project.Name.Should().Be("Test");
        cfg.Features.VerboseLogging.Should().BeTrue();
        cfg.Features.ParallelDownloads.Should().BeFalse();
    }

    [Fact]
    public void Load_BrokenJson_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ broken json {{{}}}");

        // Should not throw — must return safe defaults
        var act = () => AppConfig.Load(_tempDir);
        act.Should().NotThrow();
    }

    [Fact]
    public void Load_EmptyJson_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{}");

        var cfg = AppConfig.Load(_tempDir);
        cfg.Should().NotBeNull();
        cfg.Backup.KeepCount.Should().BeGreaterThan(0, because: "default KeepCount must be positive");
    }

    [Fact]
    public void Load_DiagnosticsConfig_ParsedCorrectly()
    {
        var json = """
        {
          "diagnostics": {
            "check_targets": [
              { "name": "discord.com", "type": "url", "url": "https://discord.com", "host": "discord.com" }
            ],
            "conflicting_services": ["GoodbyeDPI"]
          }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);

        var cfg = AppConfig.Load(_tempDir);

        cfg.Diagnostics.CheckTargets.Should().HaveCount(1);
        cfg.Diagnostics.CheckTargets[0].Name.Should().Be("discord.com");
        cfg.Diagnostics.ConflictingServices.Should().Contain("GoodbyeDPI");
    }

    [Fact]
    public void Load_WatchdogConfig_HasSensibleDefaults()
    {
        var cfg = AppConfig.Load(_tempDir);

        cfg.Watchdog.CheckIntervalMinutes.Should().BeGreaterThan(0);
        cfg.Watchdog.FailThreshold.Should().BeGreaterThan(0);
        cfg.Watchdog.CooldownMinutes.Should().BeGreaterThan(0);
    }
}
