using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Tracks available and downloaded AI models by scanning the models directory.
/// </summary>
public sealed class ModelInventory
{
    private readonly AiSettings _settings;
    private readonly ILogger<ModelInventory> _logger;
    private readonly Dictionary<AiModelRole, AiModelState> _states = new();
    private readonly object _lock = new();

    public ModelInventory(AiSettings settings, ILogger<ModelInventory> logger)
    {
        _settings = settings;
        _logger = logger;
        Refresh();
    }

    /// <summary>Scan the models directory and update state for all roles.</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            foreach (var role in Enum.GetValues<AiModelRole>())
            {
                var definition = _settings.Models.GetByRole(role);
                var modelPath = GetModelPath(role);

                if (string.IsNullOrEmpty(definition.File))
                {
                    _states[role] = AiModelState.NotDownloaded;
                }
                else if (File.Exists(modelPath))
                {
                    _states[role] = AiModelState.Ready;
                    _logger.LogInformation("Model {Role} found at {Path}", role, modelPath);
                }
                else
                {
                    _states[role] = AiModelState.NotDownloaded;
                    _logger.LogInformation("Model {Role} not found at {Path}", role, modelPath);
                }
            }
        }
    }

    /// <summary>Get the current state of a model role.</summary>
    public AiModelState GetState(AiModelRole role)
    {
        lock (_lock)
        {
            return _states.GetValueOrDefault(role, AiModelState.NotDownloaded);
        }
    }

    /// <summary>Set the state of a model role (used by download/lifecycle managers).</summary>
    public void SetState(AiModelRole role, AiModelState state)
    {
        lock (_lock)
        {
            _states[role] = state;
        }
    }

    /// <summary>Get the full file path for a model role.</summary>
    public string GetModelPath(AiModelRole role)
    {
        var definition = _settings.Models.GetByRole(role);
        var subdirectory = role == AiModelRole.Audio ? "whisper" : "llama";
        return Path.Combine(_settings.ModelsDirectory, subdirectory, definition.File);
    }

    /// <summary>Get the model definition for a role.</summary>
    public AiModelDefinition GetDefinition(AiModelRole role) => _settings.Models.GetByRole(role);

    /// <summary>Check if all required models are ready (downloaded and verified).</summary>
    public bool AreAllReady()
    {
        lock (_lock)
        {
            return Enum.GetValues<AiModelRole>().All(r => _states.GetValueOrDefault(r) == AiModelState.Ready);
        }
    }

    /// <summary>Get status for all model roles.</summary>
    public IReadOnlyList<AiModelStatus> GetAllStatuses()
    {
        lock (_lock)
        {
            return Enum.GetValues<AiModelRole>().Select(role =>
            {
                var def = _settings.Models.GetByRole(role);
                return new AiModelStatus
                {
                    Role = role,
                    ModelType = role == AiModelRole.Audio ? AiModelType.Audio : AiModelType.Text,
                    State = _states.GetValueOrDefault(role, AiModelState.NotDownloaded),
                    ModelFile = def.File,
                    SizeMB = def.SizeMB,
                };
            }).ToList();
        }
    }
}
