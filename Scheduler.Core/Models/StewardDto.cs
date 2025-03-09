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
        private bool DoFlightsOverlap(FlightDto flight1, FlightDto flight2)
        {
            return (flight1.DepartureTime <= flight2.ArrivalTime &&
                    flight1.ArrivalTime >= flight2.DepartureTime);
        }

        private bool HasEnoughRestBetween(FlightDto flight1, FlightDto flight2)
        {
            // Determine which flight comes first
            var earlierFlight = flight1.DepartureTime < flight2.DepartureTime ? flight1 : flight2;
            var laterFlight = earlierFlight == flight1 ? flight2 : flight1;

            // Check if there's at least 12 hours between the end of the earlier flight
            // and the start of the later flight
            TimeSpan restTime = laterFlight.DepartureTime - earlierFlight.ArrivalTime;
            return restTime.TotalHours >= 12;
        }
        public bool IsAvailable(DateTime flightTime, float duration)
        {
            if (LastFlightEndTime == null)
                return true;

            TimeSpan restTime = TimeSpan.FromHours(12);
            return flightTime - LastFlightEndTime.Value > restTime;
        }
        public bool IsAvailableForFlight(FlightDto flight, WeeklySchedule schedule)
        {
            // First check basic availability
            if (!IsAvailable(flight.DepartureTime, flight.FlightTime))
                return false;

            // If steward isn't scheduled yet, they're available
            if (!schedule.StewardSchedules.ContainsKey(StewardId))
                return true;

            // Check for overlap with existing flights
            foreach (var existingFlight in schedule.StewardSchedules[StewardId])
            {
                // Check if flights overlap
                if (DoFlightsOverlap(existingFlight, flight))
                    return false;

                // Check if there's enough rest time between flights
                if (!HasEnoughRestBetween(existingFlight, flight))
                    return false;
            }

            return true;
        }
        public float GetSuitabilityScore(FlightDto flight, float averageMonthlyHours)
        {
            // Base score starts at 0
            float score = 0;

            // Experience adds up to 25 points
            score += Math.Min(25, ExperienceYears * 5);

            // Feedback adds up to 20 points
            score += Math.Min(20, Math.Max(0, FeedbackScore * 3));

            // Workload balance adds up to 20 points (inverse of current hours)
            float workloadScore = 20 * (1 - (MonthlyHours / Math.Max(1, averageMonthlyHours * 1.5f)));
            score += Math.Max(0, workloadScore);

            // Language match adds 15 points
            if (flight.RequiredLanguageId.HasValue &&
                flight.RequiredLanguageId.Value > 0 &&
                LanguageIds.Contains(flight.RequiredLanguageId.Value))
            {
                score += 15;
            }

            // Being senior adds 10 points for business class
            if (IsSenior && Role == "Business")
                score += 10;

            return score;
        }
    }
}
