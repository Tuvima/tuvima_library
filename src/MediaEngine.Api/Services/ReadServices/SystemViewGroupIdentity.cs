using System.Security.Cryptography;
using System.Text;
using MediaEngine.Api.Models;

namespace MediaEngine.Api.Services.ReadServices;

public static class SystemViewGroupIdentity
{
    public static Guid CreateId(ContentGroupDto group, string? mediaType, string? groupField)
    {
        var identity = BuildIdentity(group, mediaType, groupField);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{Normalize(mediaType)}|{Normalize(groupField)}|{identity}"));
        return new Guid(bytes);
    }

    private static string BuildIdentity(ContentGroupDto group, string? mediaType, string? groupField)
    {
        var name = Normalize(group.DisplayName);
        if (string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name}|{Normalize(group.Creator)}";
        }

        return string.Join("|",
            name,
            Normalize(group.Creator),
            Normalize(group.Network),
            Normalize(group.Year));
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "(blank)"
            : value.Trim().ToLowerInvariant();
}
