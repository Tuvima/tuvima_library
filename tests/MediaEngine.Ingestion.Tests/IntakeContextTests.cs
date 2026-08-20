using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Tests;

public sealed class IntakeContextTests
{
    [Fact]
    public async Task DebounceQueue_PreservesDirectDestination_WhenWatcherEventRacesUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tuvima-intake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "phone-photo.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        try
        {
            using var queue = new DebounceQueue(new DebounceOptions
            {
                SettleDelay = TimeSpan.FromMilliseconds(10),
                ProbeInterval = TimeSpan.FromMilliseconds(5),
                MaxProbeAttempts = 1,
            });
            var destinationId = Guid.NewGuid().ToString("D");
            var intake = new IntakeContext
            {
                SourceKind = IntakeSourceKinds.MobileBackup,
                SourceId = Guid.NewGuid().ToString("D"),
                DestinationLibraryId = destinationId,
                ActorProfileId = Guid.NewGuid(),
            };

            queue.Enqueue(new FileEvent
            {
                Path = path,
                EventType = FileEventType.Created,
                OccurredAt = DateTimeOffset.UtcNow,
                Intake = intake,
            });

            queue.Enqueue(new FileEvent
            {
                Path = path,
                EventType = FileEventType.Modified,
                OccurredAt = DateTimeOffset.UtcNow,
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var candidate = await queue.Reader.ReadAsync(timeout.Token);

            Assert.NotNull(candidate.Intake);
            Assert.Equal(destinationId, candidate.Intake.DestinationLibraryId);
            Assert.Equal(IntakeSourceKinds.MobileBackup, candidate.Intake.SourceKind);
            Assert.Equal(intake.ActorProfileId, candidate.Intake.ActorProfileId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(IntakeSourceKinds.Watcher)]
    [InlineData(IntakeSourceKinds.SharedIncoming)]
    [InlineData(IntakeSourceKinds.DirectLibrary)]
    [InlineData(IntakeSourceKinds.BrowserUpload)]
    [InlineData(IntakeSourceKinds.MobileBackup)]
    [InlineData(IntakeSourceKinds.ConnectedDevice)]
    [InlineData(IntakeSourceKinds.Api)]
    public void IntakeSourceKinds_AcceptsCanonicalValues(string value)
    {
        Assert.True(IntakeSourceKinds.IsValid(value));
    }
}
