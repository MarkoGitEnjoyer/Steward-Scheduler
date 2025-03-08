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
        // Generates a schedule based on priority rules
        public WeeklySchedule GenerateSchedule(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart,
            SchedulingWeights weights)
        {
            var schedule = new WeeklySchedule
            {
                WeekStart = weekStart,
                WeekEnd = weekStart.AddDays(7)
            };

            // Calculate average monthly hours for workload balancing
            float averageMonthlyHours = stewards.Count > 0 ? stewards.Average(s => s.MonthlyHours) : 0;

            // Group stewards by role for easy lookup
            var businessStewards = stewards.Where(s => s.Role == "Business").ToList();
            var economyStewards = stewards.Where(s => s.Role == "Economy").ToList();
            var seniorStewards = stewards.Where(s => s.IsSenior).ToList();

            // Sort flights by priority (highest first)
            var sortedFlights = flights.OrderByDescending(f => f.Priority).ToList();

            // Process flights in priority order
            foreach (var flight in sortedFlights)
            {
                // Skip flights that are not in the current week
                if (flight.DepartureTime < weekStart || flight.DepartureTime >= schedule.WeekEnd)
                    continue;

                var flightAssignment = new FlightAssignment { Flight = flight };

                // Calculate scores for senior stewards (must be business class)
                var eligibleSeniorStewards = seniorStewards
                    .Where(s => s.Role == "Business")
                    .Select(s => new
                    {
                        Steward = s,
                        Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours)
                    })
                    .Where(x => x.Score > 0) // Must have non-zero score to be eligible
                    .OrderByDescending(x => x.Score)
                    .ToList();

                // Assign senior steward if available (required for every flight)
                if (eligibleSeniorStewards.Any())
                {
                    var bestSenior = eligibleSeniorStewards.First().Steward;
                    flightAssignment.BusinessStewards.Add(bestSenior);

                    // Update monthly hours
                    bestSenior.MonthlyHours += flight.FlightTime;

                    // Update last flight time
                    bestSenior.LastFlightEndTime = flight.ArrivalTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(bestSenior.StewardId))
                        schedule.StewardSchedules[bestSenior.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[bestSenior.StewardId].Add(flight);
                }

                // Assign remaining business class stewards
                int remainingBusiness = flight.RequiredBusinessCrew - flightAssignment.BusinessStewards.Count;
                if (remainingBusiness > 0)
                {
                    // Get eligible business stewards (not already assigned to this flight)
                    var availableBusinessStewards = businessStewards
                        .Where(s => !flightAssignment.BusinessStewards.Any(assignedSteward => assignedSteward.StewardId == s.StewardId))
                        .Select(s => new
                        {
                            Steward = s,
                            Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours)
                        })
                        .Where(x => x.Score > 0)
                        .OrderByDescending(x => x.Score)
                        .Take(remainingBusiness)
                        .ToList();

                    foreach (var stewardInfo in availableBusinessStewards)
                    {
                        flightAssignment.BusinessStewards.Add(stewardInfo.Steward);

                        // Update monthly hours
                        stewardInfo.Steward.MonthlyHours += flight.FlightTime;

                        // Update last flight time
                        stewardInfo.Steward.LastFlightEndTime = flight.ArrivalTime;

                        // Add to steward's schedule
                        if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                            schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);
                    }
                }

                // Assign economy class stewards
                var availableEconomyStewards = economyStewards
                    .Select(s => new
                    {
                        Steward = s,
                        Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .Take(flight.RequiredEconomyCrew)
                    .ToList();

                foreach (var stewardInfo in availableEconomyStewards)
                {
                    flightAssignment.EconomyStewards.Add(stewardInfo.Steward);

                    // Update monthly hours
                    stewardInfo.Steward.MonthlyHours += flight.FlightTime;

                    // Update last flight time
                    stewardInfo.Steward.LastFlightEndTime = flight.ArrivalTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                        schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);
                }

                schedule.FlightAssignments.Add(flightAssignment);
            }

            // Calculate fitness score for this schedule
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }

        // Look for potential swaps to improve partially scheduled flights
        public void ImproveSchedule(WeeklySchedule schedule, List<StewardDto> stewards)
        {
            // Identify incomplete flight assignments
            var incompleteAssignments = schedule.FlightAssignments
                .Where(fa => !fa.IsComplete())
                .OrderByDescending(fa => fa.Flight.Priority)
                .ToList();

            if (incompleteAssignments.Count == 0)
                return;

            // Find flights with complete crew that have lower priority
            var completeAssignments = schedule.FlightAssignments
                .Where(fa => fa.IsComplete())
                .OrderBy(fa => fa.Flight.Priority)
                .ToList();

            // Attempt to improve each incomplete assignment
            foreach (var incomplete in incompleteAssignments)
            {
                // Check if missing business crew with senior steward
                if (!incomplete.HasSeniorSteward || incomplete.BusinessStewards.Count < incomplete.Flight.RequiredBusinessCrew)
                {
                    // Try to find a senior steward from a lower priority flight
                    foreach (var complete in completeAssignments)
                    {
                        // Skip flights with higher or equal priority
                        if (complete.Flight.Priority >= incomplete.Flight.Priority)
                            continue;

                        // Look for senior stewards in this flight
                        var seniorStewards = complete.BusinessStewards.Where(s => s.IsSenior).ToList();

                        foreach (var senior in seniorStewards)
                        {
                            // Check if this steward can work on the incomplete flight
                            if (senior.IsAvailable(incomplete.Flight.DepartureTime, incomplete.Flight.FlightTime) &&
                                senior.MonthlyHours - complete.Flight.FlightTime + incomplete.Flight.FlightTime <= 90)
                            {
                                // Perform the swap
                                complete.BusinessStewards.Remove(senior);
                                incomplete.BusinessStewards.Add(senior);

                                // Update monthly hours
                                senior.MonthlyHours = senior.MonthlyHours - complete.Flight.FlightTime + incomplete.Flight.FlightTime;

                                // Update steward's schedule
                                if (schedule.StewardSchedules.ContainsKey(senior.StewardId))
                                {
                                    schedule.StewardSchedules[senior.StewardId].Remove(complete.Flight);
                                    schedule.StewardSchedules[senior.StewardId].Add(incomplete.Flight);
                                }

                                break;
                            }
                        }

                        // Stop if the requirement is now met
                        if (incomplete.HasSeniorSteward)
                            break;
                    }
                }

                // Check if missing economy crew
                if (incomplete.EconomyStewards.Count < incomplete.Flight.RequiredEconomyCrew)
                {
                    int missing = incomplete.Flight.RequiredEconomyCrew - incomplete.EconomyStewards.Count;

                    // Try to reassign from lower priority flights
                    foreach (var complete in completeAssignments)
                    {
                        // Skip flights with higher or equal priority
                        if (complete.Flight.Priority >= incomplete.Flight.Priority)
                            continue;

                        foreach (var steward in complete.EconomyStewards.ToList())
                        {
                            // Check if this steward can work on the incomplete flight
                            if (steward.IsAvailable(incomplete.Flight.DepartureTime, incomplete.Flight.FlightTime) &&
                                steward.MonthlyHours - complete.Flight.FlightTime + incomplete.Flight.FlightTime <= 90)
                            {
                                // Perform the swap
                                complete.EconomyStewards.Remove(steward);
                                incomplete.EconomyStewards.Add(steward);

                                // Update monthly hours
                                steward.MonthlyHours = steward.MonthlyHours - complete.Flight.FlightTime + incomplete.Flight.FlightTime;

                                // Update steward's schedule
                                if (schedule.StewardSchedules.ContainsKey(steward.StewardId))
                                {
                                    schedule.StewardSchedules[steward.StewardId].Remove(complete.Flight);
                                    schedule.StewardSchedules[steward.StewardId].Add(incomplete.Flight);
                                }

                                missing--;
                                if (missing == 0)
                                    break;
                            }
                        }

                        if (missing == 0)
                            break;
                    }
                }
            }

            // Recalculate fitness score after improvements
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);
        }
    }
}