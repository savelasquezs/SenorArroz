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
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Features.DeliveryRouting.Services;

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
            services.AddScoped<IKitchenAutoPrintService, KitchenAutoPrintService>();
            services.AddScoped<ILoyaltyCycleService, LoyaltyCycleService>();
            services.AddScoped<IFreeDeliverymanFcmTokenResolver, FreeDeliverymanFcmTokenResolver>();
            services.AddScoped<WhatsAppAttentionService>();
            services.AddSingleton<IAiToolSchemaValidator, AiToolSchemaValidator>();
            services.AddScoped<AgentToolExecutor>();
            services.AddScoped<IAgentToolExecutor>(sp=>sp.GetRequiredService<AgentToolExecutor>());
            services.AddScoped<IAgentToolCatalog>(sp=>sp.GetRequiredService<AgentToolExecutor>());
            services.AddScoped<IWhatsAppAiOrchestrator, WhatsAppAiOrchestrator>();
            services.AddScoped<IBranchBusinessHoursService, BranchBusinessHoursService>();
            services.AddSingleton<WhatsAppAwayMessageService>();
            services.AddScoped<IDeliveryStayDetectionService, DeliveryStayDetectionService>();
            services.AddScoped<IDeliveryAutoCompletionService, DeliveryAutoCompletionService>();
            services.AddScoped<IDeliveryStayClassificationService, DeliveryStayClassificationService>();
            services.AddScoped<IDeliveryIncidentEvidenceService, DeliveryIncidentEvidenceService>();
            services.AddScoped<IDeliveryTrackingAlertService, DeliveryTrackingAlertService>();
            services.AddScoped<IDeliveryAppVersionPolicy, DeliveryAppVersionPolicy>();
            services.AddScoped<IKitchenPreparationEstimator, KitchenPreparationEstimator>();
            services.AddScoped<IDeliverymanAvailabilityService, DeliverymanAvailabilityService>();
            services.AddScoped<IDeliveryRoutingPlanService, DeliveryRoutingPlanService>();
            services.AddScoped<IWhatsAppSystemPromptBuilder, WhatsAppSystemPromptBuilder>();

            return services;
        }
    }
}
