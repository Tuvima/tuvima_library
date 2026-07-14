using MediaEngine.Api.Endpoints;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SkiaSharp;

namespace MediaEngine.Api.Tests;

public sealed class ProfileAvatarEndpointTests
{
    [Fact]
    public async Task UploadProfileAvatarAsync_PreservesExistingAvatarWhenProfileUpdateFails()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(root, "existing.png");
            var existingBytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(existingPath, existingBytes);
            var profile = CreateProfile(existingPath);
            var service = new FakeProfileService(profile, updateResult: false);
            using var upload = new MemoryStream(CreatePng());
            var request = CreateMultipartRequest(upload, "avatar.png", "image/png");

            var result = await ProfileEndpoints.UploadProfileAvatarAsync(
                profile.Id,
                request,
                service,
                new TuvimaDataPaths(root),
                CancellationToken.None);

            Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Equal(existingBytes, await File.ReadAllBytesAsync(existingPath));
            Assert.Equal(existingPath, service.PersistedProfile.AvatarImagePath);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "profiles", profile.Id.ToString("D"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadProfileAvatarAsync_PreservesExistingAvatarWhenImageIsInvalid()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(root, "existing.png");
            var existingBytes = new byte[] { 5, 6, 7, 8 };
            await File.WriteAllBytesAsync(existingPath, existingBytes);
            var profile = CreateProfile(existingPath);
            var service = new FakeProfileService(profile, updateResult: true);
            using var upload = new MemoryStream("not an image"u8.ToArray());
            var request = CreateMultipartRequest(upload, "avatar.png", "image/png");

            var result = await ProfileEndpoints.UploadProfileAvatarAsync(
                profile.Id,
                request,
                service,
                new TuvimaDataPaths(root),
                CancellationToken.None);

            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Equal(existingBytes, await File.ReadAllBytesAsync(existingPath));
            Assert.Equal(0, service.UpdateCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadProfileAvatarAsync_DeletesExistingAvatarOnlyAfterSuccessfulUpdate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(root, "existing.png");
            await File.WriteAllBytesAsync(existingPath, [9, 10, 11, 12]);
            var profile = CreateProfile(existingPath);
            var service = new FakeProfileService(profile, updateResult: true);
            using var upload = new MemoryStream(CreatePng());
            var request = CreateMultipartRequest(upload, "avatar.png", "image/png");

            var result = await ProfileEndpoints.UploadProfileAvatarAsync(
                profile.Id,
                request,
                service,
                new TuvimaDataPaths(root),
                CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.False(File.Exists(existingPath));
            Assert.NotEqual(existingPath, service.PersistedProfile.AvatarImagePath);
            Assert.True(File.Exists(service.PersistedProfile.AvatarImagePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpRequest CreateMultipartRequest(Stream contents, string fileName, string contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=tuvima-test";
        var file = new FormFile(contents, 0, contents.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        var files = new FormFileCollection { file };
        context.Features.Set<IFormFeature>(new FormFeature(new FormCollection([], files)));
        return context.Request;
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Purple);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Profile CreateProfile(string avatarPath) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Avatar Test",
        AvatarColor = "#7C4DFF",
        AvatarImagePath = avatarPath,
        Role = ProfileRole.Consumer,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Profile Clone(Profile profile) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        AvatarColor = profile.AvatarColor,
        AvatarImagePath = profile.AvatarImagePath,
        Role = profile.Role,
        PinHash = profile.PinHash,
        CreatedAt = profile.CreatedAt,
        NavigationConfig = profile.NavigationConfig,
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tuvima_avatar_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProfileService(Profile profile, bool updateResult) : IProfileService
    {
        public Profile PersistedProfile { get; private set; } = Clone(profile);
        public int UpdateCount { get; private set; }

        public Task<Profile?> GetProfileAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Profile?>(id == PersistedProfile.Id ? Clone(PersistedProfile) : null);

        public Task<bool> UpdateProfileAsync(Profile updated, CancellationToken ct = default)
        {
            UpdateCount++;
            if (updateResult)
            {
                PersistedProfile = Clone(updated);
            }

            return Task.FromResult(updateResult);
        }

        public Task<IReadOnlyList<Profile>> GetAllProfilesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Profile>>([Clone(PersistedProfile)]);

        public Task<Profile> CreateProfileAsync(
            string displayName,
            ProfileRole role,
            string avatarColor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Profile> GetDefaultProfileAsync(CancellationToken ct = default) =>
            Task.FromResult(Clone(PersistedProfile));
    }
}
