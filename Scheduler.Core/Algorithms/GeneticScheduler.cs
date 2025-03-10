using Scheduler.Core.Models;
using Scheduler.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Algorithms
{
    public class GeneticScheduler
    {
        private readonly Random _random = new Random();
        private readonly GeneticAlgorithmConfig _config;

        public GeneticScheduler(GeneticAlgorithmConfig config = null)
        {
            _config = config ?? new GeneticAlgorithmConfig();
        }

        // Generate initial population using different weight configurations
        public List<WeeklySchedule> GenerateInitialPopulation(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            var population = new List<WeeklySchedule>();
            var priorityScheduler = new PriorityBasedScheduler();

            // Generate weight variations
            var weightVariations = SchedulingWeights.GenerateVariations(_config.PopulationSize);

            // Create schedules with different weight configurations
            foreach (var weights in weightVariations)
            {
                // Create a deep copy of stewards for each run to avoid interference
                var stewardsCopy = DeepCopyStewards(stewards);

                // Generate schedule with this weight configuration
                var schedule = priorityScheduler.GenerateSchedule(flights, stewardsCopy, weekStart, weights);

                // Try to improve it
                priorityScheduler.ImproveSchedule(schedule, stewardsCopy);

                population.Add(schedule);

                // If we have enough schedules, stop
                if (population.Count >= _config.PopulationSize)
                    break;
            }

            // If we still need more schedules, create random variations
            while (population.Count < _config.PopulationSize)
            {
                // Get a random schedule from existing population
                var baseSchedule = population[_random.Next(population.Count)];

                // Create a mutated copy
                var newSchedule = Mutate(baseSchedule.Clone(), flights, stewards);

                population.Add(newSchedule);
            }

            return population;
        }

        // Run the genetic algorithm
        public WeeklySchedule OptimizeSchedule(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            // Generate initial population
            var population = GenerateInitialPopulation(flights, stewards, weekStart);

            // Evolution loop
            for (int generation = 0; generation < _config.MaxGenerations; generation++)
            {
                // Calculate fitness for all schedules if not already done
                foreach (var schedule in population)
                {
                    if (schedule.FitnessScore == 0)
                        schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);
                }

                // Sort by fitness (descending)
                population = population.OrderByDescending(s => s.FitnessScore).ToList();

                // Check if we've reached desired fitness or have no flights (error condition)
                if (population[0].FitnessScore >= 0.95 || population[0].FlightAssignments.Count == 0)
                    break;

                // Create new population
                var newPopulation = new List<WeeklySchedule>();

                // Elitism: Keep the best schedules
                int eliteCount = (int)Math.Max(1, Math.Floor(_config.PopulationSize * _config.ElitismRate));
                newPopulation.AddRange(population.Take(eliteCount).Select(s => s.Clone()));

                // Fill the rest with crossover and mutation
                while (newPopulation.Count < _config.PopulationSize)
                {
                    // Tournament selection: pick two parents
                    var parent1 = SelectParent(population);
                    var parent2 = SelectParent(population);

                    WeeklySchedule child;

                    // Crossover
                    if (_random.NextDouble() < _config.CrossoverRate)
                    {
                        child = Crossover(parent1, parent2);
                    }
                    else
                    {
                        // No crossover, just clone one parent
                        child = _random.NextDouble() < 0.5 ? parent1.Clone() : parent2.Clone();
                    }

                    // Mutation
                    if (_random.NextDouble() < _config.MutationRate)
                    {
                        child = Mutate(child, flights, stewards);
                    }

                    // Calculate fitness of the new schedule
                    child.FitnessScore = FitnessCalculator.CalculateScheduleFitness(child, stewards);
                    newPopulation.Add(child);
                }

                // Replace population
                population = newPopulation;
            }

            // Sort by fitness and return the best schedule
            population = population.OrderByDescending(s => s.FitnessScore).ToList();
            return population[0];
        }

        // Tournament selection
        private WeeklySchedule SelectParent(List<WeeklySchedule> population)
        {
            // Pick 3 random candidates
            int tournamentSize = Math.Min(3, population.Count);
            var candidates = new List<WeeklySchedule>();

            for (int i = 0; i < tournamentSize; i++)
            {
                int idx = _random.Next(population.Count);
                candidates.Add(population[idx]);
            }

            // Return the best
            return candidates.OrderByDescending(s => s.FitnessScore).First();
        }

        // Crossover two parent schedules to create a child
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            // Create a new empty schedule
            var child = new WeeklySchedule
            {
                WeekStart = parent1.WeekStart,
                WeekEnd = parent1.WeekEnd
            };

            // Get all flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .ToList();

            // Track processed flights to avoid duplicate assignments
            var processedFlights = new HashSet<int>();

            // For each flight ID, randomly choose assignments from either parent
            foreach (int flightId in allFlightIds)
            {
                // Skip if already processed as part of a pair
                if (processedFlights.Contains(flightId))
                    continue;

                var flightAssignment1 = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var flightAssignment2 = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                if (flightAssignment1 == null && flightAssignment2 == null)
                    continue;

                // If one parent doesn't have this flight, use the other's assignment
                if (flightAssignment1 == null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(flightAssignment2));
                    processedFlights.Add(flightId);

                    // Handle paired flight if it exists
                    if (flightAssignment2.Flight.ReturnFlightId.HasValue)
                    {
                        var pairedId = flightAssignment2.Flight.ReturnFlightId.Value;
                        var pairedAssignment = parent2.FlightAssignments
                            .FirstOrDefault(fa => fa.Flight.FlightId == pairedId);

                        if (pairedAssignment != null)
                        {
                            child.FlightAssignments.Add(CloneFlightAssignment(pairedAssignment));
                            processedFlights.Add(pairedId);
                        }
                    }
                    continue;
                }

                if (flightAssignment2 == null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(flightAssignment1));
                    processedFlights.Add(flightId);

                    // Handle paired flight if it exists
                    if (flightAssignment1.Flight.ReturnFlightId.HasValue)
                    {
                        var pairedId = flightAssignment1.Flight.ReturnFlightId.Value;
                        var pairedAssignment = parent1.FlightAssignments
                            .FirstOrDefault(fa => fa.Flight.FlightId == pairedId);

                        if (pairedAssignment != null)
                        {
                            child.FlightAssignments.Add(CloneFlightAssignment(pairedAssignment));
                            processedFlights.Add(pairedId);
                        }
                    }
                    continue;
                }

                // Choose which parent's assignment to use
                var selectedAssignment = _random.NextDouble() < 0.5
                    ? flightAssignment1
                    : flightAssignment2;

                var selectedParent = _random.NextDouble() < 0.5 ? parent1 : parent2;

                // Add this flight's assignment
                child.FlightAssignments.Add(CloneFlightAssignment(selectedAssignment));
                processedFlights.Add(flightId);

                // Handle paired flight if it exists
                if (selectedAssignment.Flight.ReturnFlightId.HasValue)
                {
                    var pairedId = selectedAssignment.Flight.ReturnFlightId.Value;
                    var pairedAssignment = selectedParent.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == pairedId);

                    if (pairedAssignment != null)
                    {
                        child.FlightAssignments.Add(CloneFlightAssignment(pairedAssignment));
                        processedFlights.Add(pairedId);
                    }
                }
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(child);

            return child;
        }

        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // If schedule has no flights, don't try to mutate
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // Choose a mutation type
            int mutationType = _random.Next(3);

            try
            {
                switch (mutationType)
                {
                    case 0:
                        // Swap two stewards between flights
                        MutateByStewardSwap(schedule);
                        break;

                    case 1:
                        // Replace a steward with another qualified one not in the schedule
                        MutateByReplacement(schedule, allStewards);
                        break;

                    case 2:
                        // Add a flight that's not currently in the schedule
                        MutateByAddingFlight(schedule, allFlights, allStewards);
                        break;
                }

                // Rebuild steward schedules
                RebuildStewardSchedules(schedule);

                return schedule;
            }
            catch (Exception)
            {
                // If any error occurs, return the original schedule
                return schedule;
            }
        }

        // Swap stewards between flights
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count < 2)
                return;

            // Pick two random flights
            int idx1 = _random.Next(schedule.FlightAssignments.Count);
            int idx2 = _random.Next(schedule.FlightAssignments.Count);

            // Make sure they're different
            int attempts = 0;
            while (idx1 == idx2 && attempts < 5)
            {
                idx2 = _random.Next(schedule.FlightAssignments.Count);
                attempts++;
            }

            if (idx1 == idx2)
                return; // Can't find two different flights

            var flight1 = schedule.FlightAssignments[idx1];
            var flight2 = schedule.FlightAssignments[idx2];

            // If either flight is part of a pair, we need to find and handle both flights in the pair
            var flight1Pair = schedule.FlightAssignments
                .FirstOrDefault(fa => flight1.Flight.ReturnFlightId.HasValue &&
                                     fa.Flight.FlightId == flight1.Flight.ReturnFlightId.Value);

            var flight2Pair = schedule.FlightAssignments
                .FirstOrDefault(fa => flight2.Flight.ReturnFlightId.HasValue &&
                                     fa.Flight.FlightId == flight2.Flight.ReturnFlightId.Value);

            // Choose whether to swap business or economy stewards
            bool swapBusiness = _random.NextDouble() < 0.5;

            if (swapBusiness)
            {
                if (flight1.BusinessStewards.Count > 0 && flight2.BusinessStewards.Count > 0)
                {
                    // Don't swap senior stewards to avoid issues
                    var nonSenior1 = flight1.BusinessStewards.Where(s => !s.IsSenior).ToList();
                    var nonSenior2 = flight2.BusinessStewards.Where(s => !s.IsSenior).ToList();

                    if (nonSenior1.Any() && nonSenior2.Any())
                    {
                        // Pick random non-senior stewards from each flight
                        int stewardIdx1 = _random.Next(nonSenior1.Count);
                        int stewardIdx2 = _random.Next(nonSenior2.Count);

                        var steward1 = nonSenior1[stewardIdx1];
                        var steward2 = nonSenior2[stewardIdx2];

                        // Remove steward1 from flight1
                        flight1.BusinessStewards.Remove(steward1);
                        // Remove steward2 from flight2
                        flight2.BusinessStewards.Remove(steward2);

                        // Add steward2 to flight1
                        flight1.BusinessStewards.Add(steward2);
                        // Add steward1 to flight2
                        flight2.BusinessStewards.Add(steward1);

                        // If these flights are part of pairs, update those too
                        if (flight1Pair != null)
                        {
                            flight1Pair.BusinessStewards.Remove(steward1);
                            flight1Pair.BusinessStewards.Add(steward2);
                        }

                        if (flight2Pair != null)
                        {
                            flight2Pair.BusinessStewards.Remove(steward2);
                            flight2Pair.BusinessStewards.Add(steward1);
                        }
                    }
                }
            }
            else // Swap economy stewards
            {
                if (flight1.EconomyStewards.Count > 0 && flight2.EconomyStewards.Count > 0)
                {
                    // Pick random stewards from each flight
                    int stewardIdx1 = _random.Next(flight1.EconomyStewards.Count);
                    int stewardIdx2 = _random.Next(flight2.EconomyStewards.Count);

                    var steward1 = flight1.EconomyStewards[stewardIdx1];
                    var steward2 = flight2.EconomyStewards[stewardIdx2];

                    // Remove steward1 from flight1
                    flight1.EconomyStewards.Remove(steward1);
                    // Remove steward2 from flight2
                    flight2.EconomyStewards.Remove(steward2);

                    // Add steward2 to flight1
                    flight1.EconomyStewards.Add(steward2);
                    // Add steward1 to flight2
                    flight2.EconomyStewards.Add(steward1);

                    // If these flights are part of pairs, update those too
                    if (flight1Pair != null)
                    {
                        flight1Pair.EconomyStewards.Remove(steward1);
                        flight1Pair.EconomyStewards.Add(steward2);
                    }

                    if (flight2Pair != null)
                    {
                        flight2Pair.EconomyStewards.Remove(steward2);
                        flight2Pair.EconomyStewards.Add(steward1);
                    }
                }
            }
        }

        // Replace a steward with another not in the schedule
        private void MutateByReplacement(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return;

            // Pick a random flight
            int flightIdx = _random.Next(schedule.FlightAssignments.Count);
            var flightAssignment = schedule.FlightAssignments[flightIdx];

            // Find paired flight if it exists
            var pairedAssignment = schedule.FlightAssignments
                .FirstOrDefault(fa => flightAssignment.Flight.ReturnFlightId.HasValue &&
                                     fa.Flight.FlightId == flightAssignment.Flight.ReturnFlightId.Value);

            // Choose whether to replace business or economy steward
            bool replaceBusiness = _random.NextDouble() < 0.5;

            if (replaceBusiness && flightAssignment.BusinessStewards.Count > 0)
            {
                // Don't replace senior stewards if there's only one
                var nonSeniorStewards = flightAssignment.BusinessStewards
                    .Where(s => !s.IsSenior || flightAssignment.BusinessStewards.Count(bs => bs.IsSenior) > 1)
                    .ToList();

                if (nonSeniorStewards.Count == 0)
                    return;

                // Pick a random steward to replace
                int stewardIdx = _random.Next(nonSeniorStewards.Count);
                var stewardToReplace = nonSeniorStewards[stewardIdx];

                // Find a replacement from all business stewards not in this flight
                var possibleReplacements = allStewards
                    .Where(s => s.Role == "Business" &&
                              !flightAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId))
                    .ToList();

                // For senior replacement, we need to ensure the replacement is also senior
                if (stewardToReplace.IsSenior)
                {
                    possibleReplacements = possibleReplacements.Where(s => s.IsSenior).ToList();
                }

                if (possibleReplacements.Count > 0)
                {
                    int replacementIdx = _random.Next(possibleReplacements.Count);
                    var replacement = possibleReplacements[replacementIdx];

                    // Replace in the flight assignment
                    flightAssignment.BusinessStewards.Remove(stewardToReplace);
                    flightAssignment.BusinessStewards.Add(replacement);

                    // Replace in paired flight if it exists
                    if (pairedAssignment != null)
                    {
                        pairedAssignment.BusinessStewards.Remove(stewardToReplace);
                        pairedAssignment.BusinessStewards.Add(replacement);
                    }
                }
            }
            else if (!replaceBusiness && flightAssignment.EconomyStewards.Count > 0)
            {
                // Pick a random steward to replace
                int stewardIdx = _random.Next(flightAssignment.EconomyStewards.Count);
                var stewardToReplace = flightAssignment.EconomyStewards[stewardIdx];

                // Find a replacement from all economy stewards not in this flight
                var possibleReplacements = allStewards
                    .Where(s => s.Role == "Economy" &&
                              !flightAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId))
                    .ToList();

                if (possibleReplacements.Count > 0)
                {
                    int replacementIdx = _random.Next(possibleReplacements.Count);
                    var replacement = possibleReplacements[replacementIdx];

                    // Replace in the flight assignment
                    flightAssignment.EconomyStewards.Remove(stewardToReplace);
                    flightAssignment.EconomyStewards.Add(replacement);

                    // Replace in paired flight if it exists
                    if (pairedAssignment != null)
                    {
                        pairedAssignment.EconomyStewards.Remove(stewardToReplace);
                        pairedAssignment.EconomyStewards.Add(replacement);
                    }
                }
            }
        }

        // Add a flight that's not currently in the schedule
        private void MutateByAddingFlight(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Get all flights from the current week
            var weekFlights = allFlights
                .Where(f => f.DepartureTime >= schedule.WeekStart &&
                           f.DepartureTime < schedule.WeekEnd)
                .ToList();

            // Find flights that are in the current week but not in the schedule
            var scheduledFlightIds = schedule.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .ToHashSet();

            var unscheduledFlights = weekFlights
                .Where(f => !scheduledFlightIds.Contains(f.FlightId))
                .ToList();

            if (unscheduledFlights.Count == 0)
                return;

            // Pick a random unscheduled flight
            int flightIdx = _random.Next(unscheduledFlights.Count);
            var flightToAdd = unscheduledFlights[flightIdx];

            // Check if it has a return flight also unscheduled
            FlightDto returnFlightToAdd = null;
            if (flightToAdd.ReturnFlightId.HasValue)
            {
                returnFlightToAdd = allFlights
                    .FirstOrDefault(f => f.FlightId == flightToAdd.ReturnFlightId.Value &&
                                      !scheduledFlightIds.Contains(f.FlightId));
            }

            // Create new flight assignments
            var newAssignment = new FlightAssignment { Flight = flightToAdd };
            FlightAssignment returnAssignment = null;

            if (returnFlightToAdd != null)
            {
                returnAssignment = new FlightAssignment { Flight = returnFlightToAdd };
            }

            // Calculate current hours for each steward
            var stewardHours = allStewards.ToDictionary(
                s => s.StewardId,
                s => schedule.StewardSchedules.ContainsKey(s.StewardId)
                    ? schedule.StewardSchedules[s.StewardId].Sum(f => f.FlightTime) + s.MonthlyHours
                    : s.MonthlyHours);

            // Find available senior stewards for business class
            var availableSeniors = allStewards
                .Where(s => s.Role == "Business" && s.IsSenior &&
                         stewardHours.GetValueOrDefault(s.StewardId, 0) + flightToAdd.FlightTime +
                            (returnFlightToAdd?.FlightTime ?? 0) <= 90)
                .ToList();

            if (!availableSeniors.Any())
                return; // Can't staff this flight without a senior steward

            // Assign a senior steward
            var seniorSteward = availableSeniors[_random.Next(availableSeniors.Count)];
            newAssignment.BusinessStewards.Add(seniorSteward);

            if (returnAssignment != null)
            {
                returnAssignment.BusinessStewards.Add(seniorSteward);
            }

            // Find regular business stewards
            int remainingBusinessNeeded = flightToAdd.RequiredBusinessCrew - 1; // We already added a senior

            if (remainingBusinessNeeded > 0)
            {
                var availableBusinessStewards = allStewards
                    .Where(s => s.Role == "Business" && !s.IsSenior &&
                             stewardHours.GetValueOrDefault(s.StewardId, 0) + flightToAdd.FlightTime +
                                (returnFlightToAdd?.FlightTime ?? 0) <= 90)
                    .Take(remainingBusinessNeeded)
                    .ToList();

                foreach (var steward in availableBusinessStewards)
                {
                    newAssignment.BusinessStewards.Add(steward);

                    if (returnAssignment != null)
                    {
                        returnAssignment.BusinessStewards.Add(steward);
                    }
                }
            }

            // Find economy stewards
            int economyNeeded = flightToAdd.RequiredEconomyCrew;

            if (economyNeeded > 0)
            {
                var availableEconomyStewards = allStewards
                    .Where(s => s.Role == "Economy" &&
                             stewardHours.GetValueOrDefault(s.StewardId, 0) + flightToAdd.FlightTime +
                                (returnFlightToAdd?.FlightTime ?? 0) <= 90)
                    .Take(economyNeeded)
                    .ToList();

                foreach (var steward in availableEconomyStewards)
                {
                    newAssignment.EconomyStewards.Add(steward);

                    if (returnAssignment != null)
                    {
                        returnAssignment.EconomyStewards.Add(steward);
                    }
                }
            }

            // Add the new flight assignment(s)
            schedule.FlightAssignments.Add(newAssignment);

            if (returnAssignment != null)
            {
                schedule.FlightAssignments.Add(returnAssignment);
            }
        }

        // Rebuild steward schedules after modifications
        private void RebuildStewardSchedules(WeeklySchedule schedule)
        {
            // Clear existing schedules
            schedule.StewardSchedules.Clear();

            // Rebuild from flight assignments
            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards)
                {
                    if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                        schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[steward.StewardId].Add(assignment.Flight);
                }

                foreach (var steward in assignment.EconomyStewards)
                {
                    if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                        schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[steward.StewardId].Add(assignment.Flight);
                }
            }

            // Update LastFlightEndTime for each steward based on their assigned flights
            var stewardLastFlights = new Dictionary<int, DateTime>();

            foreach (var kvp in schedule.StewardSchedules)
            {
                int stewardId = kvp.Key;
                var flights = kvp.Value;

                if (flights.Any())
                {
                    // Find the latest arrival time
                    var lastArrival = flights.Max(f => f.ArrivalTime);

                    // Find the corresponding steward and update their LastFlightEndTime
                    var steward = schedule.FlightAssignments
                        .SelectMany(fa => fa.BusinessStewards.Concat(fa.EconomyStewards))
                        .FirstOrDefault(s => s.StewardId == stewardId);

                    if (steward != null)
                    {
                        steward.LastFlightEndTime = lastArrival;
                    }
                }
            }
        }

        // Helper methods

        private FlightAssignment CloneFlightAssignment(FlightAssignment assignment)
        {
            var clone = new FlightAssignment
            {
                Flight = assignment.Flight
            };

            clone.BusinessStewards.AddRange(assignment.BusinessStewards);
            clone.EconomyStewards.AddRange(assignment.EconomyStewards);

            return clone;
        }

        private List<StewardDto> DeepCopyStewards(List<StewardDto> stewards)
        {
            var copies = new List<StewardDto>();

            foreach (var steward in stewards)
            {
                var copy = new StewardDto
                {
                    StewardId = steward.StewardId,
                    FirstName = steward.FirstName,
                    LastName = steward.LastName,
                    Role = steward.Role,
                    IsSenior = steward.IsSenior,
                    JoiningDate = steward.JoiningDate,
                    LastFlightEndTime = steward.LastFlightEndTime,
                    MonthlyHours = steward.MonthlyHours,
                    PositiveFeedbackCount = steward.PositiveFeedbackCount,
                    NegativeFeedbackCount = steward.NegativeFeedbackCount
                };

                copy.LicenseIds.AddRange(steward.LicenseIds);
                copy.LanguageIds.AddRange(steward.LanguageIds);

                copies.Add(copy);
            }

            return copies;
        }
    }
}