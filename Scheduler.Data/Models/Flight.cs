using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class Flight
    {
        [Key]
        public int FlightId { get; set; }
        public string FlightNumber { get; set; }
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string AircraftType { get; set; }
        public string Destination { get; set; }
        public int? RequiredLanguageId { get; set; }
        public float FlightTime { get; set; }
        public int Priority { get; set; }

        // standart fk means a lot of flights may relate to one Aircraft
        [ForeignKey("AircraftType")]
        public virtual AircraftType Aircraft { get; set; }

        [ForeignKey("RequiredLanguageId")]
        public virtual Language RequiredLanguage { get; set; }

        // Navigation properties, other fk means 1 flight may relate to many assignments
        public virtual ICollection<Assignment> Assignments { get; set; }

    }
}