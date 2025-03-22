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

        // Current hours in the database for this month
        public float MonthlyHours { get; set; }

        // Projected hours including new assignments - for enforcing 90-hour constraint
        public float ProjectedHours { get; set; }

        public List<int> LicenseIds { get; set; } = new List<int>();
        public List<string> LicensedAircraftTypes { get; set; } = new List<string>();

        public List<int> LanguageIds { get; set; } = new List<int>();
        public int PositiveFeedbackCount { get; set; }
        public int NegativeFeedbackCount { get; set; }

        public float ExperienceYears => (float)(DateTime.Now - JoiningDate).TotalDays / 365;
        public float FeedbackScore => PositiveFeedbackCount - NegativeFeedbackCount;

        // Enforce 90-hour constraint
        public bool WouldExceedHourLimit(float additionalHours)
        {
            return (ProjectedHours + additionalHours) > 90;
        }

        // Helper to initialize and update projected hours
        public void InitializeProjectedHours()
        {
            ProjectedHours = MonthlyHours;
        }

        // Safely add hours while tracking the limit
        public bool AddHours(float hours)
        {
            if (WouldExceedHourLimit(hours))
            {
                return false;
            }

            ProjectedHours += hours;
            return true;
        }

        // Remove hours (for when flights are unassigned)
        public void RemoveHours(float hours)
        {
            ProjectedHours -= hours;
            // Safety check to prevent negative values
            if (ProjectedHours < 0)
                ProjectedHours = 0;
        }

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

        public bool IsAvailable(DateTime flightDepartureTime, float flightDuration)
        {
            if (LastFlightEndTime == null)
                return true;

            // Ensure minimum rest period of 12 hours
            TimeSpan restTime = TimeSpan.FromHours(12);
            return flightDepartureTime - LastFlightEndTime.Value >= restTime;
        }

        public bool IsAvailableForFlight(FlightDto flight, WeeklySchedule schedule)
        {
            // Check 90-hour constraint - HARD LIMIT
            if (WouldExceedHourLimit(flight.FlightTime))
            {
                return false;
            }

            // First check basic availability
            if (!IsAvailable(flight.DepartureTime, flight.FlightTime))
                return false;

            // Check aircraft license
            if (!HasLicenseForAircraft(flight.AircraftType))
                return false;

            // If steward isn't scheduled yet, they're available (subject to license check)
            if (!schedule.StewardSchedules.ContainsKey(StewardId))
                return true;

            // Check for overlap or insufficient rest with ALL existing flights
            foreach (var existingFlight in schedule.StewardSchedules[StewardId])
            {
                // Check if flights overlap in time
                if (DoFlightsOverlap(existingFlight, flight))
                    return false;

                // Check if there's enough rest time between flights
                if (!HasEnoughRestBetween(existingFlight, flight))
                    return false;
            }

            return true;
        }

        // Add this dictionary as a static field to the StewardDto class
        private static readonly Dictionary<string, int> AircraftLicenseMap = new Dictionary<string, int>
        {
            { "A321", 1 },
            { "B777", 2 },
            { "B737", 3 },
            { "B747", 4 },
            { "A350", 5 }
        };

        public bool HasLicenseForAircraft(string aircraftType)
        {
            if (string.IsNullOrEmpty(aircraftType) || LicenseIds == null || !LicenseIds.Any())
                return false;

            // Try to get the license ID for this aircraft type from the dictionary
            if (AircraftLicenseMap.TryGetValue(aircraftType, out int aircraftLicenseId))
            {
                // Check if the steward has this license
                return LicenseIds.Contains(aircraftLicenseId);
            }

            // Unknown aircraft type
            return false;
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
            float workloadScore = 20 * (1 - (ProjectedHours / Math.Max(1, averageMonthlyHours * 1.5f)));
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

        // Clone method for deep copying
        public StewardDto Clone()
        {
            var clone = new StewardDto
            {
                StewardId = this.StewardId,
                FirstName = this.FirstName,
                LastName = this.LastName,
                Role = this.Role,
                IsSenior = this.IsSenior,
                JoiningDate = this.JoiningDate,
                MonthlyHours = this.MonthlyHours,
                ProjectedHours = this.ProjectedHours,
                PositiveFeedbackCount = this.PositiveFeedbackCount,
                NegativeFeedbackCount = this.NegativeFeedbackCount
            };

            if (this.LastFlightEndTime.HasValue)
            {
                clone.LastFlightEndTime = this.LastFlightEndTime.Value;
            }

            // Clone collections
            clone.LicenseIds = new List<int>(this.LicenseIds);
            clone.LanguageIds = new List<int>(this.LanguageIds);

            if (this.LicensedAircraftTypes != null)
            {
                clone.LicensedAircraftTypes = new List<string>(this.LicensedAircraftTypes);
            }

            return clone;
        }
    }
}