namespace MediaEngine.Domain.Contracts;

public enum SignInMethodRemovalResult
{
    Removed,
    NotFound,
    LastSignInMethod,
}

public interface IAccountSignInMethodRepository
{
    Task<SignInMethodRemovalResult> RemoveExternalLoginAsync(
        Guid accountId,
        Guid loginId,
        CancellationToken ct = default);

    Task<SignInMethodRemovalResult> RemovePasskeyAsync(
        Guid accountId,
        byte[] credentialId,
        CancellationToken ct = default);
}
