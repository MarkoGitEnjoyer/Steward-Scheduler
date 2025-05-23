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
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public List<FlightAssignment> FlightAssignments { get; set; } = new List<FlightAssignment>();
        public Dictionary<int, List<FlightDto>> StewardSchedules { get; set; } = new Dictionary<int, List<FlightDto>>();

        // track steward hours in this schedule
        public Dictionary<int, float> StewardHours { get; set; } = new Dictionary<int, float>();

        public int TotalFlightCount { get; set; }

        // fitness score for genetic algorithm
        public float FitnessScore { get; set; }

        public static WeeklySchedule InitializeSchedule(DateTime weekStart)
        {
            return new WeeklySchedule
            {
                WeekStart = weekStart,
                WeekEnd = weekStart.AddDays(7)
            };
        }

        #region Hour Management Methods
        /// <summary>
        /// add hours to a steward's schedule
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
        /// remove hours from a steward's schedule
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
        /// get a steward's current scheduled hours
        /// </summary>
        public float GetStewardScheduledHours(int stewardId)
        {
            if (StewardHours.ContainsKey(stewardId))
            {
                return StewardHours[stewardId];
            }
            return 0;
        }

        #endregion

        #region Validation Methods

        /// <summary>
        /// verify that no steward exceeds 90 hours
        /// </summary>
        public bool VerifyHourConstraints(List<StewardDto> stewards = null)
        {
            if (stewards != null)
            {
                // convert stewards to dictionary and the key is stewardId
                var stewardMap = stewards.ToDictionary(s => s.StewardId);

                foreach (var entry in StewardHours)
                {
                    int stewardId = entry.Key;
                    float scheduledHours = entry.Value;

                    // if it finds steward in dictionary we did before we can use it in {} block
                    if (stewardMap.TryGetValue(stewardId, out var steward))
                    {
                        // calculate total hours (base + scheduled)
                        float totalHours = steward.MonthlyHours + scheduledHours;

                        if (totalHours > MAX_HOURS_LIMIT)
                        {
                            LogHourConstraintViolation(stewardId, totalHours);
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// validate that a schedule respects all constraints
        /// </summary>
        public bool ValidateSchedule()
        {
            if (FlightAssignments.Count == 0)
                return false;

            return !HasOverlappingFlightsOrRestTime() &&
                   VerifyHourConstraints();
        }

        /// <summary>
        /// check if there are any overlapping flights in the schedule
        /// </summary>
        public bool HasOverlappingFlightsOrRestTime()
        {
            // check each steward's schedule for overlapping flights
            foreach (var kvp in StewardSchedules)
            {
                var flights = kvp.Value;

                // sort flights by departure time
                var orderedFlights = flights.OrderBy(f => f.DepartureTime).ToList();

                // Check for overlaps
                for (int i = 0; i < orderedFlights.Count - 1; i++)
                {
                    for (int j = i + 1; j < orderedFlights.Count; j++)
                    {
                        if (StewardDto.DoFlightsOverlap(orderedFlights[i], orderedFlights[j]))
                        {
                            return true;
                        }
                        TimeSpan restTime = orderedFlights[j].DepartureTime - orderedFlights[i].ArrivalTime;

                        // check if rest time is less than 12 hours
                        if (restTime.TotalHours < 12)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        #endregion

        #region Logging Methods

        public void LogFlightScheduled(FlightDto flight, FlightAssignment flightAssignment)
        {
            Console.WriteLine($"Scheduled flight {flight.FlightId}: Priority {flight.Priority}, " +
                            $"Business: {flightAssignment.BusinessStewards.Count}/{flight.RequiredBusinessCrew}, " +
                            $"Economy: {flightAssignment.EconomyStewards.Count}/{flight.RequiredEconomyCrew}");
        }

        public void LogFlightUnscheduled(FlightDto flight, FlightAssignment flightAssignment)
        {
            Console.WriteLine($"Could not schedule flight {flight.FlightId}: Priority {flight.Priority}, " +
                            $"Business: {flightAssignment.BusinessStewards.Count}/{flight.RequiredBusinessCrew}, " +
                            $"Economy: {flightAssignment.EconomyStewards.Count}/{flight.RequiredEconomyCrew}");
        }

        public void LogNoSeniorSteward(int flightId)
        {
            Console.WriteLine($"Could not schedule flight {flightId}: No available senior steward");
        }

        public void LogHourConstraintViolation(int stewardId, float hours)
        {
            Console.WriteLine($"Hour constraint violation: Steward {stewardId} has {hours} hours");
        }

        #endregion

        #region Clone Methods

        /// <summary>
        /// clone method for genetic operations
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

            // clone flight assignments
            CloneFlightAssignments(clone);

            // clone steward schedules
            CloneStewardSchedules(clone);

            // clone steward hours
            CloneStewardHours(clone);

            return clone;
        }

        /// <summary>
        /// clone flight assignments to the target schedule
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
        /// clone steward schedules to the target schedule
        /// </summary>
        private void CloneStewardSchedules(WeeklySchedule clone)
        {
            foreach (var steward in this.StewardSchedules)
            {
                clone.StewardSchedules[steward.Key] = new List<FlightDto>(steward.Value);
            }
        }

        /// <summary>
        /// clone steward hours to the target schedule
        /// </summary>
        private void CloneStewardHours(WeeklySchedule clone)
        {
            foreach (var entry in this.StewardHours)
            {
                clone.StewardHours[entry.Key] = entry.Value;
            }
        }

        #endregion


    }
}