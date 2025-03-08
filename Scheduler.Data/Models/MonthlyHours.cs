using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class MonthlyHours
    {
        [Key]
        public int RecordId { get; set; }
        public int StewardId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public float HoursWorked { get; set; }

        [ForeignKey("StewardId")]
        public virtual Steward Steward { get; set; }
    }
}
