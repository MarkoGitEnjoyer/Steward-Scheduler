using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Models
{
    public enum Role
    {
        Business,
        Economy
    }

    public class Steward
    {
        [Key]
        public int StewardId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        [Column("role")]
        public string RoleString { get; set; }

        [NotMapped] 
        public Role Role
        {
            get => Enum.Parse<Role>(RoleString, true);
            set => RoleString = value.ToString();
        }

        public bool IsSenior { get; set; }
        public DateTime JoiningDate { get; set; }
        public DateTime? LastFlightEndTime { get; set; }

        // Navigation properties
        public virtual ICollection<MonthlyHours> MonthlyHours { get; set; }
        public virtual ICollection<StewardLicense> StewardLicenses { get; set; }
        public virtual ICollection<StewardLanguage> StewardLanguages { get; set; }
        public virtual ICollection<Feedback> Feedbacks { get; set; }
        public virtual ICollection<Assignment> Assignments { get; set; }

        public Steward()
        {
            MonthlyHours = new HashSet<MonthlyHours>();
            StewardLicenses = new HashSet<StewardLicense>();
            StewardLanguages = new HashSet<StewardLanguage>();
            Feedbacks = new HashSet<Feedback>();
            Assignments = new HashSet<Assignment>();
        }
    }
}
