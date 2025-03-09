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
        // Generate a schedule based on priority rules with improved resource utilization
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

            // Create a copy of stewards to track monthly hours during scheduling
            var stewardWorkingHours = stewards.ToDictionary(
                s => s.StewardId,
                s => s.MonthlyHours);

            // Calculate average monthly hours for workload balancing
            float averageMonthlyHours = stewards.Count > 0 ? stewards.Average(s => s.MonthlyHours) : 0;

            // Group stewards by role for easy lookup
            var businessStewards = stewards.Where(s => s.Role == "Business").ToList();
            var economyStewards = stewards.Where(s => s.Role == "Economy").ToList();
            var seniorStewards = stewards.Where(s => s.Role == "Business" && s.IsSenior).ToList();

            // Identify flight pairs (outbound and return flights)
            var flightPairs = new Dictionary<int, int>();
            foreach (var flight in flights)
            {
                if (flight.ReturnFlightId.HasValue)
                {
                    flightPairs[flight.FlightId] = flight.ReturnFlightId.Value;
                }
            }

            // Track assigned flight pairs to avoid duplicate assignments
            var assignedFlightPairs = new HashSet<int>();

            // Create a composite scoring system for flights
            // This balances priority with scheduling ease to prevent high-priority
            // flights from monopolizing the best stewards
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

            // Process flights in optimized order
            foreach (var flight in sortedFlights)
            {
                // Skip flights that are not in the current week
                if (flight.DepartureTime < weekStart || flight.DepartureTime >= schedule.WeekEnd)
                    continue;

                // Skip flights that are already assigned as part of a pair
                if (assignedFlightPairs.Contains(flight.FlightId))
                    continue;

                // Check if this is part of a flight pair
                FlightDto returnFlight = null;
                if (flightPairs.TryGetValue(flight.FlightId, out var returnFlightId))
                {
                    returnFlight = flights.FirstOrDefault(f => f.FlightId == returnFlightId);
                }

                var flightAssignment = new FlightAssignment { Flight = flight };
                FlightAssignment returnFlightAssignment = null;

                if (returnFlight != null)
                {
                    returnFlightAssignment = new FlightAssignment { Flight = returnFlight };
                }

                // Calculate total flight time for availability check (including return flight)
                float totalFlightTime = flight.FlightTime;
                if (returnFlight != null)
                {
                    totalFlightTime += returnFlight.FlightTime;
                }

                // Calculate scores for senior stewards with enhanced scoring system
                var eligibleSeniorStewards = seniorStewards
                    .Where(s => s.Role == "Business" &&
                                IsAvailableForFlightPair(s, flight, returnFlight) &&
                                WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours))
                    .Select(s => new
                    {
                        Steward = s,
                        // Use standard fitness calculation with a bonus for underutilized stewards
                        Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                               // Prioritize underutilized stewards
                               (90 - stewardWorkingHours[s.StewardId]) * 0.5f
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                // Assign senior steward if available (required for every flight, but only ONE)
                if (eligibleSeniorStewards.Any())
                {
                    var bestSenior = eligibleSeniorStewards.First().Steward;
                    flightAssignment.BusinessStewards.Add(bestSenior);

                    // Also assign to return flight if it exists
                    if (returnFlightAssignment != null)
                    {
                        returnFlightAssignment.BusinessStewards.Add(bestSenior);
                    }

                    // Update monthly hours
                    UpdateStewardHours(bestSenior.StewardId, totalFlightTime, stewardWorkingHours);

                    // Update last flight time - use the later of the two flights
                    DateTime endTime = flight.ArrivalTime;
                    if (returnFlight != null && returnFlight.ArrivalTime > endTime)
                    {
                        endTime = returnFlight.ArrivalTime;
                    }
                    bestSenior.LastFlightEndTime = endTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(bestSenior.StewardId))
                        schedule.StewardSchedules[bestSenior.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[bestSenior.StewardId].Add(flight);
                    if (returnFlight != null)
                    {
                        schedule.StewardSchedules[bestSenior.StewardId].Add(returnFlight);
                    }
                }
                else
                {
                    // Try again with relaxed constraints if we couldn't find a senior steward
                    // This fallback helps increase assignment rates
                    var fallbackSeniors = businessStewards
                        .Where(s => s.IsSenior &&
                               WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours, relaxed: true))
                        .OrderBy(s => stewardWorkingHours[s.StewardId])
                        .Take(1)
                        .ToList();

                    if (fallbackSeniors.Any())
                    {
                        var seniorSteward = fallbackSeniors.First();
                        flightAssignment.BusinessStewards.Add(seniorSteward);

                        if (returnFlightAssignment != null)
                        {
                            returnFlightAssignment.BusinessStewards.Add(seniorSteward);
                        }

                        // Update tracking
                        UpdateStewardHours(seniorSteward.StewardId, totalFlightTime, stewardWorkingHours);

                        DateTime endTime = flight.ArrivalTime;
                        if (returnFlight != null && returnFlight.ArrivalTime > endTime)
                        {
                            endTime = returnFlight.ArrivalTime;
                        }
                        seniorSteward.LastFlightEndTime = endTime;

                        if (!schedule.StewardSchedules.ContainsKey(seniorSteward.StewardId))
                            schedule.StewardSchedules[seniorSteward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[seniorSteward.StewardId].Add(flight);
                        if (returnFlight != null)
                        {
                            schedule.StewardSchedules[seniorSteward.StewardId].Add(returnFlight);
                        }
                    }
                }

                // Assign remaining business class stewards
                int remainingBusiness = flight.RequiredBusinessCrew - flightAssignment.BusinessStewards.Count;
                int returnRemainingBusiness = returnFlight?.RequiredBusinessCrew ?? 0;
                if (returnFlightAssignment != null)
                {
                    returnRemainingBusiness -= returnFlightAssignment.BusinessStewards.Count;
                }

                if (remainingBusiness > 0)
                {
                    // First try with optimal constraints
                    var availableBusinessStewards = businessStewards
                        .Where(s => !flightAssignment.BusinessStewards.Any(assignedSteward => assignedSteward.StewardId == s.StewardId) &&
                                   !s.IsSenior && // Exclude senior stewards - already handled
                                   IsAvailableForFlightPair(s, flight, returnFlight) &&
                                   WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours))
                        .Select(s => new
                        {
                            Steward = s,
                            // Enhanced scoring with workload balancing
                            Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                                   // Prioritize less utilized stewards
                                   (90 - stewardWorkingHours[s.StewardId]) * 0.5f
                        })
                        .OrderByDescending(x => x.Score)
                        .Take(remainingBusiness)
                        .ToList();

                    // If we didn't find enough, try with relaxed constraints
                    if (availableBusinessStewards.Count < remainingBusiness)
                    {
                        var relaxedBusinessStewards = businessStewards
                            .Where(s => !flightAssignment.BusinessStewards.Any(assignedSteward => assignedSteward.StewardId == s.StewardId) &&
                                       !s.IsSenior && // Exclude senior stewards - already handled
                                       WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours, relaxed: true))
                            .Select(s => new
                            {
                                Steward = s,
                                Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                                       (90 - stewardWorkingHours[s.StewardId]) * 0.5f
                            })
                            .OrderByDescending(x => x.Score)
                            .Take(remainingBusiness - availableBusinessStewards.Count)
                            .ToList();

                        availableBusinessStewards.AddRange(relaxedBusinessStewards);
                    }

                    foreach (var stewardInfo in availableBusinessStewards)
                    {
                        flightAssignment.BusinessStewards.Add(stewardInfo.Steward);

                        // Also assign to return flight if it exists
                        if (returnFlightAssignment != null)
                        {
                            returnFlightAssignment.BusinessStewards.Add(stewardInfo.Steward);
                        }

                        // Update monthly hours
                        UpdateStewardHours(stewardInfo.Steward.StewardId, totalFlightTime, stewardWorkingHours);

                        // Update last flight time - use the later of the two flights
                        DateTime endTime = flight.ArrivalTime;
                        if (returnFlight != null && returnFlight.ArrivalTime > endTime)
                        {
                            endTime = returnFlight.ArrivalTime;
                        }
                        stewardInfo.Steward.LastFlightEndTime = endTime;

                        // Add to steward's schedule
                        if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                            schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);
                        if (returnFlight != null)
                        {
                            schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(returnFlight);
                        }
                    }
                }

                // Assign economy class stewards with enhanced selection
                // First try with standard constraints
                var availableEconomyStewards = economyStewards
                    .Where(s => IsAvailableForFlightPair(s, flight, returnFlight) &&
                               WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours))
                    .Select(s => new
                    {
                        Steward = s,
                        // Enhanced scoring
                        Score = FitnessCalculator.CalculateStewardScore(s, flight, weights, averageMonthlyHours) +
                               // Prioritize underutilized stewards
                               (90 - stewardWorkingHours[s.StewardId]) * 0.5f
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(flight.RequiredEconomyCrew)
                    .ToList();

                // If we didn't find enough, try with relaxed constraints
                if (availableEconomyStewards.Count < flight.RequiredEconomyCrew)
                {
                    var relaxedEconomyStewards = economyStewards
                        .Where(s => !availableEconomyStewards.Any(existing => existing.Steward.StewardId == s.StewardId) &&
                                   WillNotExceedHourLimit(s.StewardId, totalFlightTime, stewardWorkingHours, relaxed: true))
                        .Select(s => new
                        {
                            Steward = s,
                            Score = s.GetSuitabilityScore(flight, averageMonthlyHours) +
                                   (90 - stewardWorkingHours[s.StewardId]) * 0.5f
                        })
                        .OrderByDescending(x => x.Score)
                        .Take(flight.RequiredEconomyCrew - availableEconomyStewards.Count)
                        .ToList();

                    availableEconomyStewards.AddRange(relaxedEconomyStewards);
                }

                foreach (var stewardInfo in availableEconomyStewards)
                {
                    flightAssignment.EconomyStewards.Add(stewardInfo.Steward);

                    // Also assign to return flight if it exists
                    if (returnFlightAssignment != null)
                    {
                        returnFlightAssignment.EconomyStewards.Add(stewardInfo.Steward);
                    }

                    // Update monthly hours
                    UpdateStewardHours(stewardInfo.Steward.StewardId, totalFlightTime, stewardWorkingHours);

                    // Update last flight time - use the later of the two flights
                    DateTime endTime = flight.ArrivalTime;
                    if (returnFlight != null && returnFlight.ArrivalTime > endTime)
                    {
                        endTime = returnFlight.ArrivalTime;
                    }
                    stewardInfo.Steward.LastFlightEndTime = endTime;

                    // Add to steward's schedule
                    if (!schedule.StewardSchedules.ContainsKey(stewardInfo.Steward.StewardId))
                        schedule.StewardSchedules[stewardInfo.Steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(flight);
                    if (returnFlight != null)
                    {
                        schedule.StewardSchedules[stewardInfo.Steward.StewardId].Add(returnFlight);
                    }
                }

                // Only add assignment if we have enough crew to meet minimum requirements
                // This helps prevent partially scheduled flights that can't be completed
                bool shouldScheduleFlight = flightAssignment.BusinessStewards.Count > 0 &&
                                         flightAssignment.EconomyStewards.Count > 0;

                // Always try to add flights with senior stewards (even if incomplete)
                // This ensures high-priority flights get at least partial staffing
                if (shouldScheduleFlight || flightAssignment.HasSeniorSteward)
                {
                    schedule.FlightAssignments.Add(flightAssignment);

                    // Add return flight assignment if it exists
                    if (returnFlightAssignment != null)
                    {
                        schedule.FlightAssignments.Add(returnFlightAssignment);
                        assignedFlightPairs.Add(returnFlight.FlightId);
                    }

                    // Mark this flight as assigned
                    if (returnFlight != null)
                    {
                        assignedFlightPairs.Add(flight.FlightId);
                    }
                }
            }

            // Perform multiple improvement passes
            for (int i = 0; i < 3; i++)
            {
                ImproveSchedule(schedule, stewards);
            }

            // Try to fill any remaining unassigned flights
            FillUnassignedFlights(schedule, stewards);

            // Calculate fitness score for this schedule
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }
        public void FillUnassignedFlights(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // 1. Identify unassigned flights in the week
            var assignedFlightIds = schedule.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .ToHashSet();

            // This assumes all scheduled flights are already in the FlightAssignments list
            // In a real implementation, you'd need access to the full list of flights
            // For now, just gather the ones from the schedule
            var allFlightsInWeek = schedule.FlightAssignments
                .Select(fa => fa.Flight)
                .Where(f => f.DepartureTime >= schedule.WeekStart &&
                            f.DepartureTime < schedule.WeekEnd)
                .ToList();

            var unassignedFlights = allFlightsInWeek
                .Where(f => !assignedFlightIds.Contains(f.FlightId))
                .ToList();

            if (unassignedFlights.Count == 0)
                return;

            // 2. Calculate steward utilization
            var stewardHours = new Dictionary<int, float>();
            foreach (var steward in allStewards)
            {
                stewardHours[steward.StewardId] = steward.MonthlyHours;

                // Add hours from current schedule
                if (schedule.StewardSchedules.TryGetValue(steward.StewardId, out var flights))
                {
                    stewardHours[steward.StewardId] += flights.Sum(f => f.FlightTime);
                }
            }

            // 3. Find underutilized stewards
            var underutilizedStewards = allStewards
                .Where(s => {
                    float hours = stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0;
                    return hours < 50; // Under 50 hours is underutilized
                })
                .OrderBy(s => stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0)
                .ToList();

            // Group stewards by role
            var businessStewards = underutilizedStewards.Where(s => s.Role == "Business").ToList();
            var economyStewards = underutilizedStewards.Where(s => s.Role == "Economy").ToList();
            var seniorStewards = businessStewards.Where(s => s.IsSenior).ToList();

            // 4. Try to assign stewards to each unassigned flight
            foreach (var flight in unassignedFlights)
            {
                var assignment = new FlightAssignment { Flight = flight };

                // Assign senior steward for business class
                var availableSeniors = seniorStewards
                    .Where(s => {
                        // Check available hours
                        float currentHours = stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0;
                        if (currentHours + flight.FlightTime > 90)
                            return false;

                        // Check time conflicts
                        foreach (var existingAssignment in schedule.FlightAssignments)
                        {
                            if ((existingAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId) ||
                                 existingAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId)) &&
                                ((existingAssignment.Flight.DepartureTime <= flight.ArrivalTime &&
                                  existingAssignment.Flight.ArrivalTime >= flight.DepartureTime) ||
                                 (flight.DepartureTime - existingAssignment.Flight.ArrivalTime).TotalHours < 12))
                            {
                                return false;
                            }
                        }

                        return true;
                    })
                    .OrderBy(s => stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0)
                    .ToList();

                if (availableSeniors.Any())
                {
                    var senior = availableSeniors.First();
                    assignment.BusinessStewards.Add(senior);

                    // Update hours
                    if (!stewardHours.ContainsKey(senior.StewardId))
                        stewardHours[senior.StewardId] = 0;
                    stewardHours[senior.StewardId] += flight.FlightTime;
                }

                // Fill remaining business positions
                int remainingBusiness = flight.RequiredBusinessCrew - assignment.BusinessStewards.Count;
                if (remainingBusiness > 0)
                {
                    var availableBusiness = businessStewards
                        .Where(s => !s.IsSenior &&
                                   !assignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId))
                        .Where(s => {
                            // Check available hours
                            float currentHours = stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0;
                            if (currentHours + flight.FlightTime > 90)
                                return false;

                            // Check time conflicts
                            foreach (var existingAssignment in schedule.FlightAssignments)
                            {
                                if ((existingAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId) ||
                                     existingAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId)) &&
                                    ((existingAssignment.Flight.DepartureTime <= flight.ArrivalTime &&
                                      existingAssignment.Flight.ArrivalTime >= flight.DepartureTime) ||
                                     (flight.DepartureTime - existingAssignment.Flight.ArrivalTime).TotalHours < 12))
                                {
                                    return false;
                                }
                            }

                            return true;
                        })
                        .OrderBy(s => stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0)
                        .Take(remainingBusiness)
                        .ToList();

                    foreach (var steward in availableBusiness)
                    {
                        assignment.BusinessStewards.Add(steward);

                        // Update hours
                        if (!stewardHours.ContainsKey(steward.StewardId))
                            stewardHours[steward.StewardId] = 0;
                        stewardHours[steward.StewardId] += flight.FlightTime;
                    }
                }

                // Fill economy positions
                var availableEconomy = economyStewards
                    .Where(s => {
                        // Check available hours
                        float currentHours = stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0;
                        if (currentHours + flight.FlightTime > 90)
                            return false;

                        // Check time conflicts
                        foreach (var existingAssignment in schedule.FlightAssignments)
                        {
                            if ((existingAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId) ||
                                 existingAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId)) &&
                                ((existingAssignment.Flight.DepartureTime <= flight.ArrivalTime &&
                                  existingAssignment.Flight.ArrivalTime >= flight.DepartureTime) ||
                                 (flight.DepartureTime - existingAssignment.Flight.ArrivalTime).TotalHours < 12))
                            {
                                return false;
                            }
                        }

                        return true;
                    })
                    .OrderBy(s => stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0)
                    .Take(flight.RequiredEconomyCrew)
                    .ToList();

                foreach (var steward in availableEconomy)
                {
                    assignment.EconomyStewards.Add(steward);

                    // Update hours
                    if (!stewardHours.ContainsKey(steward.StewardId))
                        stewardHours[steward.StewardId] = 0;
                    stewardHours[steward.StewardId] += flight.FlightTime;
                }

                // If we have a complete or near-complete assignment, add it
                if (assignment.IsComplete() ||
                    (assignment.BusinessStewards.Count > 0 && assignment.EconomyStewards.Count > 0))
                {
                    schedule.FlightAssignments.Add(assignment);

                    // Update steward schedules
                    foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                    {
                        if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                            schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[steward.StewardId].Add(flight);
                    }
                }
            }
        }

        // Helper method to check if a steward is available for a flight pair
        private bool HasSufficientCrew(FlightDto flight,
                        List<StewardDto> businessStewards,
                        List<StewardDto> economyStewards,
                        List<StewardDto> seniorStewards)
        {
            // Check if we have at least 2x the required number of stewards available
            // This increases the chance of finding valid assignments even with constraints
            bool enoughBusiness = businessStewards.Count >= flight.RequiredBusinessCrew * 2;
            bool enoughEconomy = economyStewards.Count >= flight.RequiredEconomyCrew * 2;
            bool hasSeniors = seniorStewards.Count >= 1;

            // Also check if language requirements can be easily met
            bool languageRequirementMet = true;
            if (flight.RequiredLanguageId.HasValue && flight.RequiredLanguageId.Value > 0)
            {
                int langId = flight.RequiredLanguageId.Value;
                languageRequirementMet =
                    businessStewards.Any(s => s.LanguageIds.Contains(langId)) ||
                    economyStewards.Any(s => s.LanguageIds.Contains(langId));
            }

            return enoughBusiness && enoughEconomy && hasSeniors && languageRequirementMet;
        }

        private bool IsAvailableForFlightPair(StewardDto steward, FlightDto flight, FlightDto returnFlight)
        {
            // Check availability for the first flight
            if (!steward.IsAvailable(flight.DepartureTime, flight.FlightTime))
                return false;

            // If there's a return flight, check availability for it too
            if (returnFlight != null)
            {
                // Create a temporary copy to calculate intermediate availability
                var tempSteward = new StewardDto
                {
                    StewardId = steward.StewardId,
                    LastFlightEndTime = flight.ArrivalTime
                };

                if (!tempSteward.IsAvailable(returnFlight.DepartureTime, returnFlight.FlightTime))
                    return false;
            }

            return true;
        }

        // Helper method to check if adding flight hours would exceed the monthly limit
        // Added a relaxed parameter to allow for more flexible scheduling when needed
        private bool WillNotExceedHourLimit(int stewardId, float additionalHours,
                                         Dictionary<int, float> currentHours, bool relaxed = false)
        {
            if (!currentHours.ContainsKey(stewardId))
                return additionalHours <= 90; // New steward

            float limit = relaxed ? 90 : 85; // Use a lower threshold normally for safety margin
            return currentHours[stewardId] + additionalHours <= limit;
        }

        // Helper method to update a steward's hours
        private void UpdateStewardHours(int stewardId, float additionalHours, Dictionary<int, float> currentHours)
        {
            if (!currentHours.ContainsKey(stewardId))
                currentHours[stewardId] = 0;

            currentHours[stewardId] += additionalHours;
        }

        // Improved ImproveSchedule method to respect flight pairs, single senior requirement, and hour limits
        public void ImproveSchedule(WeeklySchedule schedule, List<StewardDto> stewards)
        {
            // Create a working copy of current hours
            var stewardWorkingHours = stewards.ToDictionary(
                s => s.StewardId,
                s => s.MonthlyHours);

            // Update hours based on current schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    UpdateStewardHours(steward.StewardId, assignment.Flight.FlightTime, stewardWorkingHours);
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

            // Create a map of flight pairs
            var flightPairs = new Dictionary<int, FlightAssignment>();
            foreach (var assignment in schedule.FlightAssignments)
            {
                // Find the pair if it exists
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    var returnId = assignment.Flight.ReturnFlightId.Value;
                    var returnAssignment = schedule.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnId);

                    if (returnAssignment != null)
                    {
                        flightPairs[assignment.Flight.FlightId] = returnAssignment;
                    }
                }
            }

            // Attempt to improve each incomplete assignment
            foreach (var incomplete in incompleteAssignments)
            {
                // Check if this is part of a flight pair
                FlightAssignment pairedIncomplete = null;
                if (flightPairs.TryGetValue(incomplete.Flight.FlightId, out var paired))
                {
                    pairedIncomplete = paired;
                }

                // Check if missing business crew with senior steward
                if (!incomplete.HasSeniorSteward || incomplete.BusinessStewards.Count < incomplete.Flight.RequiredBusinessCrew)
                {
                    // Try to find a senior steward from a lower priority flight
                    foreach (var complete in completeAssignments)
                    {
                        // Skip flights with higher or equal priority
                        if (complete.Flight.Priority >= incomplete.Flight.Priority)
                            continue;

                        // Skip flights that are part of a pair with the incomplete flight
                        if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pairedComplete) &&
                            (pairedComplete.Flight.FlightId == incomplete.Flight.FlightId ||
                             (pairedIncomplete != null && pairedComplete.Flight.FlightId == pairedIncomplete.Flight.FlightId)))
                            continue;

                        // Look for senior stewards in this flight
                        var seniorStewards = complete.BusinessStewards.Where(s => s.IsSenior).ToList();

                        // Only try to swap if we need a senior and there's only one senior in the complete flight
                        if (!incomplete.HasSeniorSteward && seniorStewards.Count == 1)
                        {
                            var senior = seniorStewards.First();

                            // Calculate total flight time for the incomplete flight(s)
                            float incompleteTime = incomplete.Flight.FlightTime;
                            if (pairedIncomplete != null)
                            {
                                incompleteTime += pairedIncomplete.Flight.FlightTime;
                            }

                            // Calculate time for the complete flight(s)
                            float completeTime = complete.Flight.FlightTime;
                            if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pairedCompleteFlight))
                            {
                                completeTime += pairedCompleteFlight.Flight.FlightTime;
                            }

                            // Check if this steward can work on the incomplete flight
                            if (senior.IsAvailable(incomplete.Flight.DepartureTime, incomplete.Flight.FlightTime) &&
                                (stewardWorkingHours[senior.StewardId] - completeTime + incompleteTime) <= 90)
                            {
                                // Perform the swap
                                complete.BusinessStewards.Remove(senior);
                                incomplete.BusinessStewards.Add(senior);

                                // If there's a paired flight, update it too
                                if (flightPairs.TryGetValue(complete.Flight.FlightId, out var completePair))
                                {
                                    completePair.BusinessStewards.Remove(senior);
                                }

                                if (pairedIncomplete != null)
                                {
                                    pairedIncomplete.BusinessStewards.Add(senior);
                                }

                                // Update monthly hours
                                stewardWorkingHours[senior.StewardId] =
                                    stewardWorkingHours[senior.StewardId] - completeTime + incompleteTime;

                                // Update steward's schedule
                                if (schedule.StewardSchedules.ContainsKey(senior.StewardId))
                                {
                                    schedule.StewardSchedules[senior.StewardId].Remove(complete.Flight);
                                    schedule.StewardSchedules[senior.StewardId].Add(incomplete.Flight);

                                    if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pair))
                                    {
                                        schedule.StewardSchedules[senior.StewardId].Remove(pair.Flight);
                                    }

                                    if (pairedIncomplete != null)
                                    {
                                        schedule.StewardSchedules[senior.StewardId].Add(pairedIncomplete.Flight);
                                    }
                                }

                                break;
                            }
                        }
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

                        // Skip flights that are part of a pair with the incomplete flight
                        if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pairedComplete) &&
                            (pairedComplete.Flight.FlightId == incomplete.Flight.FlightId ||
                             (pairedIncomplete != null && pairedComplete.Flight.FlightId == pairedIncomplete.Flight.FlightId)))
                            continue;

                        foreach (var steward in complete.EconomyStewards.ToList())
                        {
                            // Calculate total flight time for the incomplete flight(s)
                            float incompleteTime = incomplete.Flight.FlightTime;
                            if (pairedIncomplete != null)
                            {
                                incompleteTime += pairedIncomplete.Flight.FlightTime;
                            }

                            // Calculate time for the complete flight(s)
                            float completeTime = complete.Flight.FlightTime;
                            if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pairedCompleteFlight))
                            {
                                completeTime += pairedCompleteFlight.Flight.FlightTime;
                            }

                            // Check if this steward can work on the incomplete flight
                            if (steward.IsAvailable(incomplete.Flight.DepartureTime, incomplete.Flight.FlightTime) &&
                                (stewardWorkingHours[steward.StewardId] - completeTime + incompleteTime) <= 90)
                            {
                                // Perform the swap
                                complete.EconomyStewards.Remove(steward);
                                incomplete.EconomyStewards.Add(steward);

                                // If there's a paired flight, update it too
                                if (flightPairs.TryGetValue(complete.Flight.FlightId, out var completePair))
                                {
                                    completePair.EconomyStewards.Remove(steward);
                                }

                                if (pairedIncomplete != null)
                                {
                                    pairedIncomplete.EconomyStewards.Add(steward);
                                }

                                // Update monthly hours
                                stewardWorkingHours[steward.StewardId] =
                                    stewardWorkingHours[steward.StewardId] - completeTime + incompleteTime;

                                // Update steward's schedule
                                if (schedule.StewardSchedules.ContainsKey(steward.StewardId))
                                {
                                    schedule.StewardSchedules[steward.StewardId].Remove(complete.Flight);
                                    schedule.StewardSchedules[steward.StewardId].Add(incomplete.Flight);

                                    if (flightPairs.TryGetValue(complete.Flight.FlightId, out var pairComp))
                                    {
                                        schedule.StewardSchedules[steward.StewardId].Remove(pairComp.Flight);
                                    }

                                    if (pairedIncomplete != null)
                                    {
                                        schedule.StewardSchedules[steward.StewardId].Add(pairedIncomplete.Flight);
                                    }
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