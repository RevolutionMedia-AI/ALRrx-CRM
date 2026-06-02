using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Application.DependencyInjection;

/// <summary>
/// Registers all Application-layer services: FluentValidation validators discovered
/// by scanning the Application assembly.
/// </summary>
public static class ApplicationRegistration
{
    /// <summary>
    /// Adds FluentValidation validators from the Application assembly to the DI container.
    /// Call this from <c>Program.cs</c> via <c>builder.Services.AddSliceApplication()</c>.
    /// </summary>
    public static IServiceCollection AddSliceApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationRegistration).Assembly);
        return services;
    }
}
