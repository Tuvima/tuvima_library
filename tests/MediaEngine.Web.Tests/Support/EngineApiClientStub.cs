using System.Reflection;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Models;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests.Support;

internal class EngineApiClientStub : DispatchProxy
{
    private static readonly MethodInfo TaskFromResultMethod =
        typeof(Task)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Task.FromResult));

    private readonly Dictionary<string, Func<object?[]?, object?>> _handlers =
        new(StringComparer.Ordinal);

    public static IEngineApiClient CreateDefault()
    {
        var proxy = Create<IEngineApiClient, EngineApiClientStub>();
        var stub = (EngineApiClientStub)(object)proxy;
        stub.RegisterDefaults();
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (_handlers.TryGetValue(targetMethod.Name, out var handler))
        {
            return handler(args);
        }

        return CreateDefaultValue(targetMethod.ReturnType);
    }

    private void RegisterDefaults()
    {
        _handlers[nameof(IEngineApiClient.ToAbsoluteEngineUrl)] =
            args => args?[0]?.ToString() ?? string.Empty;

        _handlers[nameof(IEngineApiClient.GetResolvedUISettingsAsync)] =
            _ => Task.FromResult<ResolvedUISettingsViewModel?>(new ResolvedUISettingsViewModel
            {
                DeviceClass = "web",
            });

        _handlers[nameof(IEngineApiClient.GetProfilesAsync)] =
            _ => Task.FromResult(new List<ProfileViewModel>
            {
                new(
                    Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    "Test User",
                    "#C9922E",
                    "Administrator",
                    DateTimeOffset.UtcNow),
            });

        _handlers[nameof(IEngineApiClient.GetTasteProfileAsync)] =
            _ => Task.FromResult<TasteProfile?>(new TasteProfile
            {
                UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Summary = "Test profile built from a mixed library.",
                MediaTypeMix = new Dictionary<string, double>
                {
                    ["Books"] = 0.45,
                    ["Movies"] = 0.25,
                    ["Music"] = 0.30,
                },
                LastUpdatedAt = DateTimeOffset.UtcNow,
            });

        _handlers[nameof(IEngineApiClient.GetPlaybackSettingsAsync)] =
            args => Task.FromResult<UserPlaybackSettingsDto?>(
                UserPlaybackSettingsDto.CreateDefaults((Guid)(args?[0] ?? Guid.Parse("00000000-0000-0000-0000-000000000001"))));

        _handlers[nameof(IEngineApiClient.UpdatePlaybackSettingsAsync)] =
            args => Task.FromResult<UserPlaybackSettingsDto?>((UserPlaybackSettingsDto?)args?[1]);

        _handlers[nameof(IEngineApiClient.GetProfileOverviewAsync)] =
            _ => Task.FromResult<ProfileOverviewViewModel?>(new ProfileOverviewViewModel
            {
                Profile = new ProfileViewModel(
                    Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    "Test User",
                    "#C9922E",
                    "Administrator",
                    DateTimeOffset.UtcNow.AddYears(-1)),
                Stats = new ProfileOverviewStatsViewModel
                {
                    TotalItems = 3,
                    InProgress = 1,
                    Completed = 1,
                    RecentActivity = 2,
                    MediaTypeMix = new Dictionary<string, int>
                    {
                        ["Movies"] = 1,
                        ["Books"] = 1,
                        ["Music"] = 1,
                    },
                    LibraryCounts = new Dictionary<string, int>
                    {
                        ["Movies"] = 12,
                        ["Shows"] = 4,
                        ["Books"] = 8,
                    },
                    ActivityBuckets = new Dictionary<string, int>
                    {
                        ["PlaybackUpdated"] = 2,
                    },
                    TopGenres = new Dictionary<string, int>
                    {
                        ["Drama"] = 3,
                        ["Sci-Fi"] = 2,
                    },
                    ConsumedSeconds = 7200,
                    ConsumedSecondsByMediaType = new Dictionary<string, double>
                    {
                        ["Movies"] = 5400,
                        ["Books"] = 1800,
                    },
                },
                ContinueItems =
                [
                    new ProfileOverviewItemViewModel
                    {
                        AssetId = Guid.Parse("91000000-0000-0000-0000-000000000001"),
                        WorkId = Guid.Parse("91000000-0000-0000-0000-000000000101"),
                        Title = "Landman",
                        Subtitle = "S2 - E10",
                        MediaType = "TV",
                        CollectionName = "Landman",
                        Route = "/watch/player/91000000-0000-0000-0000-000000000001",
                        PositionSeconds = 1800,
                        DurationSeconds = 2800,
                        ProgressPct = 64,
                        LastAccessed = DateTimeOffset.UtcNow.AddHours(-2),
                    },
                ],
                RecentItems =
                [
                    new ProfileOverviewItemViewModel
                    {
                        AssetId = Guid.Parse("91000000-0000-0000-0000-000000000002"),
                        WorkId = Guid.Parse("91000000-0000-0000-0000-000000000102"),
                        Title = "The Substance",
                        Subtitle = "Movie",
                        MediaType = "Movies",
                        CollectionName = "Movies",
                        Route = "/watch/player/91000000-0000-0000-0000-000000000002",
                        ProgressPct = 100,
                        LastAccessed = DateTimeOffset.UtcNow.AddDays(-1),
                    },
                ],
                CompletedItems =
                [
                    new ProfileOverviewItemViewModel
                    {
                        AssetId = Guid.Parse("91000000-0000-0000-0000-000000000003"),
                        WorkId = Guid.Parse("91000000-0000-0000-0000-000000000103"),
                        Title = "Dexter: Resurrection",
                        Subtitle = "S1 - E10",
                        MediaType = "TV",
                        CollectionName = "Dexter",
                        Route = "/watch/player/91000000-0000-0000-0000-000000000003",
                        ProgressPct = 100,
                        LastAccessed = DateTimeOffset.UtcNow.AddDays(-4),
                    },
                ],
                RecentlyAddedItems =
                [
                    new ProfileOverviewItemViewModel
                    {
                        AssetId = Guid.Parse("91000000-0000-0000-0000-000000000004"),
                        WorkId = Guid.Parse("91000000-0000-0000-0000-000000000104"),
                        Title = "Dune: Part Two",
                        Subtitle = "Movie",
                        MediaType = "Movies",
                        ProgressPct = 0,
                        LastAccessed = DateTimeOffset.UtcNow.AddDays(-3),
                        AddedAt = DateTimeOffset.UtcNow.AddDays(-3),
                    },
                ],
                Activity =
                [
                    new ProfileOverviewActivityViewModel
                    {
                        Id = 1,
                        ActionType = "PlaybackUpdated",
                        Detail = "Continued Landman",
                        OccurredAt = DateTimeOffset.UtcNow.AddHours(-2),
                    },
                ],
                Taste = new TasteProfile
                {
                    UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Summary = "Test profile built from a mixed library.",
                    MediaTypeMix = new Dictionary<string, double>
                    {
                        ["Books"] = 0.45,
                        ["Movies"] = 0.25,
                        ["Music"] = 0.30,
                    },
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                },
            });

        _handlers[nameof(IEngineApiClient.GetReviewCountAsync)] =
            _ => Task.FromResult(3);

        _handlers[nameof(IEngineApiClient.GetPendingReviewsAsync)] =
            _ => Task.FromResult(new List<ReviewItemViewModel>
            {
                new()
                {
                    Id = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                    EntityId = Guid.Parse("70000000-0000-0000-0000-000000000101"),
                    EntityType = "Work",
                    EntityTitle = "Unmatched Album",
                    MediaType = "Music",
                    Trigger = "LowConfidence",
                    Status = "Pending",
                    ConfidenceScore = 0.42,
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                    BridgeIdentifiers = new Dictionary<string, string>
                    {
                        ["isrc"] = "TEST123",
                    },
                },
            });

        _handlers[nameof(IEngineApiClient.GetSystemStatusAsync)] =
            _ => Task.FromResult<SystemStatusViewModel?>(new SystemStatusViewModel
            {
                Status = "ok",
                Version = "10.0.0-test",
                Language = "en",
            });

        _handlers[nameof(IEngineApiClient.GetServerGeneralAsync)] =
            _ => Task.FromResult<ServerGeneralSettingsDto?>(new ServerGeneralSettingsDto(
                ServerName: "Tuvima Library",
                Language: "en",
                DisplayLanguage: "en",
                MetadataLanguage: "en",
                AdditionalLanguages: ["fr"],
                AcceptAnyLanguage: true,
                Country: "US",
                DateFormat: "system",
                TimeFormat: "system"));

        _handlers[nameof(IEngineApiClient.GetLibraryOverviewAsync)] =
            _ => Task.FromResult<LibraryOverviewViewModel?>(new LibraryOverviewViewModel
            {
                EnrichedStage3 = 12,
                UniverseAssigned = 10,
                ArtPending = 2,
            });

        _handlers[nameof(IEngineApiClient.GetManagedCollectionsAsync)] =
            _ => Task.FromResult(new List<ManagedCollectionViewModel>
            {
                new()
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Name = "Summer Movies",
                    Description = "Fast access to the current movie shortlist.",
                    CollectionType = "Playlist",
                    Visibility = "shared",
                    IsEnabled = true,
                    ItemCount = 12,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
                    CanEdit = true,
                    CanShare = true,
                },
                new()
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    Name = "Quiet Reads",
                    Description = "A slower reading lane for long-form titles.",
                    CollectionType = "Smart",
                    Visibility = "private",
                    IsEnabled = false,
                    ItemCount = 0,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    CanEdit = false,
                    CanShare = false,
                },
            });

        _handlers[nameof(IEngineApiClient.GetDisplayHomeAsync)] =
            _ => Task.FromResult<DisplayPageDto?>(CreateDisplayPage("home", "Home"));

        _handlers[nameof(IEngineApiClient.GetDisplayBrowseAsync)] =
            args =>
            {
                var lane = args?[0]?.ToString();
                var mediaType = args?[1]?.ToString();
                var grouping = args?[2]?.ToString();
                var key = string.Equals(lane, "listen", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(grouping, "home", StringComparison.OrdinalIgnoreCase)
                    ? "listen-music"
                    : lane ?? "browse";
                var title = key == "listen-music" ? "Music" : key;
                return Task.FromResult<DisplayPageDto?>(CreateDisplayPage(key, title));
            };

        _handlers[nameof(IEngineApiClient.SearchWorksAsync)] =
            _ => Task.FromResult(new List<SearchResultViewModel>());
    }

    private static DisplayPageDto CreateDisplayPage(string key, string title)
    {
        var workId = Guid.Parse("90000000-0000-0000-0000-000000000001");
        var action = new DisplayActionDto("openWork", "Open", WorkId: workId, WebUrl: "/listen/music/songs");
        var artwork = new DisplayArtworkDto("/art/test-cover.jpg", "/art/test-square.jpg", null, null, null, null, null, null, null, null, null, null, null, "#1ED760");
        var card = new DisplayCardDto(
            Id: workId,
            WorkId: workId,
            AssetId: null,
            CollectionId: null,
            MediaType: "Music",
            GroupingType: "work",
            Title: "Test Track",
            Subtitle: "Test Artist",
            Facts: ["Test Artist", "Test Album"],
            Artwork: artwork,
            PreferredShape: "square",
            Presentation: "album",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, true, false, false),
            SortTimestamp: DateTimeOffset.UtcNow);

        return new DisplayPageDto(
            Key: key,
            Title: title,
            Subtitle: null,
            Hero: new DisplayHeroDto(title, "Test Artist", "Library", artwork, null, [action]),
            Shelves: [new DisplayShelfDto("recently-added", "Recently Added", "Fresh arrivals", [card], "/listen/music/playlists/system/recently-added")],
            Catalog: [card]);
    }

    private static object? CreateDefaultValue(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GenericTypeArguments[0];
            var result = CreateTaskResultValue(resultType);
            return TaskFromResultMethod.MakeGenericMethod(resultType).Invoke(null, [result]);
        }

        if (returnType.IsValueType)
        {
            return Activator.CreateInstance(returnType);
        }

        return null;
    }

    private static object? CreateTaskResultValue(Type resultType)
    {
        if (resultType.IsGenericType)
        {
            var openGeneric = resultType.GetGenericTypeDefinition();

            if (openGeneric == typeof(List<>)
                || openGeneric == typeof(Dictionary<,>))
            {
                return Activator.CreateInstance(resultType);
            }

            if (openGeneric == typeof(IReadOnlyList<>)
                || openGeneric == typeof(IEnumerable<>)
                || openGeneric == typeof(ICollection<>)
                || openGeneric == typeof(IList<>))
            {
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(resultType.GenericTypeArguments[0]));
            }

            if (openGeneric == typeof(IReadOnlyDictionary<,>))
            {
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(resultType.GenericTypeArguments));
            }
        }

        if (resultType.IsValueType)
        {
            return Activator.CreateInstance(resultType);
        }

        return null;
    }
}
