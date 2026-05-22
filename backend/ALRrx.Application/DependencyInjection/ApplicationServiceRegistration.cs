using ALRrx.Application.Mappings;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ALRrx.Application.DependencyInjection;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(AutoMapperProfile));
        services.AddValidatorsFromAssemblyContaining<TimeFilterValidator>();

        services.AddScoped<GetDashboardDataUseCase>();
        services.AddScoped<GetReportUseCase>();
        services.AddScoped<ExportReportUseCase>();
        services.AddScoped<GetAvailableQueriesUseCase>();
        services.AddScoped<ExportDashboardUseCase>();

        return services;
    }
}
