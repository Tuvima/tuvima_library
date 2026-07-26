using MediaEngine.AI.Configuration;
using MediaEngine.AI.Features;
using MediaEngine.AI.Infrastructure;
using MediaEngine.AI.Llama;
using MediaEngine.AI.Whisper;
using MediaEngine.Domain.Contracts;

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
        services.AddSingleton(settings);
        services.AddSingleton<ModelInventory>();
        services.AddSingleton<AiModelSelectionAdvisor>();
        services.AddSingleton<AiBenchmarkHarness>();
        services.AddSingleton<IAiBenchmarkModelRunner, LocalTextBenchmarkModelRunner>();
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
        services.AddSingleton<IQidDisambiguator, QidDisambiguator>();
        services.AddSingleton<ISeriesAligner, SeriesAligner>();
        services.AddSingleton<IWatchingOrderAdvisor, WatchingOrderAdvisor>();
        services.AddSingleton<IVibeTagger, VibeTagger>();
        services.AddSingleton<ITldrGenerator, TldrGenerator>();
        services.AddSingleton<ICoverArtValidator, CoverArtValidator>();
        services.AddSingleton<IAudioSimilarityService, AudioSimilarityService>();
        services.AddSingleton<ICoverArtHashService, CoverArtHashService>();
        services.AddSingleton<ITasteProfiler, TasteProfiler>();
        services.AddSingleton<IWhyExplainer, WhyExplainer>();
        services.AddSingleton<IIntentSearchParser, IntentSearchParser>();
        services.AddSingleton<IUrlMetadataExtractor, UrlMetadataExtractor>();
        services.AddSingleton<IDescriptionIntelligenceService, DescriptionIntelligenceService>();
        services.AddSingleton<GpuBackendDetector>();
        services.AddSingleton<ResourceMonitorService>();
        services.AddSingleton<HardwareBenchmarkService>();
        return services;
    }
}
