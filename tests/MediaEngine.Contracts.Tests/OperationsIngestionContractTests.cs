using System.Text.Json;
using MediaEngine.Contracts.Activity;
using MediaEngine.Contracts.Ingestion;

namespace MediaEngine.Contracts.Tests;

public sealed class OperationsIngestionContractTests
{
    [Fact]
    public void IngestionOperationJob_CountUnit_RoundTripsThroughSharedContract()
    {
        const string json =
            """
            {
              "active_jobs": [
                {
                  "job_id": "11111111-2222-3333-4444-555555555555",
                  "count_unit": "albums"
                }
              ]
            }
            """;

        var snapshot = JsonSerializer.Deserialize<IngestionOperationsSnapshotDto>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var job = Assert.Single(Assert.IsType<List<IngestionOperationsJobDto>>(snapshot!.ActiveJobs));
        Assert.Equal("albums", job.CountUnit);

        var roundTrip = JsonSerializer.Serialize(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(roundTrip);
        Assert.Equal(
            "albums",
            document.RootElement
                .GetProperty("active_jobs")[0]
                .GetProperty("count_unit")
                .GetString());
    }

    [Fact]
    public void ActivityContracts_DoNotLeakDashboardRouteOrRelativeTimeHelpers()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var entryJson = JsonSerializer.Serialize(
            new ActivityEntryResponse
            {
                OccurredAt = "2025-01-02T03:04:05Z",
                ActionType = "MediaAdded",
            },
            options);
        var personJson = JsonSerializer.Serialize(
            new ActivityPersonAuditDto
            {
                PersonId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                PersonName = "Example Person",
            },
            options);

        Assert.DoesNotContain("relativeTime", entryJson, StringComparison.Ordinal);
        Assert.DoesNotContain("personUrl", personJson, StringComparison.Ordinal);
    }
}
