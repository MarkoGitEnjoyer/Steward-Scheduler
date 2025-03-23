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

        // Track steward hours in this schedule
        public Dictionary<int, float> StewardHours { get; set; } = new Dictionary<int, float>();

        public int TotalFlightCount { get; set; }

        // Fitness score for genetic algorithm
        public float FitnessScore { get; set; }

        // Initialize steward hours for the entire schedule
        public void InitializeStewardHours(List<StewardDto> stewards)
        {
            // Reset all stewards' projected hours to their base monthly hours
            foreach (var steward in stewards)
            {
                steward.InitializeProjectedHours();
            }

            // Clear the tracking dictionary
            StewardHours.Clear();

            // Calculate hours for each steward based on current assignments
            foreach (var assignment in FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    // Update the StewardHours tracking dictionary
                    if (!StewardHours.ContainsKey(steward.StewardId))
                    {
                        StewardHours[steward.StewardId] = 0;
                    }
                    StewardHours[steward.StewardId] += flightTime;

                    // Update the steward's projected hours
                    steward.AddHours(flightTime);
                }
            }
        }

        // Add a steward to a flight if it doesn't violate the 90-hour constraint
        public bool TryAddStewardToFlight(StewardDto steward, FlightAssignment assignment)
        {
            // Check if adding this flight would exceed the steward's 90-hour limit
            if (steward.WouldExceedHourLimit(assignment.Flight.FlightTime))
            {
                return false;
            }

            // Check role-specific requirements
            if (steward.Role == "Business")
            {
                assignment.BusinessStewards.Add(steward);
            }
            else if (steward.Role == "Economy")
            {
                assignment.EconomyStewards.Add(steward);
            }
            else
            {
                return false; // Unknown role
            }

            // Update the steward's hours
            steward.AddHours(assignment.Flight.FlightTime);

            // Update tracking dictionary
            if (!StewardHours.ContainsKey(steward.StewardId))
            {
                StewardHours[steward.StewardId] = 0;
            }
            StewardHours[steward.StewardId] += assignment.Flight.FlightTime;

            // Update steward's schedule
            if (!StewardSchedules.ContainsKey(steward.StewardId))
            {
                StewardSchedules[steward.StewardId] = new List<FlightDto>();
            }
            StewardSchedules[steward.StewardId].Add(assignment.Flight);

            return true;
        }

        // Remove a steward from a flight and update hours
        public void RemoveStewardFromFlight(StewardDto steward, FlightAssignment assignment)
        {
            bool removed = false;

            // Check role and remove from appropriate list
            if (steward.Role == "Business")
            {
                removed = assignment.BusinessStewards.Remove(steward);
            }
            else if (steward.Role == "Economy")
            {
                removed = assignment.EconomyStewards.Remove(steward);
            }

            if (removed)
            {
                // Update the steward's hours
                steward.RemoveHours(assignment.Flight.FlightTime);

                // Update tracking dictionary
                if (StewardHours.ContainsKey(steward.StewardId))
                {
                    StewardHours[steward.StewardId] -= assignment.Flight.FlightTime;
                    if (StewardHours[steward.StewardId] < 0)
                    {
                        StewardHours[steward.StewardId] = 0;
                    }
                }

                // Update steward's schedule
                if (StewardSchedules.ContainsKey(steward.StewardId))
                {
                    StewardSchedules[steward.StewardId].Remove(assignment.Flight);
                }
            }
        }

        // Verify that no steward exceeds 90 hours
        public bool VerifyHourConstraints()
        {
            foreach (var entry in StewardHours)
            {
                int stewardId = entry.Key;
                float scheduledHours = entry.Value;

                // Get the steward from any assignment
                StewardDto steward = null;
                foreach (var assignment in FlightAssignments)
                {
                    steward = assignment.BusinessStewards
                        .Concat(assignment.EconomyStewards)
                        .FirstOrDefault(s => s.StewardId == stewardId);

                    if (steward != null)
                        break;
                }

                if (steward != null)
                {
                    // Calculate total hours (base + scheduled)
                    float totalHours = steward.MonthlyHours + scheduledHours;

                    if (totalHours > 90)
                    {
                        Console.WriteLine($"Hour constraint violation: Steward {stewardId} has {totalHours} hours");
                        return false;
                    }
                }
            }
            return true;
        }

        // Clone method for genetic operations
        public WeeklySchedule Clone()
        {
            var clone = new WeeklySchedule
            {
                WeekStart = this.WeekStart,
                WeekEnd = this.WeekEnd,
                FitnessScore = this.FitnessScore,
                TotalFlightCount = this.TotalFlightCount
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

            // Copy steward hours
            foreach (var entry in this.StewardHours)
            {
                clone.StewardHours[entry.Key] = entry.Value;
            }

            return clone;
        }

        public bool WouldExceedStewardHours(int stewardId, float additionalHours)
        {
            // Get current hours in this schedule
            float currentHours = GetStewardFlightHours(stewardId);

            // Get the steward from any assignment to check base hours
            StewardDto steward = null;
            foreach (var assignment in FlightAssignments)
            {
                steward = assignment.BusinessStewards
                    .Concat(assignment.EconomyStewards)
                    .FirstOrDefault(s => s.StewardId == stewardId);

                if (steward != null)
                    break;
            }

            // If we couldn't find the steward, assume base hours of 0
            float baseHours = steward?.MonthlyHours ?? 0;

            return (baseHours + currentHours + additionalHours) > 90;
        }

        // Helper method to calculate a steward's current flight hours in this schedule
        public float GetStewardFlightHours(int stewardId)
        {
            if (StewardHours.ContainsKey(stewardId))
            {
                return StewardHours[stewardId];
            }

            if (!StewardSchedules.ContainsKey(stewardId))
                return 0;

            float hours = StewardSchedules[stewardId].Sum(f => f.FlightTime);
            // Cache the result
            StewardHours[stewardId] = hours;
            return hours;
        }

        // Helper method to check if a steward is already overworked
        public bool IsStewardOverworked(StewardDto steward)
        {
            if (steward.WouldExceedHourLimit(0))
            {
                return true;
            }

            return false;
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