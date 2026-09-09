using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class AccountSignInMethodRepository(IDatabaseConnection database)
    : IAccountSignInMethodRepository
{
    public Task<SignInMethodRemovalResult> RemoveExternalLoginAsync(
        Guid accountId,
        Guid loginId,
        CancellationToken ct = default) => database.ExecuteWriteAsync((connection, transaction, token) =>
    {
        token.ThrowIfCancellationRequested();
        var exists = connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM account_external_logins
            WHERE id=@loginId AND account_id=@accountId;
            """, new { accountId, loginId }, transaction) == 1;
        if (!exists)
        {
            return SignInMethodRemovalResult.NotFound;
        }

        if (CountOtherMethods(connection, transaction, accountId, "external", loginId, null) == 0)
        {
            return SignInMethodRemovalResult.LastSignInMethod;
        }

        connection.Execute("""
            DELETE FROM account_external_logins WHERE id=@loginId AND account_id=@accountId;
            """, new { accountId, loginId }, transaction);
        return SignInMethodRemovalResult.Removed;
    }, ct);

    public Task<SignInMethodRemovalResult> RemovePasskeyAsync(
        Guid accountId,
        byte[] credentialId,
        CancellationToken ct = default) => database.ExecuteWriteAsync((connection, transaction, token) =>
    {
        token.ThrowIfCancellationRequested();
        var exists = connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM account_passkeys
            WHERE credential_id=@credentialId AND account_id=@accountId;
            """, new { accountId, credentialId }, transaction) == 1;
        if (!exists)
        {
            return SignInMethodRemovalResult.NotFound;
        }

        if (CountOtherMethods(connection, transaction, accountId, "passkey", null, credentialId) == 0)
        {
            return SignInMethodRemovalResult.LastSignInMethod;
        }

        connection.Execute("""
            DELETE FROM account_passkeys WHERE credential_id=@credentialId AND account_id=@accountId;
            """, new { accountId, credentialId }, transaction);
        return SignInMethodRemovalResult.Removed;
    }, ct);

    private static int CountOtherMethods(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid accountId,
        string removing,
        Guid? loginId,
        byte[]? credentialId)
    {
        var password = connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM account_credentials
            WHERE account_id=@accountId AND credential_kind='Password';
            """, new { accountId }, transaction);
        var external = connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM account_external_logins
            WHERE account_id=@accountId AND (@removing<>'external' OR id<>@loginId);
            """, new { accountId, removing, loginId }, transaction);
        var passkeys = connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM account_passkeys
            WHERE account_id=@accountId
              AND (@removing<>'passkey' OR credential_id<>@credentialId);
            """, new { accountId, removing, credentialId }, transaction);
        return password + external + passkeys;
    }
}
