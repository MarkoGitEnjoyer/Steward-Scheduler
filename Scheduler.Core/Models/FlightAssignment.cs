using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class FlightAssignment
    {
        public FlightDto Flight { get; set; }
        public List<StewardDto> BusinessStewards { get; set; } = new List<StewardDto>();
        public List<StewardDto> EconomyStewards { get; set; } = new List<StewardDto>();
        public bool HasSeniorSteward => BusinessStewards.Exists(s => s.IsSenior);

        public bool IsComplete()
        {
            return BusinessStewards.Count >= Flight.RequiredBusinessCrew &&
                   EconomyStewards.Count >= Flight.RequiredEconomyCrew &&
                   HasSeniorSteward;
        }
    }
}
