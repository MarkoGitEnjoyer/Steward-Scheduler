using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class StewardLicense
    {
        public int StewardId { get; set; }
        public int LicenseId { get; set; }

        [ForeignKey("StewardId")]
        public virtual Steward Steward { get; set; }

        [ForeignKey("LicenseId")]
        public virtual AircraftLicense License { get; set; }
    }
}
