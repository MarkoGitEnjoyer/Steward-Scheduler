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

            // Generate weight variations for diversity
            var weightVariations = SchedulingWeights.GenerateVariations(_config.PopulationSize * 2);

            Console.WriteLine($"Generated {weightVariations.Count} different weight configurations");

            // Create schedules with different weight configurations
            foreach (var weights in weightVariations)
            {
                // Create a deep copy of stewards for each run to avoid interference
                var stewardsCopy = DeepCopyStewards(stewards);

                // Generate schedule with this weight configuration
                var schedule = priorityScheduler.GenerateSchedule(flights, stewardsCopy, weekStart, weights);

                // Try to improve it with local search
                priorityScheduler.ImproveSchedule(schedule, stewardsCopy);

                // Calculate initial fitness
                schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

                // Only add if valid and unique enough
                if (!population.Any(p => AreSchedulesSimilar(p, schedule, 0.9f)))
                {
                    population.Add(schedule);
                }

                // If we have enough schedules, stop
                if (population.Count >= _config.PopulationSize)
                    break;
            }

            Console.WriteLine($"Generated {population.Count} valid and diverse initial schedules");

           
            // Log fitness scores of initial population
            Console.WriteLine("Initial population fitness scores:");
            foreach (var schedule in population.OrderByDescending(s => s.FitnessScore))
            {
                Console.WriteLine($"Fitness: {schedule.FitnessScore}, Flights: {schedule.FlightAssignments.Count}");
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

            // Track best solution for reporting
            var bestEver = population.OrderByDescending(s => s.FitnessScore).First().Clone();
            int noImprovementCount = 0;

            Console.WriteLine($"Starting optimization with {_config.MaxGenerations} generations");

            // Evolution loop
            for (int generation = 0; generation < _config.MaxGenerations; generation++)
            {
                // Sort by fitness (descending)
                population = population.OrderByDescending(s => s.FitnessScore).ToList();

                var currentBest = population[0];
                bool improved = false;

                // Check if we've improved the best solution
                if (currentBest.FitnessScore > bestEver.FitnessScore)
                {
                    bestEver = currentBest.Clone();
                    noImprovementCount = 0;
                    improved = true;

                    Console.WriteLine($"Generation {generation}: New best solution found! Fitness: {bestEver.FitnessScore}");
                }
                else
                {
                    noImprovementCount++;
                }

                // Early termination if no improvement for many generations
                if (noImprovementCount > 15 && generation > 20)
                {
                    Console.WriteLine($"Early termination at generation {generation}: No improvement for {noImprovementCount} generations");
                    break;
                }

                // Check if we've reached desired fitness or have no flights (error condition)
                if (population[0].FitnessScore >= 0.95 || population[0].FlightAssignments.Count == 0)
                {
                    Console.WriteLine($"Reached target fitness or error condition at generation {generation}");
                    break;
                }

                // Create new population
                var newPopulation = new List<WeeklySchedule>();

                // Elitism: Keep the best schedules
                int eliteCount = (int)Math.Max(1, Math.Floor(_config.PopulationSize * _config.ElitismRate));
                newPopulation.AddRange(population.Take(eliteCount).Select(s => s.Clone()));

                Console.WriteLine($"Generation {generation}: Keeping {eliteCount} elite schedules");

                // Adaptive mutation rate - increase if we're not improving
                float currentMutationRate = _config.MutationRate;
                if (noImprovementCount > 5)
                {
                    currentMutationRate = Math.Min(0.5f, _config.MutationRate * (1.0f + noImprovementCount * 0.05f));
                    Console.WriteLine($"Increasing mutation rate to {currentMutationRate} due to stagnation");
                }

                // Fill the rest with crossover and mutation
                while (newPopulation.Count < _config.PopulationSize)
                {
                    // Tournament selection: pick two parents
                    var parent1 = SelectParent(population);
                    var parent2 = SelectParent(population);

                    // Avoid same parent
                    while (object.ReferenceEquals(parent1, parent2) && population.Count > 1)
                    {
                        parent2 = SelectParent(population);
                    }

                    WeeklySchedule child = null;

                    // Crossover
                    if (_random.NextDouble() < _config.CrossoverRate)
                    {
                        child = Crossover(parent1, parent2);

                        // Verify the child has valid flights
                        if (HasOverlappingFlights(child))
                        {
                            // If invalid, just clone one parent
                            child = _random.NextDouble() < 0.5 ? parent1.Clone() : parent2.Clone();
                        }
                    }
                    else
                    {
                        // No crossover, just clone one parent
                        child = _random.NextDouble() < 0.5 ? parent1.Clone() : parent2.Clone();
                    }

                    // Mutation
                    if (_random.NextDouble() < currentMutationRate)
                    {
                        var mutatedChild = Mutate(child.Clone(), flights, stewards, currentMutationRate);

                        // Only use the mutated child if it's valid
                        if (!HasOverlappingFlights(mutatedChild) &&
                            mutatedChild.FlightAssignments.Count > 0)
                        {
                            child = mutatedChild;
                        }
                    }

                    // Calculate fitness of the new schedule
                    child.FitnessScore = FitnessCalculator.CalculateScheduleFitness(child, stewards);

                    // Only add if it's a decent solution
                    if (child.FitnessScore > 0.1)
                    {
                        newPopulation.Add(child);
                    }
                }

                // If we lost schedules due to validation, replace them
                while (newPopulation.Count < _config.PopulationSize)
                {
                    // Clone a valid schedule
                    var replacement = newPopulation[_random.Next(newPopulation.Count)].Clone();
                    replacement.FitnessScore = FitnessCalculator.CalculateScheduleFitness(replacement, stewards);
                    newPopulation.Add(replacement);
                }

                // Occasional logging
                if (generation % 5 == 0 || improved)
                {
                    var averageFitness = newPopulation.Average(s => s.FitnessScore);
                    Console.WriteLine($"Gen {generation}: Best={population[0].FitnessScore:F4}, Avg={averageFitness:F4}, Flights={population[0].FlightAssignments.Count}");
                }

                // Replace population
                population = newPopulation;
            }

            // Sort by fitness and return the best schedule
            population = population.OrderByDescending(s => s.FitnessScore).ToList();

            // Compare final solution with best ever found
            if (bestEver.FitnessScore > population[0].FitnessScore)
            {
                Console.WriteLine($"Returning best solution ever found: {bestEver.FitnessScore} vs current best {population[0].FitnessScore}");
                return bestEver;
            }

            Console.WriteLine($"Final best solution: Fitness={population[0].FitnessScore}, Flights={population[0].FlightAssignments.Count}");

            return population[0];
        }

        #region Genetic Operations

        // Tournament selection
        private WeeklySchedule SelectParent(List<WeeklySchedule> population)
        {
            // Pick tournament candidates (larger tournament size = more selection pressure)
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

        // Check if two schedules are very similar (to maintain diversity)
        private bool AreSchedulesSimilar(WeeklySchedule schedule1, WeeklySchedule schedule2, float similarityThreshold)
        {
            int matchingAssignments = 0;
            int totalAssignments = schedule1.FlightAssignments.Count;

            if (totalAssignments == 0 || schedule2.FlightAssignments.Count == 0)
                return false;

            foreach (var assignment1 in schedule1.FlightAssignments)
            {
                var assignment2 = schedule2.FlightAssignments
                    .FirstOrDefault(a => a.Flight.FlightId == assignment1.Flight.FlightId);

                if (assignment2 != null)
                {
                    // Check business stewards
                    bool businessMatch = assignment1.BusinessStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .SequenceEqual(
                            assignment2.BusinessStewards
                            .Select(s => s.StewardId)
                            .OrderBy(id => id));

                    // Check economy stewards
                    bool economyMatch = assignment1.EconomyStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .SequenceEqual(
                            assignment2.EconomyStewards
                            .Select(s => s.StewardId)
                            .OrderBy(id => id));

                    if (businessMatch && economyMatch)
                        matchingAssignments++;
                }
            }

            float similarity = (float)matchingAssignments / totalAssignments;
            return similarity >= similarityThreshold;
        }

        // Crossover operator
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            var child = new WeeklySchedule
            {
                WeekStart = parent1.WeekStart,
                WeekEnd = parent1.WeekEnd
            };

            // Find all flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .ToList();

            // Track processed flights
            var processedFlights = new HashSet<int>();

            // Handle all flights
            foreach (var flightId in allFlightIds)
            {
                if (processedFlights.Contains(flightId))
                    continue;

                var parent1Assignment = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var parent2Assignment = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // Randomly choose which parent to inherit from
                bool useParent1 = _random.NextDouble() < 0.5;

                if (useParent1 && parent1Assignment != null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(parent1Assignment));
                }
                else if (!useParent1 && parent2Assignment != null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(parent2Assignment));
                }
                else if (parent1Assignment != null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(parent1Assignment));
                }
                else if (parent2Assignment != null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(parent2Assignment));
                }

                processedFlights.Add(flightId);
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(child);

            return child;
        }
        private bool VerifyHourConstraints(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // Create a dictionary to track total hours
            var totalHours = allStewards.ToDictionary(
                s => s.StewardId,
                s => s.MonthlyHours
            );

            // Add hours from the schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!totalHours.ContainsKey(steward.StewardId))
                        totalHours[steward.StewardId] = 0;

                    totalHours[steward.StewardId] += flightTime;

                    // If any steward exceeds 90 hours, the schedule is invalid
                    if (totalHours[steward.StewardId] > 90)
                        return false;
                }
            }

            return true;
        }

        // Mutation operator
        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights,
                              List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // Apply multiple mutations based on mutationRate
            int mutations = 1 + (int)(mutationRate * 3); // At least 1, up to 4 mutations

            for (int m = 0; m < mutations; m++)
            {
                // Choose a mutation type with different probabilities
                double randomValue = _random.NextDouble();

                try
                {
                    if (randomValue < 0.4) // 40% chance
                    {
                        // Swap two stewards between flights
                        MutateByStewardSwap(schedule);
                    }
                    else if (randomValue < 0.7) // 30% chance
                    {
                        // Replace a steward with another qualified one
                        MutateByReplacement(schedule, allStewards);
                    }
                    else if (randomValue < 0.9) // 20% chance
                    {
                        // Add a flight that's not currently in the schedule
                        MutateByAddingFlight(schedule, allFlights, allStewards);
                    }
                    else // 10% chance
                    {
                        // Remove a flight from the schedule (more dramatic change)
                        MutateByRemovingFlight(schedule);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue with other mutations
                    Console.WriteLine($"Mutation error: {ex.Message}");
                }
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(schedule);

            // Verify the 90-hour constraint is maintained
            bool isValid = VerifyHourConstraints(schedule, allStewards);

            // If constraint is violated, revert to original schedule
            if (!isValid)
            {
                Console.WriteLine("Mutation resulted in 90-hour constraint violation. Reverting changes.");
                return schedule.Clone(); // This will be replaced with the original in the calling method
            }

            return schedule;
        }
        // Swap stewards between flights
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count < 2)
                return;

            // Attempt several times to find a valid swap
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Pick two random flights
                int idx1 = _random.Next(schedule.FlightAssignments.Count);
                int idx2 = _random.Next(schedule.FlightAssignments.Count);

                // Make sure they're different
                if (idx1 == idx2) continue;

                var flight1 = schedule.FlightAssignments[idx1];
                var flight2 = schedule.FlightAssignments[idx2];

                // Choose steward type to swap
                bool swapBusiness = _random.NextDouble() < 0.5;

                if (swapBusiness)
                {
                    // Swap business stewards
                    if (flight1.BusinessStewards.Count > 0 && flight2.BusinessStewards.Count > 0)
                    {
                        int steward1Idx = _random.Next(flight1.BusinessStewards.Count);
                        int steward2Idx = _random.Next(flight2.BusinessStewards.Count);

                        var steward1 = flight1.BusinessStewards[steward1Idx];
                        var steward2 = flight2.BusinessStewards[steward2Idx];

                        // Skip senior stewards if they're the only senior
                        if ((steward1.IsSenior && flight1.BusinessStewards.Count(s => s.IsSenior) <= 1) ||
                            (steward2.IsSenior && flight2.BusinessStewards.Count(s => s.IsSenior) <= 1))
                            continue;

                        // Check if both stewards can work on the other's flights
                        bool canSwap = steward1.HasLicenseForAircraft(flight2.Flight.AircraftType) &&
                                      steward2.HasLicenseForAircraft(flight1.Flight.AircraftType);

                        if (canSwap)
                        {
                            // Perform swap
                            flight1.BusinessStewards.RemoveAt(steward1Idx);
                            flight2.BusinessStewards.RemoveAt(steward2Idx);

                            flight1.BusinessStewards.Add(steward2);
                            flight2.BusinessStewards.Add(steward1);

                            // Successfully swapped
                            return;
                        }
                    }
                }
                else
                {
                    // Swap economy stewards using similar logic
                    if (flight1.EconomyStewards.Count > 0 && flight2.EconomyStewards.Count > 0)
                    {
                        int steward1Idx = _random.Next(flight1.EconomyStewards.Count);
                        int steward2Idx = _random.Next(flight2.EconomyStewards.Count);

                        var steward1 = flight1.EconomyStewards[steward1Idx];
                        var steward2 = flight2.EconomyStewards[steward2Idx];

                        // Check if stewards can work on the other's flights
                        bool canSwap = steward1.HasLicenseForAircraft(flight2.Flight.AircraftType) &&
                                      steward2.HasLicenseForAircraft(flight1.Flight.AircraftType);

                        if (canSwap)
                        {
                            // Perform swap
                            flight1.EconomyStewards.RemoveAt(steward1Idx);
                            flight2.EconomyStewards.RemoveAt(steward2Idx);

                            flight1.EconomyStewards.Add(steward2);
                            flight2.EconomyStewards.Add(steward1);

                            // Successfully swapped
                            return;
                        }
                    }
                }
            }
        }

        // Replace a steward with another qualified one
        private void MutateByReplacement(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return;

            // Try several attempts to find a valid replacement
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // Pick a random flight
                int flightIdx = _random.Next(schedule.FlightAssignments.Count);
                var flightAssignment = schedule.FlightAssignments[flightIdx];

                // Choose whether to replace business or economy steward
                bool replaceBusiness = _random.NextDouble() < 0.5;

                if (replaceBusiness && flightAssignment.BusinessStewards.Count > 0)
                {
                    // Don't replace senior stewards if there's only one
                    var replaceable = flightAssignment.BusinessStewards
                        .Where(s => !s.IsSenior || flightAssignment.BusinessStewards.Count(bs => bs.IsSenior) > 1)
                        .ToList();

                    if (replaceable.Count == 0)
                        continue;

                    // Pick a random steward to replace
                    int stewardIdx = _random.Next(replaceable.Count);
                    var stewardToReplace = replaceable[stewardIdx];

                    // Find potential replacements
                    var candidates = allStewards
                        .Where(s => s.Role == "Business" &&
                               s.StewardId != stewardToReplace.StewardId &&
                               s.HasLicenseForAircraft(flightAssignment.Flight.AircraftType) &&
                               !flightAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId))
                        .ToList();

                    // If steward being replaced is senior, replacement must also be senior
                    if (stewardToReplace.IsSenior)
                    {
                        candidates = candidates.Where(s => s.IsSenior).ToList();
                    }

                    if (candidates.Any())
                    {
                        // Pick a random replacement
                        var replacement = candidates[_random.Next(candidates.Count)];

                        // Replace in the flight assignment
                        flightAssignment.BusinessStewards.Remove(stewardToReplace);
                        flightAssignment.BusinessStewards.Add(replacement);

                        return;
                    }
                }
                else if (!replaceBusiness && flightAssignment.EconomyStewards.Count > 0)
                {
                    // Pick a random economy steward to replace
                    int stewardIdx = _random.Next(flightAssignment.EconomyStewards.Count);
                    var stewardToReplace = flightAssignment.EconomyStewards[stewardIdx];

                    // Find potential replacements
                    var candidates = allStewards
                        .Where(s => s.Role == "Economy" &&
                               s.StewardId != stewardToReplace.StewardId &&
                               s.HasLicenseForAircraft(flightAssignment.Flight.AircraftType) &&
                               !flightAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId))
                        .ToList();

                    if (candidates.Any())
                    {
                        // Pick a random replacement
                        var replacement = candidates[_random.Next(candidates.Count)];

                        // Replace in the flight assignment
                        flightAssignment.EconomyStewards.Remove(stewardToReplace);
                        flightAssignment.EconomyStewards.Add(replacement);

                        return;
                    }
                }
            }
        }

        // Add a new flight to the schedule
        private void MutateByAddingFlight(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Get all flights for the current week
            var weekFlights = allFlights
                .Where(f => f.DepartureTime >= schedule.WeekStart &&
                       f.DepartureTime < schedule.WeekEnd)
                .ToList();

            // Find already scheduled flight IDs
            var scheduledFlightIds = schedule.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .ToHashSet();

            // Find unscheduled flights
            var unscheduledFlights = weekFlights
                .Where(f => !scheduledFlightIds.Contains(f.FlightId))
                .ToList();

            if (!unscheduledFlights.Any())
                return;

            // Try to add one of the unscheduled flights
            foreach (var flight in unscheduledFlights)
            {
                var assignment = new FlightAssignment { Flight = flight };

                // Find senior steward
                var availableSeniors = allStewards
                    .Where(s => s.Role == "Business" && s.IsSenior &&
                          s.HasLicenseForAircraft(flight.AircraftType) &&
                          CanStewardWorkFlight(s, flight, schedule))
                    .ToList();

                if (!availableSeniors.Any())
                    continue;

                // Assign a senior steward
                var seniorSteward = availableSeniors[_random.Next(availableSeniors.Count)];
                assignment.BusinessStewards.Add(seniorSteward);

                // Find regular business stewards
                int businessNeeded = flight.RequiredBusinessCrew - 1; // -1 for the senior

                var availableBusinessStewards = allStewards
                    .Where(s => s.Role == "Business" && !s.IsSenior &&
                          s.HasLicenseForAircraft(flight.AircraftType) &&
                          CanStewardWorkFlight(s, flight, schedule))
                    .Take(businessNeeded)
                    .ToList();

                // Add business stewards
                foreach (var steward in availableBusinessStewards)
                {
                    assignment.BusinessStewards.Add(steward);
                }

                // Find economy stewards
                var availableEconomyStewards = allStewards
                    .Where(s => s.Role == "Economy" &&
                          s.HasLicenseForAircraft(flight.AircraftType) &&
                          CanStewardWorkFlight(s, flight, schedule))
                    .Take(flight.RequiredEconomyCrew)
                    .ToList();

                // Add economy stewards
                foreach (var steward in availableEconomyStewards)
                {
                    assignment.EconomyStewards.Add(steward);
                }

                // Add flight if it meets minimum staffing requirements
                if (assignment.BusinessStewards.Any(s => s.IsSenior) &&
                    assignment.EconomyStewards.Any())
                {
                    schedule.FlightAssignments.Add(assignment);
                    return;
                }
            }
        }

        // Remove a flight from the schedule
        private void MutateByRemovingFlight(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count <= 2)
                return;

            // Pick a random flight
            int flightIndex = _random.Next(schedule.FlightAssignments.Count);
            var flightToRemove = schedule.FlightAssignments[flightIndex];

            // Remove it
            schedule.FlightAssignments.RemoveAt(flightIndex);
        }

        #endregion

        #region Helper Methods

        // Helper method to check if there are any overlapping flights in the schedule
        private bool HasOverlappingFlights(WeeklySchedule schedule)
        {
            // Check each steward's schedule for overlapping flights
            foreach (var kvp in schedule.StewardSchedules)
            {
                var flights = kvp.Value;

                // Sort flights by departure time
                var orderedFlights = flights.OrderBy(f => f.DepartureTime).ToList();

                // Check for overlaps
                for (int i = 0; i < orderedFlights.Count - 1; i++)
                {
                    for (int j = i + 1; j < orderedFlights.Count; j++)
                    {
                        if (DoFlightsOverlap(orderedFlights[i], orderedFlights[j]))
                        {
                            return true;
                        }
                    }
                }
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

        // Helper method to check if a steward can work a flight without conflicts
        private bool CanStewardWorkFlight(StewardDto steward, FlightDto flight, WeeklySchedule schedule)
        {
            // If steward isn't scheduled yet, check if they have the right license and their total hours won't exceed 90
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
            {
                // Check for license
                if (!steward.HasLicenseForAircraft(flight.AircraftType))
                    return false;

                // Check that adding this flight's hours won't exceed 90 hours
                float totalHours = steward.MonthlyHours + flight.FlightTime;
                if (totalHours > 90)
                    return false;

                return true;
            }

            // Check if steward has license for this aircraft
            if (!steward.HasLicenseForAircraft(flight.AircraftType))
                return false;

            // Calculate current flight hours in this schedule
            float scheduledHours = schedule.StewardSchedules[steward.StewardId].Sum(f => f.FlightTime);

            // Check if adding this flight would exceed 90 hours total
            if (steward.MonthlyHours + scheduledHours + flight.FlightTime > 90)
                return false;

            // Check for overlap or insufficient rest with ALL existing flights
            foreach (var existingFlight in schedule.StewardSchedules[steward.StewardId])
            {
                // Skip the same flight
                if (existingFlight.FlightId == flight.FlightId)
                    continue;

                // Check if flights overlap in time
                if (DoFlightsOverlap(existingFlight, flight))
                    return false;

                // Check if there's enough rest time between flights
                if (!HasEnoughRestBetween(existingFlight, flight))
                    return false;
            }

            return true;
        }
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

                if (steward.LicensedAircraftTypes != null)
                {
                    copy.LicensedAircraftTypes = new List<string>(steward.LicensedAircraftTypes);
                }

                copies.Add(copy);
            }

            return copies;
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

        #endregion
    }
}