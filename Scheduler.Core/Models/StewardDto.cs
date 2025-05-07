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

        /// <summary>
        /// Checks if the steward is available at the given time considering rest requirements
        /// </summary>
        public bool IsAvailable(DateTime flightDepartureTime, float flightDuration)
        {
            if (LastFlightEndTime == null)
                return true;

            // Ensure minimum rest period of 12 hours
            TimeSpan restTime = TimeSpan.FromHours(12);
            return flightDepartureTime - LastFlightEndTime.Value >= restTime;
        }

        /// <summary>
        /// Checks if steward is available for a specific flight considering all constraints
        /// </summary>
        public bool IsAvailableForFlight(FlightDto flight, WeeklySchedule schedule)
        {
            // Check hour limit
            if (!IsWithinHourLimit(flight, schedule))
                return false;

            // Check basic availability based on rest time
            if (!IsAvailable(flight.DepartureTime, flight.FlightTime))
                return false;

            // Check aircraft license
            if (!HasLicenseForAircraft(flight.AircraftType))
                return false;

            // If steward isn't scheduled yet, they're available (subject to checks above)
            if (!schedule.StewardSchedules.ContainsKey(StewardId))
                return true;

            // Check for conflicts with existing flights
            return HasNoFlightConflicts(flight, schedule);
        }

        /// <summary>
        /// Checks if adding this flight would exceed 90-hour limit
        /// </summary>
        private bool IsWithinHourLimit(FlightDto flight, WeeklySchedule schedule)
        {
            float currentScheduledHours = 0;
            if (schedule.StewardHours.ContainsKey(StewardId))
            {
                currentScheduledHours = schedule.StewardHours[StewardId];
            }

            return (MonthlyHours + currentScheduledHours + flight.FlightTime <= 90);
        }

        /// <summary>
        /// Checks if there are no conflicts with existing flights in the schedule
        /// </summary>
        private bool HasNoFlightConflicts(FlightDto flight, WeeklySchedule schedule)
        {
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

        /// <summary>
        /// Checks if steward has license for the specified aircraft type
        /// </summary>
        public bool HasLicenseForAircraft(string aircraftType)
        {
            if (string.IsNullOrEmpty(aircraftType) || !LicensedAircraftTypes.Any())
            {
                return false;
            }
            return LicensedAircraftTypes.Contains(aircraftType);
        }

        /// <summary>
        /// Checks if two flights overlap in time
        /// </summary>
        public static bool DoFlightsOverlap(FlightDto flight1, FlightDto flight2)
        {
            return (flight1.DepartureTime <= flight2.ArrivalTime &&
                    flight1.ArrivalTime >= flight2.DepartureTime);
        }

        /// <summary>
        /// Checks if there is enough rest time between two flights
        /// </summary>
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

    
    }
}