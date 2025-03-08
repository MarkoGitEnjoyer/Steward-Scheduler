using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class Assignment
    {
        [Key]
        public int AssignmentId { get; set; }
        public int StewardId { get; set; }
        public int FlightId { get; set; }

        [ForeignKey("StewardId")]
        public virtual Steward Steward { get; set; }

        [ForeignKey("FlightId")]
        public virtual Flight Flight { get; set; }
    }
}
