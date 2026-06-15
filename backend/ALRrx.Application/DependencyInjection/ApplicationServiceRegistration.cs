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
        services.AddScoped<PeriodComparisonUseCase>();
        services.AddScoped<SubmitVicidialSaleUseCase>();
        services.AddScoped<GetVicidialSalesUseCase>();
        services.AddScoped<GetActiveAltrxAgentsUseCase>();
        services.AddScoped<UpdateVicidialSaleUseCase>();
        services.AddScoped<DeleteVicidialSaleUseCase>();
        services.AddScoped<GetVicidialLeadByIdUseCase>();
        services.AddScoped<GetEnrichedSalesUseCase>();
        services.AddScoped<GetAgentPerformanceWithSalesUseCase>();
        services.AddScoped<TwilioExportUseCase>();

        services.AddScoped<GetAdminUsersUseCase>();
        services.AddScoped<GetAdminUserDetailUseCase>();
        services.AddScoped<ApproveUserUseCase>();
        services.AddScoped<RejectUserUseCase>();
        services.AddScoped<SuspendUserUseCase>();
        services.AddScoped<ReactivateUserUseCase>();
        services.AddScoped<ChangeUserRoleUseCase>();
        services.AddScoped<ResetUserPasswordUseCase>();
        services.AddScoped<SetUserPlatformAccessUseCase>();

        return services;
    }
}
