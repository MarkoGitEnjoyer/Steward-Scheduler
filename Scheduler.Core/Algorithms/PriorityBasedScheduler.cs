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
        // Generate a schedule based on priority rules with strict 90-hour constraint enforcement
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

            // IMPORTANT: Reset all stewards' projected hours to their base monthly hours
            foreach (var steward in stewards)
            {
                steward.InitializeProjectedHours();
            }

            // Calculate average monthly hours for workload balancing
            float averageMonthlyHours = stewards.Count > 0 ? stewards.Average(s => s.MonthlyHours) : 0;

            // Group stewards by role for easy lookup
            var businessStewards = stewards.Where(s => s.Role == "Business").ToList();
            var economyStewards = stewards.Where(s => s.Role == "Economy").ToList();
            var seniorStewards = stewards.Where(s => s.Role == "Business" && s.IsSenior).ToList();

            // Create a composite scoring system for flights
            var scoredFlights = flights
                .Where(f => f.DepartureTime >= weekStart && f.DepartureTime < weekStart.AddDays(7))
                .Select(f => new {
                    Flight = f,
                    // Composite score considers priority but also how easy it is to staff
                    Score = f.Priority * 3 +
                           (HasSufficientCrew(f, businessStewards, economyStewards, seniorStewards) ? 2 : 0) -
                           (f.RequiredBusinessCrew + f.RequiredEconomyCrew)
                })
                .OrderByDescending(sf => sf.Score)
                .ToList();

            // Extract flights in new priority order
            var sortedFlights = scoredFlights.Select(sf => sf.Flight).ToList();

            // Initialize steward hours dictionary for tracking
            var stewardHours = stewards.ToDictionary(s => s.StewardId, s => s.MonthlyHours);

            // Process flights in optimized order
            foreach (var flight in sortedFlights)
            {
                // Skip flights that are not in the current week
                if (flight.DepartureTime < weekStart || flight.DepartureTime >= schedule.WeekEnd)
                    continue;

                var flightAssignment = new FlightAssignment { Flight = flight };

                // Calculate total flight time for availability check
                float totalFlightTime = flight.FlightTime;

                // Calculate scores for senior stewards with enhanced scoring system
                var eligibleSeniorStewards = seniorStewards
     .Where(s => s.Role == "Business" &&
                IsAvailableForFlight(s, flight, schedule) &&
                !s.WouldExceedHourLimit(totalFlightTime) && // Explicitly check 90-hour limit
                s.HasLicenseForAircraft(flight.AircraftType))
     .Select(s => new
     {
         Steward = s,
         Score = CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                (90 - s.ProjectedHours) * 0.5f // Higher score for stewards with more available hours
     })
     .OrderByDescending(x => x.Score)
     .ToList();

                // Assign senior steward if available (required for every flight, but only ONE)
                if (eligibleSeniorStewards.Any())
                {
                    var bestSenior = eligibleSeniorStewards.First().Steward;
                    flightAssignment.BusinessStewards.Add(bestSenior);

                    // Update the steward's projected hours
                    if (!bestSenior.AddHours(totalFlightTime))
                    {
                        continue; // Skip this flight if we can't add hours
                    }

                    // Update last flight time
                    DateTime endTime = flight.ArrivalTime;
                    bestSenior.LastFlightEndTime = endTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(bestSenior.StewardId))
                        schedule.StewardSchedules[bestSenior.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[bestSenior.StewardId].Add(flight);

                    // Track steward hours in schedule
                    if (!schedule.StewardHours.ContainsKey(bestSenior.StewardId))
                    {
                        schedule.StewardHours[bestSenior.StewardId] = 0;
                    }
                    schedule.StewardHours[bestSenior.StewardId] += totalFlightTime;

                }
                else
                {
                    // If we couldn't find a senior steward within hour constraints, skip this flight
                    continue;
                }

                // Assign remaining business class stewards
                int remainingBusiness = flight.RequiredBusinessCrew - flightAssignment.BusinessStewards.Count;

                if (remainingBusiness > 0)
                {
                    // Find business stewards who won't exceed 90 hours
                    var availableBusinessStewards = businessStewards
                        .Where(s => !flightAssignment.BusinessStewards.Any(assignedSteward => assignedSteward.StewardId == s.StewardId) &&
                                  !s.IsSenior && // Exclude senior stewards - already handled
                                  IsAvailableForFlight(s, flight, schedule) &&
                                  !s.WouldExceedHourLimit(totalFlightTime) && // STRICT 90-HOUR CHECK
                                  s.HasLicenseForAircraft(flight.AircraftType))
                        .Select(s => new
                        {
                            Steward = s,
                            Score = CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                                   (90 - s.ProjectedHours) * 0.5f // Use ProjectedHours
                        })
                        .OrderByDescending(x => x.Score)
                        .Take(remainingBusiness)
                        .ToList();

                    foreach (var stewardInfo in availableBusinessStewards)
                    {
                        flightAssignment.BusinessStewards.Add(stewardInfo.Steward);

                        // Update projected hours
                        if (!stewardInfo.Steward.AddHours(totalFlightTime))
                        {
                            continue;
                        }

                        // Update hours tracking
                        stewardHours[stewardInfo.Steward.StewardId] += totalFlightTime;

                        // Update last flight time
                        DateTime endTime = flight.ArrivalTime;
                        stewardInfo.Steward.LastFlightEndTime = endTime;

                        // Add to steward's schedule
                        if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                            schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);

                        // Track steward hours in schedule
                        if (!schedule.StewardHours.ContainsKey(stewardInfo.Steward.StewardId))
                        {
                            schedule.StewardHours[stewardInfo.Steward.StewardId] = 0;
                        }
                        schedule.StewardHours[stewardInfo.Steward.StewardId] += totalFlightTime;
                    }
                }

                // Assign economy class stewards with enhanced selection
                // Only consider stewards who won't exceed 90 hours
                var availableEconomyStewards = economyStewards
                    .Where(s => IsAvailableForFlight(s, flight, schedule) &&
                              !s.WouldExceedHourLimit(totalFlightTime) && // STRICT 90-HOUR CHECK
                              s.HasLicenseForAircraft(flight.AircraftType))
                    .Select(s => new
                    {
                        Steward = s,
                        Score = CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                               (90 - s.ProjectedHours) * 0.5f
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(flight.RequiredEconomyCrew)
                    .ToList();

                // Always try to assign as many economy stewards as we can find
                foreach (var stewardInfo in availableEconomyStewards)
                {
                    flightAssignment.EconomyStewards.Add(stewardInfo.Steward);

                    // Update projected hours
                    if (!stewardInfo.Steward.AddHours(totalFlightTime))
                    {
                        continue;
                    }

                    // Update hours tracking
                    stewardHours[stewardInfo.Steward.StewardId] += totalFlightTime;

                    // Update last flight time
                    DateTime endTime = flight.ArrivalTime;
                    stewardInfo.Steward.LastFlightEndTime = endTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                        schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);

                    // Track steward hours in schedule
                    if (!schedule.StewardHours.ContainsKey(stewardInfo.Steward.StewardId))
                    {
                        schedule.StewardHours[stewardInfo.Steward.StewardId] = 0;
                    }
                    schedule.StewardHours[stewardInfo.Steward.StewardId] += totalFlightTime;
                }

                // If we have at least a senior steward and some economy crew, we can schedule the flight
                // (we'll consider partial crew better than no flight assigned)
                bool shouldScheduleFlight = flightAssignment.BusinessStewards.Count > 0 &&
                                           flightAssignment.BusinessStewards.Any(s => s.IsSenior) &&
                                           flightAssignment.EconomyStewards.Count > 0;

                if (shouldScheduleFlight)
                {
                    schedule.FlightAssignments.Add(flightAssignment);
                }
                else
                {
                    // Remove this flight's hours from the stewards (cleanup)
                    foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
                    {
                        steward.RemoveHours(totalFlightTime);
                        stewardHours[steward.StewardId] -= totalFlightTime;

                        // Clean up schedule
                        if (schedule.StewardSchedules.ContainsKey(steward.StewardId))
                        {
                            schedule.StewardSchedules[steward.StewardId].Remove(flight);
                        }

                        // Update hours in schedule tracking
                        if (schedule.StewardHours.ContainsKey(steward.StewardId))
                        {
                            schedule.StewardHours[steward.StewardId] -= totalFlightTime;
                        }
                    }
                }
            }

            // Double-check all stewards' hours are still under 90
            var overworkedStewards = stewards
                .Where(s => s.ProjectedHours > 90)
                .ToList();

          

            // Log the hours for all stewards involved in this schedule
            foreach (var id in schedule.StewardHours.Keys)
            {
                var steward = stewards.FirstOrDefault(s => s.StewardId == id);
                if (steward != null)
                {
                    float baseHours = steward.MonthlyHours;
                    float scheduledHours = schedule.StewardHours[id];
                    float projectedTotal = baseHours + scheduledHours;


                    // Safety check - update projected hours to match real calculation
                    steward.ProjectedHours = baseHours + scheduledHours;
                }
            }

            // Calculate fitness score for this schedule
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }

        // Calculate steward score for flight assignment (simplified version)
        private float CalculateStewardScore(StewardDto steward, FlightDto flight, SchedulingWeights weights, float averageMonthlyHours)
        {
            if (steward == null || flight == null)
                return 0;

            // Experience score (0-1): More experienced stewards score higher
            float experienceScore = Math.Min(1.0f, steward.ExperienceYears / 10.0f);

            // Feedback score (0-1): Stewards with more positive feedback score higher
            float feedbackScore = (steward.PositiveFeedbackCount - steward.NegativeFeedbackCount);
            feedbackScore = Math.Min(1.0f, Math.Max(0, feedbackScore / 5.0f)); // Normalize to 0-1

            // Workload balance score (0-1): Stewards with fewer flight hours score higher
            float workloadScore = 1.0f - (steward.ProjectedHours / Math.Max(1, averageMonthlyHours));
            workloadScore = Math.Max(0, Math.Min(1.0f, workloadScore)); // Clamp to 0-1

            // Language match score (0-1): Stewards who speak the required language score higher
            float languageScore = 0;
            if (flight.RequiredLanguageId.HasValue &&
                flight.RequiredLanguageId.Value > 0 &&
                steward.LanguageIds.Contains(flight.RequiredLanguageId.Value))
            {
                languageScore = 1.0f;
            }

            // Calculate weighted score
            float totalScore = weights.ExperienceWeight * experienceScore +
                             weights.FeedbackWeight * feedbackScore +
                             weights.WorkloadBalanceWeight * workloadScore +
                             weights.LanguageWeight * languageScore;

            return totalScore;
        }

        // Helper method to check if we have enough stewards (simplified version)
        private bool HasSufficientCrew(FlightDto flight,
                    List<StewardDto> businessStewards,
                    List<StewardDto> economyStewards,
                    List<StewardDto> seniorStewards)
        {
            // Filter stewards to those who have license for this aircraft type
            // AND who won't exceed 90 hours if assigned this flight
            var licensedBusinessStewards = businessStewards
                .Where(s => s.HasLicenseForAircraft(flight.AircraftType) &&
                        !s.WouldExceedHourLimit(flight.FlightTime)) // Check 90-hour constraint
                .ToList();

            var licensedEconomyStewards = economyStewards
                .Where(s => s.HasLicenseForAircraft(flight.AircraftType) &&
                        !s.WouldExceedHourLimit(flight.FlightTime)) // Check 90-hour constraint
                .ToList();

            var licensedSeniorStewards = seniorStewards
                .Where(s => s.HasLicenseForAircraft(flight.AircraftType) &&
                        !s.WouldExceedHourLimit(flight.FlightTime)) // Check 90-hour constraint
                .ToList();

            // Check if we have at least the required number of stewards available
            bool enoughBusiness = licensedBusinessStewards.Count >= flight.RequiredBusinessCrew;
            bool enoughEconomy = licensedEconomyStewards.Count >= flight.RequiredEconomyCrew;
            bool hasSeniors = licensedSeniorStewards.Count >= 1;

            return enoughBusiness && enoughEconomy && hasSeniors;
        }


        // Helper to check if two flights overlap in time
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

        // Check if steward is available for a flight
        private bool IsAvailableForFlight(StewardDto steward, FlightDto flight, WeeklySchedule schedule = null)
        {
            // First check if adding this flight would exceed 90 hours
            if (steward.WouldExceedHourLimit(flight.FlightTime))
            {
                return false;
            }

            // Check if steward is available for the flight based on last flight end time
            if (steward.LastFlightEndTime != null)
            {
                TimeSpan restTime = flight.DepartureTime - steward.LastFlightEndTime.Value;
                if (restTime.TotalHours < 12)
                    return false;
            }

            // Check against all currently assigned flights for this steward in the schedule
            if (schedule != null && schedule.StewardSchedules.TryGetValue(steward.StewardId, out var existingFlights))
            {
                foreach (var existingFlight in existingFlights)
                {
                    // Check for overlap with the flight
                    if (DoFlightsOverlap(existingFlight, flight))
                        return false;

                    // Check if there's enough rest time
                    if (!HasEnoughRestBetween(existingFlight, flight))
                        return false;
                }
            }

            return true;
        }
        // Modified to use strict 90-hour constraint
        public void ImproveSchedule(WeeklySchedule schedule, List<StewardDto> stewards)
        {
            // Reset all stewards' projected hours to base monthly hours
            foreach (var steward in stewards)
            {
                steward.InitializeProjectedHours();
            }

            // Initialize hours based on current schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    steward.AddHours(flightTime);
                }
            }

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

                        // Only try to swap if we need a senior and there's only one senior in the complete flight
                        if (!incomplete.HasSeniorSteward && seniorStewards.Count == 1)
                        {
                            var senior = seniorStewards.First();

                            // Calculate flight times
                            float incompleteTime = incomplete.Flight.FlightTime;
                            float completeTime = complete.Flight.FlightTime;

                            // Check if this steward can work on the incomplete flight
                            // and won't exceed 90 hours after the swap
                            if (IsAvailableForFlight(senior, incomplete.Flight, schedule))
                            {
                                // Temporarily remove hours from current flight
                                senior.RemoveHours(completeTime);

                                // Check if adding new flight would exceed limit
                                if (senior.WouldExceedHourLimit(incompleteTime))
                                {
                                    // Restore original hours and skip this swap
                                    senior.AddHours(completeTime);
                                    continue;
                                }

                                // Add hours for new flight
                                senior.AddHours(incompleteTime);

                                // Perform the swap
                                complete.BusinessStewards.Remove(senior);
                                incomplete.BusinessStewards.Add(senior);

                                // Update steward's schedule
                                if (schedule.StewardSchedules.ContainsKey(senior.StewardId))
                                {
                                    schedule.StewardSchedules[senior.StewardId].Remove(complete.Flight);
                                    schedule.StewardSchedules[senior.StewardId].Add(incomplete.Flight);
                                }

                                // Update last flight end time
                                DateTime endTime = incomplete.Flight.ArrivalTime;
                                senior.LastFlightEndTime = endTime;

                                break;
                            }
                        }
                    }
                }

                // Similar logic for regular business stewards and economy stewards would go here
            }

            // Recalculate fitness score after improvements
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);
        }
    }
}