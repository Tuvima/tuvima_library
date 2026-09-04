using MediaEngine.Contracts.Setup;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<SetupStatusDto?> GetSetupStatusAsync(CancellationToken ct = default);
    Task<SetupStartResponse?> BeginSetupAsync(CancellationToken ct = default);
    Task<SetupPreflightDto?> RunSetupPreflightAsync(string? setupSession, CancellationToken ct = default);
    Task<SetupAdministratorResponse?> CreateSetupAdministratorAsync(SetupAdministratorRequest request, string setupSession, CancellationToken ct = default);
    Task<SetupMediaLocationsDto?> ValidateSetupMediaLocationsAsync(string? setupSession, CancellationToken ct = default);
    Task<SetupStatusDto?> DecideSetupStepAsync(string stepKey, string status, string? detail, string? setupSession, CancellationToken ct = default);
    Task<SetupBackupInspectionDto?> UploadSetupBackupAsync(Stream stream, string fileName, string setupSession, CancellationToken ct = default);
    Task<SetupRestoreConfirmationDto?> ConfirmSetupRestoreAsync(Guid operationId, string setupSession, CancellationToken ct = default);
    Task<SetupReadinessDto?> GetSetupReadinessAsync(string? setupSession, CancellationToken ct = default);
    Task<SetupStatusDto?> CompleteSetupAsync(string? setupSession, CancellationToken ct = default);
}
