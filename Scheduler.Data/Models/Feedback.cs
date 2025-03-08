using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public enum FeedbackType
    {
        Complaint,
        Praise
    }
    public class Feedback
    {
        [Key]
        public int FeedbackId { get; set; }
        public int StewardId { get; set; }
        public FeedbackType FeedbackType { get; set; }
        public string FeedbackText { get; set; }

        [ForeignKey("StewardId")]
        public virtual Steward Steward { get; set; }
    }
}
