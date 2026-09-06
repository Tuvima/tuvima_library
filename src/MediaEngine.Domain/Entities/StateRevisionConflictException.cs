namespace MediaEngine.Domain.Entities;

public sealed class StateRevisionConflictException() : InvalidOperationException("Progress changed on another session. Refresh before changing its status.");
