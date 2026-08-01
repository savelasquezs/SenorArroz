using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.DTOs
{
    public class CustomerDto
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Phone1 { get; set; }
        public string? Phone2 { get; set; }
        public string? WhatsAppUsername { get; set; }
        public bool HasWhatsAppIdentity { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<CustomerAddressDto> Addresses { get; set; } = new();
        public int TotalOrders { get; set; }
        public DateTime? FirstOrderDate { get; set; }
        public DateTime? LastOrderDate { get; set; }
        /// <summary>Suma de totales de pedidos no cancelados (misma moneda que pedidos).</summary>
        public int TotalAccumulated { get; set; }

        /// <summary>Pedidos entregados con este cliente (derivado; no persistido en BD).</summary>
        public int LoyaltyDeliveredCount { get; set; }

        /// <summary>Paso del ciclo que aplicaría en la próxima entrega (1-based), si hay programa.</summary>
        public int? LoyaltyNextStepIndex { get; set; }

        public string? LoyaltyNextRewardLabel { get; set; }

        /// <summary>Entregas que faltan para el próximo premio. Si vale 1, el pedido actual lo activará al entregarse.</summary>
        public int? LoyaltyDeliveriesUntilNextReward { get; set; }

        /// <summary>Indica si el pedido que se está tomando debe llevar el regalo de fidelidad.</summary>
        public bool LoyaltyRewardDueOnCurrentOrder { get; set; }

        /// <summary>Mensaje listo para mostrar en toma de pedidos.</summary>
        public string? LoyaltyNextRewardMessage { get; set; }
    }
}
