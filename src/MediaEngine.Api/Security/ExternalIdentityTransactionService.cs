using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Contracts.Authentication;

namespace MediaEngine.Api.Security;

public sealed record VerifiedExternalIdentity(
    string Purpose,
    string Provider,
    string Issuer,
    string Subject,
    string? Email,
    string? DisplayName,
    Guid? AccountId,
    Guid? SessionId,
    DateTimeOffset ExpiresAt);

public sealed class ExternalIdentityTransactionService(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, VerifiedExternalIdentity> transactions = new(StringComparer.Ordinal);

    public ExternalIdentityTransactionResponse Begin(
        BeginExternalIdentityTransactionRequest request,
        Guid? accountId,
        Guid? sessionId)
    {
        var purpose = request.Purpose.Trim();
        if (purpose is not (ExternalIdentityTransactionPurposes.SignIn or ExternalIdentityTransactionPurposes.Link))
        {
            throw new ArgumentException("External identity transaction purpose is invalid.");
        }

        if (purpose == ExternalIdentityTransactionPurposes.Link && (accountId is null || sessionId is null))
        {
            throw new UnauthorizedAccessException("A live account session is required to link an external identity.");
        }

        ValidateIdentity(request.Provider, request.Issuer, request.Subject);

        var now = clock.GetUtcNow();
        foreach (var expired in transactions.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key))
        {
            transactions.TryRemove(expired, out _);
        }

        var ticket = RandomToken();
        var expiresAt = now.Add(Lifetime);
        transactions[Hash(ticket)] = new VerifiedExternalIdentity(
            purpose,
            request.Provider.Trim(),
            request.Issuer.Trim(),
            request.Subject.Trim(),
            Clean(request.Email),
            Clean(request.DisplayName),
            purpose == ExternalIdentityTransactionPurposes.Link ? accountId : null,
            purpose == ExternalIdentityTransactionPurposes.Link ? sessionId : null,
            expiresAt);
        return new ExternalIdentityTransactionResponse(ticket, expiresAt);
    }

    public VerifiedExternalIdentity? Consume(
        string ticket,
        string expectedPurpose,
        Guid? accountId = null,
        Guid? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(ticket) ||
            !transactions.TryRemove(Hash(ticket.Trim()), out var transaction))
        {
            return null;
        }

        if (transaction.ExpiresAt <= clock.GetUtcNow() ||
            !transaction.Purpose.Equals(expectedPurpose, StringComparison.Ordinal) ||
            transaction.AccountId != accountId ||
            transaction.SessionId != sessionId)
        {
            return null;
        }

        return transaction;
    }

    private static void ValidateIdentity(string provider, string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (provider.Trim().Length > 100 || subject.Trim().Length > 300 ||
            issuer.Trim().Length > 300 ||
            !Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out var issuerUri) ||
            issuerUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("External identity is invalid.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
