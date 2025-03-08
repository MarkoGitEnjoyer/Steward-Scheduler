using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class StewardLanguage
    {
        public int StewardId { get; set; }
        public int LanguageId { get; set; }

        [ForeignKey("StewardId")]
        public virtual Steward Steward { get; set; }

        [ForeignKey("LanguageId")]
        public virtual Language Language { get; set; }
    }
}
