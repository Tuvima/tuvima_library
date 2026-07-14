using System.Reflection;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Tests;

public sealed class AggregateEncapsulationGuardrailTests
{
    private static readonly Type[] AggregateTypes =
    [
        typeof(Collection),
        typeof(Work),
        typeof(Edition),
        typeof(Universe),
    ];

    [Fact]
    public void TargetAggregates_DoNotExposePublicMutableCollections()
    {
        var failures = AggregateTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => IsMutableCollectionType(property.PropertyType))
                .Select(property => $"{type.Name}.{property.Name}: {property.PropertyType.Name}"))
            .ToList();

        Assert.Empty(failures);
    }

    private static bool IsMutableCollectionType(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(List<>) ||
            definition == typeof(Dictionary<,>) ||
            definition == typeof(ICollection<>) ||
            definition == typeof(IList<>))
        {
            return true;
        }

        return type.GetInterfaces()
            .Where(interfaceType => interfaceType.IsGenericType)
            .Select(interfaceType => interfaceType.GetGenericTypeDefinition())
            .Any(definition => definition == typeof(IList<>) || definition == typeof(ICollection<>));
    }
}
