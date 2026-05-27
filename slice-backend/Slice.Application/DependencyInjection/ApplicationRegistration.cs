using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Application.DependencyInjection;

public static class ApplicationRegistration
{
    public static IServiceCollection AddSliceApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationRegistration).Assembly);
        return services;
    }
}
