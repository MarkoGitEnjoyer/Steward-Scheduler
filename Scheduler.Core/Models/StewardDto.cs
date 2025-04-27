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

        public List<int> LicenseIds { get; set; } = new List<int>();
        public List<string> LicensedAircraftTypes { get; set; } = new List<string>();

        public List<int> LanguageIds { get; set; } = new List<int>();
        public int PositiveFeedbackCount { get; set; }
        public int NegativeFeedbackCount { get; set; }

        public float ExperienceYears => (float)(DateTime.Now - JoiningDate).TotalDays / 365;
        public float FeedbackScore => PositiveFeedbackCount - NegativeFeedbackCount;

        // Aircraft license mapping for quick lookup
        private static readonly Dictionary<string, int> AircraftLicenseMap = new Dictionary<string, int>
        {
            { "A321", 1 },
            { "B777", 2 },
            { "B737", 3 },
            { "B747", 4 },
            { "A350", 5 }
        };

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
            // Check 90-hour constraint using schedule's tracking
            float currentScheduledHours = 0;
            if (schedule.StewardHours.ContainsKey(StewardId))
            {
                currentScheduledHours = schedule.StewardHours[StewardId];
            }

            if (MonthlyHours + currentScheduledHours + flight.FlightTime > 90)
            {
                return false;
            }

            // Check basic availability
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

        public static bool DoFlightsOverlap(FlightDto flight1, FlightDto flight2)
        {
            return (flight1.DepartureTime <= flight2.ArrivalTime &&
                    flight1.ArrivalTime >= flight2.DepartureTime);
        }

        public static bool HasEnoughRestBetween(FlightDto flight1, FlightDto flight2)
        {
            // Determine which flight comes first
            var earlierFlight = flight1.DepartureTime < flight2.DepartureTime ? flight1 : flight2;
            var laterFlight = earlierFlight == flight1 ? flight2 : flight1;

            // Check if there's at least 12 hours between the end of the earlier flight
            // and the start of the later flight
            TimeSpan restTime = laterFlight.DepartureTime - earlierFlight.ArrivalTime;
            return restTime.TotalHours >= 12;
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