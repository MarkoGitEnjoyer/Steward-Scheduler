using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public class Language
    {
        [Key]
        public int LanguageId { get; set; }
        public string LanguageName { get; set; }

        // Navigation properties
        public virtual ICollection<StewardLanguage> StewardLanguages { get; set; }
        public virtual ICollection<Flight> Flights { get; set; }

        public Language()
        {
            StewardLanguages = new HashSet<StewardLanguage>();
            Flights = new HashSet<Flight>();
        }
    }
}
