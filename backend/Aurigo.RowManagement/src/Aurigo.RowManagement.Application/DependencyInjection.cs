using Aurigo.RowManagement.Application.Agency;
using System.Reflection;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Common;
using FluentValidation;
using MediatR;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aurigo.RowManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var asm = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(asm);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(asm, includeInternalTypes: true);

        services.AddScoped<TokenPasswordFlow>();
        services.AddScoped<IPagePermissionService, PagePermissionService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ProjectNumberGenerator>();
        services.AddScoped<AgencyClock>();
        services.AddScoped<ParcelService>();

        // Calculations are static, stateless functions (see Calculations/README.md); nothing to register.
        return services;
    }
}
