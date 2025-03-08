using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class AircraftLicense
    {
        [Key]
        public int LicenseId { get; set; }
        public string AircraftTypeId { get; set; }

        [ForeignKey("AircraftTypeId")]
        public virtual AircraftType AircraftType { get; set; }

        // Navigation properties
        public virtual ICollection<StewardLicense> StewardLicenses { get; set; }

        public AircraftLicense()
        {
            StewardLicenses = new HashSet<StewardLicense>();
        }
    }
}
