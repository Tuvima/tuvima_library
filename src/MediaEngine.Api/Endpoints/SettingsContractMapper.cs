using System.Text.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Configuration;
using ContractLibraryPreferences = MediaEngine.Contracts.Settings.LibraryPreferencesSettings;
using ContractPipelineConfiguration = MediaEngine.Contracts.Settings.PipelineConfiguration;
using ContractTranscodingSettings = MediaEngine.Contracts.Settings.TranscodingSettings;
using StorageLibraryPreferences = MediaEngine.Domain.Configuration.LibraryPreferencesSettings;
using StoragePipelineConfiguration = MediaEngine.Domain.Configuration.PipelineConfiguration;
using StorageTranscodingSettings = MediaEngine.Domain.Configuration.TranscodingSettings;

namespace MediaEngine.Api.Endpoints;

internal static class SettingsContractMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static MediaTypeConfigurationDto ToContract(MediaTypeConfiguration value) =>
        Convert<MediaTypeConfigurationDto>(value);

    internal static MediaTypeConfiguration ToStorage(MediaTypeConfigurationDto value) =>
        Convert<MediaTypeConfiguration>(value);

    internal static MediaTypeDefinition ToStorage(MediaTypeDefinitionDto value) =>
        Convert<MediaTypeDefinition>(value);

    internal static HydrationSettingsDto ToContract(HydrationSettings value) =>
        Convert<HydrationSettingsDto>(value);

    internal static HydrationSettings ToStorage(HydrationSettingsDto value) =>
        Convert<HydrationSettings>(value);

    internal static ContractTranscodingSettings ToContract(StorageTranscodingSettings value) =>
        Convert<ContractTranscodingSettings>(value);

    internal static StorageTranscodingSettings ToStorage(ContractTranscodingSettings value) =>
        Convert<StorageTranscodingSettings>(value);

    internal static ContractPipelineConfiguration ToContract(StoragePipelineConfiguration value) =>
        Convert<ContractPipelineConfiguration>(value);

    internal static StoragePipelineConfiguration ToStorage(ContractPipelineConfiguration value) =>
        Convert<StoragePipelineConfiguration>(value);

    internal static ContractLibraryPreferences ToContract(StorageLibraryPreferences value) =>
        Convert<ContractLibraryPreferences>(value);

    internal static StorageLibraryPreferences ToStorage(ContractLibraryPreferences value) =>
        Convert<StorageLibraryPreferences>(value);

    internal static UIGlobalSettingsDto ToContract(UIGlobalSettings value) =>
        Convert<UIGlobalSettingsDto>(value);

    internal static UIGlobalSettings ToStorage(UIGlobalSettingsDto value) =>
        Convert<UIGlobalSettings>(value);

    internal static UIDeviceProfileDto ToContract(UIDeviceProfile value) =>
        Convert<UIDeviceProfileDto>(value);

    internal static UIDeviceProfile ToStorage(UIDeviceProfileDto value) =>
        Convert<UIDeviceProfile>(value);

    internal static UIProfileSettingsDto ToContract(UIProfileSettings value) =>
        Convert<UIProfileSettingsDto>(value);

    internal static UIProfileSettings ToStorage(UIProfileSettingsDto value) =>
        Convert<UIProfileSettings>(value);

    internal static ResolvedUISettingsDto ToContract(ResolvedUISettings value) =>
        Convert<ResolvedUISettingsDto>(value);

    internal static LibrariesConfigurationDto ToContract(LibrariesConfiguration value) =>
        Convert<LibrariesConfigurationDto>(value);

    internal static LibrariesConfiguration ToStorage(UpdateLibrariesRequest value)
    {
        var result = Convert<LibrariesConfiguration>(value);
        result.Libraries ??= [];
        result.IncomingSources ??= [];
        result.PersonalLibraryPolicy ??= new PersonalLibraryPolicyConfig();
        foreach (var library in result.Libraries)
        {
            library.MediaTypes ??= [];
            library.Sources ??= [];
            library.AuthorizedProfileIds ??= [];
            library.AcceptedIntakeModes ??= [];
        }

        return result;
    }

    internal static IncomingSourceDto ToContract(IncomingSourceConfig value) =>
        Convert<IncomingSourceDto>(value);

    internal static IncomingSourceConfig ToStorage(IncomingSourceDto value) =>
        Convert<IncomingSourceConfig>(value);

    private static TTarget Convert<TTarget>(object source) =>
        JsonSerializer.Deserialize<TTarget>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions)
        ?? throw new InvalidOperationException(
            $"Could not map settings value to {typeof(TTarget).FullName}.");
}
