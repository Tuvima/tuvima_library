using MediaEngine.Api.Security;

namespace MediaEngine.Api.Tests;

/// <summary>
/// Tests for <see cref="PathValidator"/> — defence-in-depth against directory
/// traversal attacks on folder-related endpoints.
/// </summary>
public sealed class PathValidatorTests
{
    // ── Null / empty / whitespace ────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNullOrWhitespacePath(string? path)
    {
        var error = PathValidator.Validate(path);
        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Path traversal sequences ─────────────────────────────────────────────

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/home/user/../etc/shadow")]
    [InlineData("C:\\Users\\..\\Windows\\system32")]
    [InlineData("..\\Windows\\system32")]
    [InlineData("/safe/path/..")]
    public void Validate_RejectsTraversalSequences(string path)
    {
        var error = PathValidator.Validate(path);
        Assert.NotNull(error);
        Assert.Contains("..", error);
    }

    // ── Legitimate paths with dots that are NOT traversal ────────────────────

    [Theory]
    [InlineData("/home/user/my.library/books")]
    [InlineData("/home/user/v1.2.3/media")]
    [InlineData("C:\\Users\\shaya\\Documents\\Media Library")]
    public void Validate_AcceptsLegitimatePathsWithDots(string path)
    {
        var error = PathValidator.Validate(path);
        Assert.Null(error);
    }

    // ── System directory rejection (Linux) ───────────────────────────────────

    [Theory]
    [InlineData("/etc/something")]
    [InlineData("/usr/local/bin")]
    [InlineData("/bin/tools")]
    [InlineData("/sbin/scripts")]
    [InlineData("/boot/grub")]
    [InlineData("/proc/cpuinfo")]
    [InlineData("/sys/class")]
    [InlineData("/dev/null")]
    public void Validate_RejectsLinuxSystemPaths(string path)
    {
        if (OperatingSystem.IsWindows()) return; // skip on Windows

        var error = PathValidator.Validate(path);
        Assert.NotNull(error);
        Assert.Contains("system directory", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Valid paths pass through ─────────────────────────────────────────────

    [Theory]
    [InlineData("/home/user/media")]
    [InlineData("/mnt/nas/library")]
    [InlineData("/opt/tuvima/data")]
    public void Validate_AcceptsValidPaths(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        var error = PathValidator.Validate(path);
        Assert.Null(error);
    }
}
