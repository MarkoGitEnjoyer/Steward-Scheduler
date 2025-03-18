using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class WeeklySchedule
    {
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public List<FlightAssignment> FlightAssignments { get; set; } = new List<FlightAssignment>();
        public Dictionary<int, List<FlightDto>> StewardSchedules { get; set; } = new Dictionary<int, List<FlightDto>>();

        public int TotalFlightCount { get; set; }

        // Fitness score for genetic algorithm
        public float FitnessScore { get; set; }

        // Clone method for genetic operations
        public WeeklySchedule Clone()
        {
            var clone = new WeeklySchedule
            {
                WeekStart = this.WeekStart,
                WeekEnd = this.WeekEnd,
                FitnessScore = this.FitnessScore
            };

            // Deep copy flight assignments
            foreach (var assignment in this.FlightAssignments)
            {
                var newAssignment = new FlightAssignment
                {
                    Flight = assignment.Flight
                };

                newAssignment.BusinessStewards.AddRange(assignment.BusinessStewards);
                newAssignment.EconomyStewards.AddRange(assignment.EconomyStewards);

                clone.FlightAssignments.Add(newAssignment);
            }

            // Deep copy steward schedules
            foreach (var steward in this.StewardSchedules)
            {
                clone.StewardSchedules[steward.Key] = new List<FlightDto>(steward.Value);
            }

            return clone;
        }
        public bool WouldExceedStewardHours(int stewardId, float additionalHours)
        {
            float currentHours = GetStewardFlightHours(stewardId);
            return (currentHours + additionalHours) > 90;
        }

        // Helper method to calculate a steward's current flight hours in this schedule
        public float GetStewardFlightHours(int stewardId)
        {
            if (!StewardSchedules.ContainsKey(stewardId))
                return 0;

            return StewardSchedules[stewardId].Sum(f => f.FlightTime);
        }

        // Helper method to check if a steward is already overworked
        public bool IsStewardOverworked(StewardDto steward)
        {
            float currentHours = GetStewardFlightHours(steward.StewardId);
            return (steward.MonthlyHours + currentHours) > 85; // Using 85 as a threshold to be cautious
        }

        // Helper method to check if adding a flight would create a conflict for a steward
        public bool WouldCreateTimeConflict(StewardDto steward, FlightDto flight)
        {
            // If steward isn't scheduled yet, no conflict
            if (!StewardSchedules.ContainsKey(steward.StewardId))
                return false;

            // Check for overlap with existing flights
            foreach (var existingFlight in StewardSchedules[steward.StewardId])
            {
                // Check if flights overlap or don't leave enough rest time (12 hours)
                if (DoFlightsOverlap(existingFlight, flight) ||
                    !HasEnoughRestBetween(existingFlight, flight))
                    return true;
            }

            return false;
        }

        // Helper to check if two flights overlap in time
        private bool DoFlightsOverlap(FlightDto flight1, FlightDto flight2)
        {
            return (flight1.DepartureTime <= flight2.ArrivalTime &&
                    flight1.ArrivalTime >= flight2.DepartureTime);
        }

        // Helper to check if there's enough rest time between flights
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
        public List<FlightDto> GetUnassignedFlights()
        {
            var assignedFlightIds = FlightAssignments.Select(fa => fa.Flight.FlightId).ToHashSet();

            // Get all flights from the week that are assigned
            var allWeekFlights = FlightAssignments.Select(fa => fa.Flight)
                .Where(f => f.DepartureTime >= WeekStart && f.DepartureTime < WeekEnd)
                .ToList();

            // Find any flights missing from assignments (this would require having a complete list of all flights)
            // Since we don't have that here, this is a placeholder that would need to be replaced with actual logic
            return new List<FlightDto>();
        }
    }
}
