using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using System.Reflection;
using SenorArroz.Domain.Services;

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

            services.AddSingleton<IClock, SystemUtcClock>();

            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            // MediatR - Registra automáticamente todos los handlers del ensamblado
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });

            // Business Rules Service
            services.AddScoped<IOrderBusinessRulesService, OrderBusinessRulesService>();
            services.AddScoped<ILoyaltyCycleService, LoyaltyCycleService>();
            services.AddScoped<IFreeDeliverymanFcmTokenResolver, FreeDeliverymanFcmTokenResolver>();
            services.AddScoped<WhatsAppAttentionService>();
            services.AddScoped<IAgentToolExecutor, AgentToolExecutor>();
            services.AddScoped<IWhatsAppAiOrchestrator, WhatsAppAiOrchestrator>();
            services.AddScoped<IBranchBusinessHoursService, BranchBusinessHoursService>();
            services.AddScoped<IWhatsAppSystemPromptBuilder, WhatsAppSystemPromptBuilder>();
            services.AddScoped<IWhatsAppOrderDraftCalculator, WhatsAppOrderDraftCalculator>();

            return services;
        }
    }
}
