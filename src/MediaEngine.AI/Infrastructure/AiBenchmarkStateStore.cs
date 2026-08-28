using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.AI.Configuration;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>Persists benchmark results beside model storage, never in committed configuration.</summary>
public sealed class AiBenchmarkStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _statePath;
    private readonly ILogger<AiBenchmarkStateStore> _logger;

    public AiBenchmarkStateStore(AiSettings settings, ILogger<AiBenchmarkStateStore> logger)
    {
        _statePath = Path.Combine(Path.GetFullPath(settings.ModelsDirectory), ".tuvima", "hardware-benchmark.json");
        _logger = logger;
    }

    public HardwareProfile LoadCurrent(string backend, string? gpuName)
    {
        var fingerprint = CreateMachineFingerprint(backend, gpuName);
        try
        {
            if (!File.Exists(_statePath))
                return NewProfile(fingerprint, backend, gpuName);

            var profile = JsonSerializer.Deserialize<HardwareProfile>(File.ReadAllText(_statePath), JsonOptions)
                ?? NewProfile(fingerprint, backend, gpuName);
            if (!string.Equals(profile.MachineFingerprint, fingerprint, StringComparison.Ordinal)
                || !string.Equals(profile.BenchmarkVersion, "v2", StringComparison.Ordinal))
            {
                profile.Outcome = AiBenchmarkOutcomes.Invalidated;
                profile.TokensPerSecond = null;
                profile.Tier = "auto";
                profile.MachineFingerprint = fingerprint;
                profile.Backend = backend;
                profile.GpuName = gpuName;
                profile.AvailableRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                profile.FailureCode = "machine_changed";
                profile.FailureMessage = "The saved benchmark was produced by a different machine or benchmark version.";
                Save(profile);
            }

            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the machine-local AI benchmark state");
            return NewProfile(fingerprint, backend, gpuName);
        }
    }

    public void Save(HardwareProfile profile)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = _statePath + ".writing";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(tempPath, _statePath, overwrite: true);
    }

    public HardwareProfile Invalidate(string backend, string? gpuName)
    {
        var profile = NewProfile(CreateMachineFingerprint(backend, gpuName), backend, gpuName);
        profile.Outcome = AiBenchmarkOutcomes.Invalidated;
        profile.FailureCode = "manually_invalidated";
        profile.FailureMessage = "The administrator invalidated this benchmark.";
        Save(profile);
        return profile;
    }

    public static string CreateMachineFingerprint(string backend, string? gpuName)
    {
        var identity = string.Join('|',
            GetMachineIdentity(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            Environment.ProcessorCount,
            backend,
            gpuName ?? "none");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static HardwareProfile NewProfile(string fingerprint, string backend, string? gpuName) => new()
    {
        MachineFingerprint = fingerprint,
        Backend = backend,
        GpuName = gpuName,
        Outcome = AiBenchmarkOutcomes.NotRun,
        Tier = "auto",
        AvailableRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
    };

    private static string GetMachineIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                if (File.Exists("/etc/machine-id"))
                    return File.ReadAllText("/etc/machine-id").Trim();
            }
            catch (IOException)
            {
                // Machine name remains a safe fallback when the OS identifier is unavailable.
            }
        }

        return Environment.MachineName;
    }
}
