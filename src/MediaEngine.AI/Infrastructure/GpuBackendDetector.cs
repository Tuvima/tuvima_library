using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Detects the best available GPU backend for LLamaSharp inference.
/// Probe order: Vulkan (broadest support) → CUDA 12 (NVIDIA-optimised) → CPU (fallback).
/// Detection is purely filesystem-based — no driver calls, no native P/Invoke.
/// </summary>
public sealed class GpuBackendDetector
{
    private readonly ILogger<GpuBackendDetector> _logger;

    public GpuBackendDetector(ILogger<GpuBackendDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect the best available backend.
    /// Probe order: CUDA first (fastest for NVIDIA) → Vulkan (broad coverage) → CPU.
    /// Returns (backendName, gpuName) — e.g. ("cuda", "NVIDIA GeForce RTX 5080") or ("cpu", null).
    /// </summary>
    public (string Backend, string? GpuName) Detect()
    {
        // Try CUDA first — fastest backend for NVIDIA GPUs.
        try
        {
            if (TryDetectCuda(out var cudaGpu))
            {
                // Try to get the real GPU name from nvidia-smi.
                var realName = QueryNvidiaSmi() ?? cudaGpu;
                _logger.LogInformation("GPU detected via CUDA: {GpuName}", realName);
                return ("cuda", realName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CUDA detection failed");
        }

        // Try Vulkan (covers AMD, Intel Arc, Intel integrated, and NVIDIA without CUDA).
        try
        {
            if (TryDetectVulkan(out var vulkanGpu))
            {
                _logger.LogInformation("GPU detected via Vulkan: {GpuName}", vulkanGpu);
                return ("vulkan", vulkanGpu);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Vulkan detection failed");
        }

        _logger.LogInformation("No GPU detected — using CPU backend");
        return ("cpu", null);
    }

    /// <summary>
    /// Query nvidia-smi for the real GPU name. Returns null if unavailable.
    /// </summary>
    private string? QueryNvidiaSmi()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n')[0].Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if the Vulkan runtime is present by looking for the loader library.
    /// Windows: vulkan-1.dll in System32. Linux: libvulkan.so.1 in standard lib paths.
    /// </summary>
    private bool TryDetectVulkan(out string? gpuName)
    {
        gpuName = null;

        if (OperatingSystem.IsWindows())
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(systemDir, "vulkan-1.dll")))
            {
                gpuName = "Vulkan-compatible GPU";
                return true;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/usr/lib/x86_64-linux-gnu/libvulkan.so.1") ||
                File.Exists("/usr/lib/libvulkan.so.1"))
            {
                gpuName = "Vulkan-compatible GPU";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if the CUDA 12 runtime is present by looking for the driver library.
    /// Windows: nvcuda.dll in System32. Linux: libcuda.so.1 in standard lib paths.
    /// </summary>
    private bool TryDetectCuda(out string? gpuName)
    {
        gpuName = null;

        if (OperatingSystem.IsWindows())
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(systemDir, "nvcuda.dll")))
            {
                gpuName = "NVIDIA CUDA GPU";
                return true;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/usr/lib/x86_64-linux-gnu/libcuda.so.1") ||
                File.Exists("/usr/lib/libcuda.so.1") ||
                File.Exists("/usr/local/cuda/lib64/libcudart.so"))
            {
                gpuName = "NVIDIA CUDA GPU";
                return true;
            }
        }

        return false;
    }
}
