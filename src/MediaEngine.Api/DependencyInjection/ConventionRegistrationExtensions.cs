using System.Reflection;

namespace MediaEngine.Api.DependencyInjection;

internal static class ConventionRegistrationExtensions
{
    internal static IServiceCollection AddSingletonImplementations<TService>(
        this IServiceCollection services,
        int expectedCount,
        params Assembly[] assemblies)
    {
        var serviceType = typeof(TService);
        var implementations = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.DefinedTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && serviceType.IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => type.AsType())
            .ToArray();

        if (implementations.Length != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} {serviceType.Name} implementations, " +
                $"but discovered {implementations.Length}: " +
                string.Join(", ", implementations.Select(type => type.FullName)));
        }

        foreach (var implementation in implementations)
            services.AddSingleton(serviceType, implementation);

        return services;
    }
}
