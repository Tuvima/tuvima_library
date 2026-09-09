using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Tests;

internal static class ProfileTestData
{
    public static Task InsertAsync(IDatabaseConnection database, Profile profile)
    {
        using var connection = database.CreateConnection();
        connection.Execute("""
            INSERT INTO profiles(id,display_name,avatar_color,avatar_image_path,role,created_at,navigation_config)
            VALUES(@Id,@DisplayName,@AvatarColor,@AvatarImagePath,@Role,@CreatedAt,@NavigationConfig);
            """, new
        {
            profile.Id,
            profile.DisplayName,
            profile.AvatarColor,
            profile.AvatarImagePath,
            Role = profile.Role.ToString(),
            CreatedAt = profile.CreatedAt.ToString("O"),
            profile.NavigationConfig,
        });
        return Task.CompletedTask;
    }
}
