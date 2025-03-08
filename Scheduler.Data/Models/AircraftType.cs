using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class AircraftType
    {
        [Key]
        public string AircraftTypeId { get; set; }
        public int BusinessClassCrew { get; set; }
        public int EconomyClassCrew { get; set; }

        // Navigation properties
        public virtual ICollection<Flight> Flights { get; set; }
        public virtual ICollection<AircraftLicense> AircraftLicenses { get; set; }

        public AircraftType()
        {
            Flights = new HashSet<Flight>();
            AircraftLicenses = new HashSet<AircraftLicense>();
        }
    }
}
