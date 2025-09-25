using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Branches.Queries
{
    
        public class GetBranchNeighborhoodsQuery: IRequest<IEnumerable<BranchNeighborhoodDto>>
    {
       
        public int BranchId { get; set; }
    }
           

   


}
