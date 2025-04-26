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

        
    }
}