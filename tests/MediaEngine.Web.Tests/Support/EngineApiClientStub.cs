using System.Reflection;
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

        _handlers[nameof(IEngineApiClient.GetReviewCountAsync)] =
            _ => Task.FromResult(3);

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

        _handlers[nameof(IEngineApiClient.SearchWorksAsync)] =
            _ => Task.FromResult(new List<SearchResultViewModel>());
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
