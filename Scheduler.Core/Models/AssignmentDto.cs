using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class AssignmentDto
    {
        public int AssignmentId { get; set; }
        public int StewardId { get; set; }
        public int FlightId { get; set; }
        public StewardDto Steward { get; set; }
        public FlightDto Flight { get; set; }
    }
}
