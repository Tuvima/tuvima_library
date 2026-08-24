using System.Text.Json;
using MediaEngine.Contracts.LocalAssets;

namespace MediaEngine.Contracts.Tests;

public sealed class ViewPersonalSpaceAdminContractTests
{
    [Fact]
    public void ReviewContract_ExposesAdministratorStorageHierarchyWithoutInternalKeysOrDeviceIdentifiers()
    {
        var now = DateTimeOffset.Parse("2026-08-23T12:00:00Z");
        var response = new ViewPersonalSpaceAdminReviewDto(
            Guid.NewGuid(),
            new ViewPersonalSpaceAdminDto(Guid.NewGuid(), "C:\\View\\profiles\\abc", now.AddDays(-2), now),
            [new ViewSourceAdminDto(Guid.NewGuid(), "folder", "Family imports", "linked",
                "D:\\Family imports", true, true, now, now.AddDays(-2), now)],
            [new ViewDeviceAdminDto(Guid.NewGuid(), null, "Shaya's phone", "Example", "Phone", now,
                "complete", now.AddDays(-2), now)]);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"profile_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_activity_at\"", json, StringComparison.Ordinal);
        Assert.Contains("\"root_path\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storage_mode\":\"linked\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_backup_at\"", json, StringComparison.Ordinal);
        Assert.Contains("\"backup_state\":\"complete\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("library_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("source_key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("client_device_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("capacity", json, StringComparison.OrdinalIgnoreCase);
    }
}
