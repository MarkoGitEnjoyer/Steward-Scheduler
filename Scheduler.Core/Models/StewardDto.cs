using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class StewardDto
    {
        public int StewardId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}";
        public string Role { get; set; }
        public bool IsSenior { get; set; }
        public DateTime JoiningDate { get; set; }
        public DateTime? LastFlightEndTime { get; set; }
        public float MonthlyHours { get; set; }
        public List<int> LicenseIds { get; set; } = new List<int>();
        public List<int> LanguageIds { get; set; } = new List<int>();
        public int PositiveFeedbackCount { get; set; }
        public int NegativeFeedbackCount { get; set; }

        public float ExperienceYears => (float)(DateTime.Now - JoiningDate).TotalDays / 365;
        public float FeedbackScore => PositiveFeedbackCount - NegativeFeedbackCount;
        public bool IsAvailable(DateTime flightTime, float duration)
        {
            if (LastFlightEndTime == null)
                return true;

            TimeSpan restTime = TimeSpan.FromHours(12);
            return flightTime - LastFlightEndTime.Value > restTime;
        }
    }
}
