namespace MediaEngine.Contracts.Ai;

public sealed record AiDownloadCancelledResponse(bool cancelled, string role);

public sealed record AiModelLoadedResponse(bool loaded, string role);

public sealed record AiModelUnloadedResponse(bool unloaded, string role);

public sealed record AiSettingsSavedResponse(bool saved);
