using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using System.Reflection;

namespace SenorArroz.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Obtener el ILoggerFactory desde el contenedor de servicios
            var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

            // Configurar AutoMapper con el loggerFactory
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.AddMaps(Assembly.GetExecutingAssembly()); // Detecta todos los Profiles
            }, loggerFactory);

            IMapper mapper = new Mapper(mapperConfig);
            services.AddSingleton(mapper);

            // MediatR - Registra automáticamente todos los handlers del ensamblado
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            });

            // FluentValidation - Para validaciones de DTOs
            // services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            // Business Rules Service
            services.AddScoped<IOrderBusinessRulesService, OrderBusinessRulesService>();

            return services;
        }
    }
}
