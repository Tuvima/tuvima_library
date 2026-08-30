using MediaEngine.AI.Configuration;
using MediaEngine.AI.Features;
using MediaEngine.AI.Infrastructure;
using MediaEngine.AI.Llama;
using MediaEngine.AI.Whisper;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.DependencyInjection;

public static class TuvimaAiServiceCollectionExtensions
{
    public static IServiceCollection AddTuvimaAi(
        this IServiceCollection services,
        IConfigurationLoader configLoader)
    {
        var settings = configLoader.LoadAi<AiSettings>()
            ?? throw new InvalidOperationException(
                "config/ai.json is required and must contain valid AI settings.");
        var modelsDirectory = Environment.GetEnvironmentVariable("TUVIMA_MODELS_DIR");
        if (!string.IsNullOrEmpty(modelsDirectory))
            settings.ModelsDirectory = modelsDirectory;

        AiSettingsValidator.ValidateAndThrow(settings);
        var gpuDetector = new GpuBackendDetector(NullLogger<GpuBackendDetector>.Instance);
        var detected = gpuDetector.Detect();
        var benchmarkStore = new AiBenchmarkStateStore(settings, NullLogger<AiBenchmarkStateStore>.Instance);
        settings.HardwareProfile = benchmarkStore.LoadCurrent(detected.Backend, detected.GpuName);
        settings.ApplyEffectiveResourceProfile();
        services.AddSingleton(settings);
        services.AddSingleton<AiConfigurationService>();
        services.AddSingleton<ModelInventory>();
        services.AddSingleton<AiModelSelectionAdvisor>();
        services.AddSingleton<AiFeatureGate>();
        services.AddSingleton(benchmarkStore);
        services.AddSingleton<IModelDownloadManager, ModelDownloadManager>();
        services.AddSingleton<IModelLifecycleManager, ModelLifecycleManager>();
        services.AddSingleton<LlamaInferenceService>();
        services.AddSingleton<ILlamaInferenceService>(sp =>
            sp.GetRequiredService<LlamaInferenceService>());
        services.AddSingleton<ITextInferenceService>(sp =>
            sp.GetRequiredService<LlamaInferenceService>());
        services.AddSingleton<WhisperInferenceService>();
        services.AddSingleton<IAudioTranscriptionService>(sp =>
            sp.GetRequiredService<WhisperInferenceService>());
        services.AddSingleton<AudioPreprocessor>();
        services.AddSingleton<ISmartLabeler, SmartLabeler>();
        services.AddSingleton<IMediaTypeAdvisor, MediaTypeAdvisor>();
        services.AddSingleton<ISeriesAligner, SeriesAligner>();
        services.AddSingleton<IVibeTagger, VibeTagger>();
        services.AddSingleton<ICoverArtHashService, CoverArtHashService>();
        services.AddSingleton<ITasteProfiler, TasteProfiler>();
        services.AddSingleton<IDescriptionIntelligenceService, DescriptionIntelligenceService>();
        services.AddSingleton(gpuDetector);
        services.AddSingleton<ResourceMonitorService>();
        services.AddSingleton<InteractiveRequestTracker>();
        services.AddSingleton<BackgroundAiAdmissionController>();
        services.AddSingleton<HardwareBenchmarkService>();
        return services;
    }
}
