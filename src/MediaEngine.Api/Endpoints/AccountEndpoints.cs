using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Contracts.Profiles;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace MediaEngine.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts").RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/me", async (ClaimsPrincipal user, IAccountRepository accounts, CancellationToken ct) =>
        {
            var accountId = RequiredAccountId(user);
            var account = await accounts.GetByIdAsync(accountId, ct).ConfigureAwait(false);
            return account is null ? ApiErrors.NotFound("Account not found.") : Results.Ok(await ToResponseAsync(account, accounts, ct).ConfigureAwait(false));
        }).Produces<AccountResponse>();

        group.MapGet("/me/external-logins", async (ClaimsPrincipal user, IAccountExternalLoginService logins, CancellationToken ct) =>
            Results.Ok((await logins.GetByAccountAsync(RequiredAccountId(user), ct).ConfigureAwait(false)).Select(ProfileContractMapper.ToResponse).ToList()))
            .Produces<List<AccountExternalLoginDto>>();

        group.MapPost("/me/external-logins", async (ClaimsPrincipal user, LinkAccountExternalLoginRequest request, IAccountExternalLoginService logins, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(ProfileContractMapper.ToResponse(await logins.LinkAsync(RequiredAccountId(user), request.Provider, request.Issuer, request.Subject, request.Email, request.DisplayName, ct).ConfigureAwait(false)));
            }
            catch (ArgumentException ex) { return ApiErrors.BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return ApiErrors.Conflict(ex.Message); }
        }).Produces<AccountExternalLoginDto>();

        group.MapDelete("/me/external-logins/{loginId:guid}", async (
            Guid loginId,
            ClaimsPrincipal user,
            IAccountExternalLoginService logins,
            IIdentityRepository identities,
            IAccountRepository accounts,
            UserManager<Account> users,
            CancellationToken ct) =>
        {
            var accountId = RequiredAccountId(user);
            var existing = await logins.GetByAccountAsync(accountId, ct).ConfigureAwait(false);
            var owned = existing.Any(login => login.Id == loginId);
            if (!owned) return ApiErrors.NotFound("External login not found.");

            var hasPassword = await identities.GetAccountCredentialAsync(
                accountId,
                AccountCredentialKind.Password,
                ct).ConfigureAwait(false) is not null;
            var account = await accounts.GetByIdAsync(accountId, ct).ConfigureAwait(false);
            var hasPasskey = account is not null
                && (await users.GetPasskeysAsync(account).ConfigureAwait(false)).Count > 0;
            if (existing.Count == 1 && !hasPassword && !hasPasskey)
                return ApiErrors.Conflict("Add another sign-in method before removing this provider.");

            return await logins.UnlinkAsync(loginId, ct).ConfigureAwait(false) ? Results.NoContent() : ApiErrors.NotFound("External login not found.");
        }).WithName("UnlinkAccountExternalLogin").Produces(StatusCodes.Status204NoContent);

        group.MapGet("/", async (IAccountRepository accounts, CancellationToken ct) =>
        {
            var all = await accounts.GetAllAsync(ct).ConfigureAwait(false);
            var result = new List<AccountResponse>(all.Count);
            foreach (var account in all) result.Add(await ToResponseAsync(account, accounts, ct).ConfigureAwait(false));
            return Results.Ok(result);
        }).RequireAuthorization(AuthPolicies.Administrator).Produces<List<AccountResponse>>();

        group.MapPost("/", async (CreateAccountRequest request, IAccountRepository accounts, IProfileRepository profiles, CancellationToken ct) =>
        {
            if (request.ProfileIds.Count == 0) return ApiErrors.BadRequest("At least one profile grant is required.");
            foreach (var profileId in request.ProfileIds.Distinct())
                if (await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false) is null) return ApiErrors.BadRequest($"Profile '{profileId}' does not exist.");
            var localOnly = string.IsNullOrWhiteSpace(request.Email);
            if (!localOnly) return ApiErrors.BadRequest("Remote accounts must be created with an invitation.");
            if (request.ProfileIds.Distinct().Count() != 1) return ApiErrors.BadRequest("Local-only access must belong to exactly one profile.");
            if (await accounts.GetLocalOnlyAccountIdForProfileAsync(request.ProfileIds[0], ct).ConfigureAwait(false) is not null) return ApiErrors.Conflict("That profile already has local-only access.");
            var now = DateTimeOffset.UtcNow;
            var account = new Account
            {
                Id = Guid.NewGuid(),
                IsLocalOnly = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await accounts.InsertAsync(account, ct).ConfigureAwait(false);
            var defaultId = request.DefaultProfileId is { } requested && request.ProfileIds.Contains(requested) ? requested : request.ProfileIds[0];
            foreach (var profileId in request.ProfileIds.Distinct())
            {
                await accounts.GrantProfileAsync(new AccountProfileGrant
                {
                    AccountId = account.Id,
                    ProfileId = profileId,
                    IsDefault = profileId == defaultId,
                    GrantedAt = now,
                }, ct).ConfigureAwait(false);
            }

            return Results.Ok(await ToResponseAsync(account, accounts, ct).ConfigureAwait(false));
        }).RequireAuthorization(AuthPolicies.Administrator).Produces<AccountResponse>();

        group.MapPut("/{accountId:guid}/profiles/{profileId:guid}", async (Guid accountId, Guid profileId, SetAccountProfileGrantRequest request, IAccountRepository accounts, IProfileRepository profiles, CancellationToken ct) =>
        {
            var account = await accounts.GetByIdAsync(accountId, ct).ConfigureAwait(false);
            if (account is null) return ApiErrors.NotFound("Account not found.");
            if (await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false) is null) return ApiErrors.NotFound("Profile not found.");
            await accounts.GrantProfileAsync(new AccountProfileGrant
            {
                AccountId = accountId,
                ProfileId = profileId,
                IsDefault = request.IsDefault,
                GrantedAt = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
            return Results.Ok(await ToResponseAsync(account, accounts, ct).ConfigureAwait(false));
        }).RequireAuthorization(AuthPolicies.Administrator).Produces<AccountResponse>();

        group.MapDelete("/{accountId:guid}/profiles/{profileId:guid}", async (Guid accountId, Guid profileId, IAccountRepository accounts, IIdentityRepository identities, CancellationToken ct) =>
        {
            var account = await accounts.GetByIdAsync(accountId, ct).ConfigureAwait(false);
            if (account is null) return ApiErrors.NotFound("Account not found.");
            var profileIds = await accounts.GetProfileIdsAsync(accountId, ct).ConfigureAwait(false);
            if (!profileIds.Contains(profileId)) return ApiErrors.NotFound("Profile grant not found.");
            if (profileIds.Count <= 1) return ApiErrors.Conflict("An enabled account must retain at least one profile grant.");
            var wasDefault = await accounts.GetDefaultProfileIdAsync(accountId, ct).ConfigureAwait(false) == profileId;
            await accounts.RevokeProfileAsync(accountId, profileId, ct).ConfigureAwait(false);
            if (wasDefault)
            {
                var replacement = profileIds.First(id => id != profileId);
                await accounts.GrantProfileAsync(new AccountProfileGrant
                {
                    AccountId = accountId,
                    ProfileId = replacement,
                    IsDefault = true,
                    GrantedAt = DateTimeOffset.UtcNow,
                }, ct).ConfigureAwait(false);
            }
            await identities.RevokeAccountSessionsAsync(accountId, DateTimeOffset.UtcNow, "profile_grant_changed", null, ct).ConfigureAwait(false);
            return Results.Ok(await ToResponseAsync(account, accounts, ct).ConfigureAwait(false));
        }).RequireAuthorization(AuthPolicies.Administrator).Produces<AccountResponse>();

        group.MapPost("/invitations", async (CreateAccountInvitationRequest request, IAccountRepository accounts, IProfileRepository profiles, CancellationToken ct) =>
        {
            if (request.ProfileIds.Count == 0) return ApiErrors.BadRequest("At least one profile grant is required.");
            string normalized;
            try
            {
                normalized = new MailAddress(request.Email.Trim()).Address.ToUpperInvariant();
            }
            catch (FormatException)
            {
                return ApiErrors.BadRequest("Enter a valid email address.");
            }

            if (await accounts.GetByNormalizedEmailAsync(normalized, ct).ConfigureAwait(false) is not null) return ApiErrors.Conflict("An account with that email already exists.");
            foreach (var id in request.ProfileIds.Distinct())
            {
                if (await profiles.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
                    return ApiErrors.BadRequest($"Profile '{id}' does not exist.");
            }

            var now = DateTimeOffset.UtcNow;
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Email = request.Email.Trim(),
                NormalizedEmail = normalized,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await accounts.InsertAsync(account, ct).ConfigureAwait(false);

            var defaultId = request.DefaultProfileId is { } selected && request.ProfileIds.Contains(selected)
                ? selected
                : request.ProfileIds[0];
            foreach (var id in request.ProfileIds.Distinct())
            {
                await accounts.GrantProfileAsync(new AccountProfileGrant
                {
                    AccountId = account.Id,
                    ProfileId = id,
                    IsDefault = id == defaultId,
                    GrantedAt = now,
                }, ct).ConfigureAwait(false);
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            var expires = now.AddDays(7);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            await accounts.InsertInvitationAsync(new AccountInvitation
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = expires,
            }, ct).ConfigureAwait(false);
            return Results.Ok(new AccountInvitationResponse(account.Id, token, expires));
        }).RequireAuthorization(AuthPolicies.Administrator).Produces<AccountInvitationResponse>();

        return app;
    }

    private static async Task<AccountResponse> ToResponseAsync(
        Account account,
        IAccountRepository accounts,
        CancellationToken ct) => new()
        {
            Id = account.Id,
            Email = account.Email,
            IsLocalOnly = account.IsLocalOnly,
            IsEnabled = account.IsEnabled,
            ProfileIds = await accounts.GetProfileIdsAsync(account.Id, ct).ConfigureAwait(false),
            DefaultProfileId = await accounts.GetDefaultProfileIdAsync(account.Id, ct).ConfigureAwait(false),
        };

    private static Guid RequiredAccountId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TuvimaClaimTypes.AccountId), out var id)
            ? id
            : throw new UnauthorizedAccessException("Account identity is unavailable.");
}
