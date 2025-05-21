using Scheduler.Core.Models;
using Scheduler.Core.Utils;
using Scheduler.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scheduler.Core.Algorithms
{
    public class PriorityBasedScheduler
    {
        #region Main Schedule Generation

        // Generate a schedule based on priority rules with strict 90-hour constraint enforcement
        public WeeklySchedule GenerateSchedule(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart,
            SchedulingWeights weights)
        {
            // set weekstart&&weekEnd
            var schedule = WeeklySchedule.InitializeSchedule(weekStart);

            // Calculate average monthly hours for workload balancing
            float averageMonthlyHours = stewards.Count > 0 ? stewards.Average(s => s.MonthlyHours) : 0;

            // Group stewards by role for easy lookup
            var stewardGroups = GroupStewardsByRole(stewards);

            // Sort flights by priority
            var sortedFlights = SortFlightsByPriority(flights, weekStart, schedule.WeekEnd);
            schedule.TotalFlightCount = sortedFlights.Count;

            // Process flights in optimized order
            foreach (var flight in sortedFlights)
            {
                ProcessFlight(flight, schedule, stewardGroups, weights, averageMonthlyHours);
            }

            // Calculate fitness score for this schedule
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }

        #endregion

        #region Initialization Methods

        private Dictionary<string, List<StewardDto>> GroupStewardsByRole(List<StewardDto> stewards)
        {
            return new Dictionary<string, List<StewardDto>>
            {
                // only business and no seniors
                ["Business"] = stewards.Where(st => st.Role == "Business" && !st.IsSenior).ToList(),
                // only economy
                ["Economy"] = stewards.Where(st => st.Role == "Economy").ToList(),
                // only seniors
                ["Senior"] = stewards.Where(st => st.IsSenior).ToList(),
            };
        }

        private List<FlightDto> SortFlightsByPriority(List<FlightDto> flights, DateTime weekStart, DateTime weekEnd)
        {
            return flights
                .OrderByDescending(sf => sf.Priority)
                .ToList();
        }

        #endregion

        #region Flight Processing

        private void ProcessFlight(
            FlightDto flight,
            WeeklySchedule schedule,
            Dictionary<string, List<StewardDto>> stewardGroups,
            SchedulingWeights weights,
            float averageMonthlyHours)
        {
            // creating new flight assignment and inserting flight
            var flightAssignment = new FlightAssignment { Flight = flight };
            // getting flight time from flight
            float flightTime = flight.FlightTime;

            // First, assign a senior steward (required for every flight)
            bool assignedSenior = AssignSeniorSteward(
                flight,
                flightAssignment,
                stewardGroups["Senior"],
                schedule,
                weights,
                averageMonthlyHours,
                flightTime);

            if (!assignedSenior)
            {
                // If we couldn't find a senior steward, skip this flight
                schedule.LogNoSeniorSteward(flight.FlightId);
                return;
            }

            // Assign remaining business class stewards
            AssignRemainingBusinessStewards(
                flight,
                flightAssignment,
                stewardGroups["Business"],
                schedule,
                weights,
                averageMonthlyHours,
                flightTime);

            // Assign economy class stewards
            AssignEconomyStewards(
                flight,
                flightAssignment,
                stewardGroups["Economy"],
                schedule,
                weights,
                averageMonthlyHours,
                flightTime);

            // Determine if the flight should be scheduled
            bool shouldSchedule = flightAssignment.IsComplete();

            if (shouldSchedule)
            {
                schedule.FlightAssignments.Add(flightAssignment);
                schedule.LogFlightScheduled(flight, flightAssignment);
            }
            else
            {
                schedule.LogFlightUnscheduled(flight, flightAssignment);
                // Remove this flight's hours from the stewards (cleanup)
                CleanupFailedAssignment(flightAssignment, schedule, flightTime);
            }
        }

        private void CleanupFailedAssignment(FlightAssignment flightAssignment, WeeklySchedule schedule, float flightTime)
        {
            foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
            {
                // Remove hours from schedule tracking
                schedule.RemoveStewardHours(steward.StewardId, flightTime);

                // Clean up schedule
                if (schedule.StewardSchedules.ContainsKey(steward.StewardId))
                {
                    schedule.StewardSchedules[steward.StewardId].Remove(flightAssignment.Flight);
                }
            }
        }

        #endregion

        #region Steward Assignment Methods

        private bool AssignSeniorSteward(
            FlightDto flight,
            FlightAssignment flightAssignment,
            List<StewardDto> seniorStewards,
            WeeklySchedule schedule,
            SchedulingWeights weights,
            float averageMonthlyHours,
            float flightTime)
        {
            // Calculate scores for senior stewards with enhanced scoring system
            var eligibleSeniorStewards = seniorStewards
                .Where(s => s.IsAvailableForFlight(flight, schedule))
                .Select(s => new
                {
                    Steward = s,
                    Score = CalculateStewardScore(s, schedule, flight, weights, averageMonthlyHours)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            // Assign senior steward if available
            if (eligibleSeniorStewards.Any())
            {
                // taking the first (the best)
                var bestSenior = eligibleSeniorStewards.First().Steward;
                flightAssignment.BusinessStewards.Add(bestSenior);

                // Update tracking
                UpdateStewardAssignment(bestSenior, flight, schedule, flightTime);
                return true;
            }

            return false;
        }

        private void AssignRemainingBusinessStewards(
            FlightDto flight,
            FlightAssignment flightAssignment,
            List<StewardDto> businessStewards,
            WeeklySchedule schedule,
            SchedulingWeights weights,
            float averageMonthlyHours,
            float flightTime)
        {
            int remainingBusiness = flight.RequiredBusinessCrew - flightAssignment.BusinessStewards.Count;

            if (remainingBusiness > 0)
            {
                // Find business stewards who won't exceed 90 hours and aren't senior
                var availableBusinessStewards = businessStewards
                    .Where(s => s.IsAvailableForFlight(flight, schedule))
                    .Select(s => new
                    {
                        Steward = s,
                        Score = CalculateStewardScore(s, schedule, flight, weights, averageMonthlyHours)
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(remainingBusiness)
                    .ToList();

                foreach (var stewardInfo in availableBusinessStewards)
                {
                    flightAssignment.BusinessStewards.Add(stewardInfo.Steward);
                    UpdateStewardAssignment(stewardInfo.Steward, flight, schedule, flightTime);
                }
            }
        }

        private void AssignEconomyStewards(
            FlightDto flight,
            FlightAssignment flightAssignment,
            List<StewardDto> economyStewards,
            WeeklySchedule schedule,
            SchedulingWeights weights,
            float averageMonthlyHours,
            float flightTime)
        {
            // Only consider stewards who won't exceed 90 hours
            var availableEconomyStewards = economyStewards
                .Where(s => s.IsAvailableForFlight(flight, schedule))
                .Select(s => new
                {
                    Steward = s,
                    Score = CalculateStewardScore(s, schedule, flight, weights, averageMonthlyHours)
                })
                .OrderByDescending(x => x.Score)
                .Take(flight.RequiredEconomyCrew)
                .ToList();

            // Always try to assign as many economy stewards as we can find
            foreach (var stewardInfo in availableEconomyStewards)
            {
                flightAssignment.EconomyStewards.Add(stewardInfo.Steward);
                UpdateStewardAssignment(stewardInfo.Steward, flight, schedule, flightTime);
            }
        }

        private void UpdateStewardAssignment(StewardDto steward, FlightDto flight, WeeklySchedule schedule, float flightTime)
        {
            // Add to steward's schedule
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

            schedule.StewardSchedules[steward.StewardId].Add(flight);

            // Track steward hours in schedule
            schedule.AddStewardHours(steward.StewardId, flightTime);
        }

        #endregion

        #region Scoring Methods

        // Calculate steward score for flight assignment
        private float CalculateStewardScore(StewardDto steward, WeeklySchedule schedule, FlightDto flight, SchedulingWeights weights, float averageMonthlyHours)
        {
            // Load all scores
            float experienceScore = Math.Min(1.0f, steward.ExperienceYears / 10.0f);
            float feedbackScore = Math.Min(1.0f, Math.Max(0.0f, steward.FeedbackScore / 5.0f));
            float workloadScore = CalculateWorkloadForStewad(steward, averageMonthlyHours, schedule);
            float languageScore = steward.DoesSpeakLanguage(flight.RequiredLanguageId) ? 1.0f : 0.0f;

            // calculate by using weights
            float qualityScore = weights.ExperienceWeight * experienceScore +
                                 weights.FeedbackWeight * feedbackScore +
                                 weights.WorkloadBalanceWeight * workloadScore +
                                 weights.LanguageWeight * languageScore;

            //getting priority factor from 0.2 to 1
            float priorityFactor = flight.Priority / 5.0f;

            // calculating how much quality of steward matches the flight
            float matchFactor = 1.0f - Math.Abs(qualityScore - priorityFactor);

            return matchFactor;
        }

        // function to calculate score for workload based on avg month hours
        private float CalculateWorkloadForStewad(StewardDto steward, float averageMonthlyHours, WeeklySchedule schedule)
        {
            float workloadScore = 0.0f;
            if (averageMonthlyHours > 0)
            {
                workloadScore = Math.Clamp(
                    (averageMonthlyHours - (steward.MonthlyHours + schedule.GetStewardScheduledHours(steward.StewardId))) / averageMonthlyHours,
                    0.0f,
                    1.0f
                );
            }
            else if (steward.MonthlyHours == 0)
            {
                workloadScore = 1.0f;
            }
            return workloadScore;
        }

        #endregion
    }
}