using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Detects the best available GPU backend for LLamaSharp inference.
/// Probe order: CUDA 12 (NVIDIA-optimised, preferred) → Vulkan (broad coverage) → CPU (fallback).
/// Detection is purely filesystem-based — no driver calls, no native P/Invoke.
/// Note: the Vulkan NuGet backend has been intentionally removed from the project because it
/// causes a fatal 0xC0000005 segfault on NVIDIA RTX hardware during llama_decode. CUDA 12 is
/// the active GPU backend. Vulkan probe logic is retained for informational detection only.
/// </summary>
public sealed class GpuBackendDetector
{
    private readonly ILogger<GpuBackendDetector> _logger;

    public GpuBackendDetector(ILogger<GpuBackendDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Whether a dedicated GPU was detected (not just integrated).
    /// Integrated GPUs (Intel UHD/Iris/HD) should not be used for AI inference — only for transcoding.
    /// Set after calling <see cref="Detect"/>.
    /// </summary>
    public bool HasDedicatedGpu { get; private set; }

    /// <summary>
    /// Detect the best available backend.
    /// Probe order: CUDA first (fastest for NVIDIA) → Vulkan (broad coverage) → CPU.
    /// Returns (backendName, gpuName) — e.g. ("cuda", "NVIDIA GeForce RTX 5080") or ("cpu", null).
    /// Also sets <see cref="HasDedicatedGpu"/> — false for integrated GPUs and no-GPU systems.
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
                _logger.LogInformation("GPU detected via CUDA: {GpuName} (dedicated)", realName);
                HasDedicatedGpu = true;
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
                // Check if the Vulkan GPU is Intel integrated — iGPUs should not be used for AI.
                bool isIntelIntegrated = vulkanGpu is not null && (
                    vulkanGpu.Contains("Intel",       StringComparison.OrdinalIgnoreCase) ||
                    vulkanGpu.Contains("UHD",         StringComparison.OrdinalIgnoreCase) ||
                    vulkanGpu.Contains("Iris",        StringComparison.OrdinalIgnoreCase) ||
                    vulkanGpu.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase));

                HasDedicatedGpu = !isIntelIntegrated;

                if (isIntelIntegrated)
                    _logger.LogInformation(
                        "GPU detected via Vulkan: {GpuName} (integrated — will use CPU for AI inference)",
                        vulkanGpu);
                else
                    _logger.LogInformation("GPU detected via Vulkan: {GpuName} (dedicated)", vulkanGpu);

                return ("vulkan", vulkanGpu);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Vulkan detection failed");
        }

        HasDedicatedGpu = false;
        _logger.LogInformation("No GPU detected — using CPU backend");
        return ("cpu", null);
    }

    /// <summary>
    /// Detect if the system only has an integrated GPU (no dedicated).
    /// Integrated GPUs should not be used for AI — only for transcoding.
    /// </summary>
    public bool IsIntegratedGpuOnly()
    {
        // If CUDA is available, it's a dedicated NVIDIA GPU.
        if (TryDetectCuda(out _)) return false;

        // Check nvidia-smi — if it works, dedicated NVIDIA present.
        if (QueryNvidiaSmi() is not null) return false;

        // If only Vulkan detected, check if it's Intel integrated.
        if (TryDetectVulkan(out var name))
        {
            if (name is not null && (
                name.Contains("Intel",       StringComparison.OrdinalIgnoreCase) ||
                name.Contains("UHD",         StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Iris",        StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Non-Intel Vulkan GPU — treat as dedicated.
            return false;
        }

        return true; // No GPU detected at all.
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
