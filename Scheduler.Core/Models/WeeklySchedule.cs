using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class WeeklySchedule
    {
        private const float MAX_HOURS_LIMIT = 90.0f;
        private const float SAFETY_MARGIN = 0.1f; // Small safety margin to prevent rounding errors

        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public List<FlightAssignment> FlightAssignments { get; set; } = new List<FlightAssignment>();
        public Dictionary<int, List<FlightDto>> StewardSchedules { get; set; } = new Dictionary<int, List<FlightDto>>();

        // Track steward hours in this schedule
        public Dictionary<int, float> StewardHours { get; set; } = new Dictionary<int, float>();

        public int TotalFlightCount { get; set; }

        // Fitness score for genetic algorithm
        public float FitnessScore { get; set; }

        /// <summary>
        /// Check if adding more hours to a steward would exceed the 90-hour limit
        /// </summary>
        public bool WouldExceedHourLimit(int stewardId, float monthlyHours, float additionalHours)
        {
            float currentScheduledHours = GetStewardScheduledHours(stewardId);
            return (monthlyHours + currentScheduledHours + additionalHours) > (MAX_HOURS_LIMIT - SAFETY_MARGIN);
        }

        /// <summary>
        /// Add hours to a steward's schedule - return success/failure
        /// </summary>
        public bool AddStewardHours(int stewardId, float hours)
        {
            if (!StewardHours.ContainsKey(stewardId))
            {
                StewardHours[stewardId] = 0;
            }

            StewardHours[stewardId] += hours;
            return true;
        }

        /// <summary>
        /// Remove hours from a steward's schedule
        /// </summary>
        public void RemoveStewardHours(int stewardId, float hours)
        {
            if (StewardHours.ContainsKey(stewardId))
            {
                StewardHours[stewardId] -= hours;
                if (StewardHours[stewardId] < 0)
                {
                    StewardHours[stewardId] = 0;
                }
            }
        }

        /// <summary>
        /// Get a steward's current scheduled hours
        /// </summary>
        public float GetStewardScheduledHours(int stewardId)
        {
            if (StewardHours.ContainsKey(stewardId))
            {
                return StewardHours[stewardId];
            }
            return 0;
        }

        /// <summary>
        /// Get a steward's total hours (monthly base + scheduled)
        /// </summary>
        public float GetStewardTotalHours(int stewardId, float baseMonthlyHours)
        {
            return baseMonthlyHours + GetStewardScheduledHours(stewardId);
        }

        /// <summary>
        /// Initialize steward hours for the entire schedule
        /// </summary>
        public void InitializeStewardHours(List<StewardDto> stewards)
        {
            // Clear the tracking dictionary
            StewardHours.Clear();

            // Calculate hours for each steward based on current assignments
            foreach (var assignment in FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;
                UpdateStewardHoursForAssignment(assignment, flightTime);
            }
        }

        /// <summary>
        /// Update hours for stewards in an assignment
        /// </summary>
        private void UpdateStewardHoursForAssignment(FlightAssignment assignment, float flightTime)
        {
            foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
            {
                // Update the StewardHours tracking dictionary
                if (!StewardHours.ContainsKey(steward.StewardId))
                {
                    StewardHours[steward.StewardId] = 0;
                }
                StewardHours[steward.StewardId] += flightTime;
            }
        }

        /// <summary>
        /// Remove a steward from a flight and update hours
        /// </summary>
        public void RemoveStewardFromFlight(StewardDto steward, FlightAssignment assignment)
        {
            bool removed = RemoveStewardFromAssignment(steward, assignment);

            if (removed)
            {
                // Update tracking
                UpdateTrackingAfterRemoval(steward, assignment);
            }
        }

        /// <summary>
        /// Remove steward from the appropriate steward list in the assignment
        /// </summary>
        private bool RemoveStewardFromAssignment(StewardDto steward, FlightAssignment assignment)
        {
            // Check role and remove from appropriate list
            if (steward.Role == "Business")
            {
                return assignment.BusinessStewards.Remove(steward);
            }
            else if (steward.Role == "Economy")
            {
                return assignment.EconomyStewards.Remove(steward);
            }
            return false;
        }

        /// <summary>
        /// Update tracking dictionaries after removing a steward from a flight
        /// </summary>
        private void UpdateTrackingAfterRemoval(StewardDto steward, FlightAssignment assignment)
        {
            // Update hours tracking
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

        /// <summary>
        /// Verify that no steward exceeds 90 hours
        /// </summary>
        public bool VerifyHourConstraints(List<StewardDto> stewards)
        {
            var stewardMap = stewards.ToDictionary(s => s.StewardId);

            foreach (var entry in StewardHours)
            {
                int stewardId = entry.Key;
                float scheduledHours = entry.Value;

                if (stewardMap.TryGetValue(stewardId, out var steward))
                {
                    // Calculate total hours (base + scheduled)
                    float totalHours = steward.MonthlyHours + scheduledHours;

                    if (totalHours > MAX_HOURS_LIMIT)
                    {
                        Console.WriteLine($"Hour constraint violation: Steward {stewardId} has {totalHours} hours");
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Clone method for genetic operations
        /// </summary>
        public WeeklySchedule Clone()
        {
            var clone = new WeeklySchedule
            {
                WeekStart = this.WeekStart,
                WeekEnd = this.WeekEnd,
                FitnessScore = this.FitnessScore,
                TotalFlightCount = this.TotalFlightCount
            };

            // Clone flight assignments
            CloneFlightAssignments(clone);

            // Clone steward schedules
            CloneStewardSchedules(clone);

            // Clone steward hours
            CloneStewardHours(clone);

            return clone;
        }

        /// <summary>
        /// Clone flight assignments to the target schedule
        /// </summary>
        private void CloneFlightAssignments(WeeklySchedule clone)
        {
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
        }

        /// <summary>
        /// Clone steward schedules to the target schedule
        /// </summary>
        private void CloneStewardSchedules(WeeklySchedule clone)
        {
            foreach (var steward in this.StewardSchedules)
            {
                clone.StewardSchedules[steward.Key] = new List<FlightDto>(steward.Value);
            }
        }

        /// <summary>
        /// Clone steward hours to the target schedule
        /// </summary>
        private void CloneStewardHours(WeeklySchedule clone)
        {
            foreach (var entry in this.StewardHours)
            {
                clone.StewardHours[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// Helper method to calculate a steward's current flight hours in this schedule
        /// </summary>
        public float GetStewardFlightHours(int stewardId)
        {
            // If we have cached hours, use them
            if (StewardHours.ContainsKey(stewardId))
            {
                return StewardHours[stewardId];
            }

            // Calculate from schedule
            if (!StewardSchedules.ContainsKey(stewardId))
                return 0;

            float hours = StewardSchedules[stewardId].Sum(f => f.FlightTime);

            // Cache the result
            StewardHours[stewardId] = hours;
            return hours;
        }
    }
}