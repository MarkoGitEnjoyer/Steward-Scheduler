using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class FlightDto
    {
        public int FlightId { get; set; }
        public string FlightNumber { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string AircraftType { get; set; }
        public string Destination { get; set; }
        public int? RequiredLanguageId { get; set; }
        public float FlightTime { get; set; }
        public int Priority { get; set; }
        public int? ReturnFlightId { get; set; }
        public int RequiredBusinessCrew { get; set; }
        public int RequiredEconomyCrew { get; set; }
    }

}
