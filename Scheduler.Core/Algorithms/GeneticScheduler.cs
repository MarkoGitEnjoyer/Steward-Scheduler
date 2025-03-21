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

                // Validate flight pairs - only add valid schedules
                if (HasValidFlightPairs(schedule))
                {
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
            }

            Console.WriteLine($"Generated {population.Count} valid and diverse initial schedules");

            // Ensure we have enough valid schedules
            while (population.Count < _config.PopulationSize)
            {
                // If we have some valid schedules, use them as templates with slight modifications
                if (population.Count > 0)
                {
                    // Get a random schedule
                    var baseSchedule = population[_random.Next(population.Count)];

                    // Clone it for safety
                    var newSchedule = baseSchedule.Clone();

                    // Apply very small mutations that preserve flight pairs
                    newSchedule = MutatePreservingPairs(newSchedule, flights, stewards);

                    // Only add if it's valid and not too similar to existing schedules
                    if (HasValidFlightPairs(newSchedule) && !HasOverlappingFlights(newSchedule) &&
                        !population.Any(p => AreSchedulesSimilar(p, newSchedule, 0.9f)))
                    {
                        newSchedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(newSchedule, stewards);
                        population.Add(newSchedule);
                    }
                }
                else
                {
                    // If we couldn't generate any valid schedules, try the PriorityScheduler again with different weights
                    var newWeights = new SchedulingWeights
                    {
                        ExperienceWeight = (float)_random.NextDouble(),
                        FeedbackWeight = (float)_random.NextDouble(),
                        WorkloadBalanceWeight = (float)_random.NextDouble(),
                        LanguageWeight = (float)_random.NextDouble()
                    };

                    // Normalize the weights
                    float sum = newWeights.ExperienceWeight + newWeights.FeedbackWeight +
                                newWeights.WorkloadBalanceWeight + newWeights.LanguageWeight;

                    newWeights.ExperienceWeight /= sum;
                    newWeights.FeedbackWeight /= sum;
                    newWeights.WorkloadBalanceWeight /= sum;
                    newWeights.LanguageWeight /= sum;

                    var stewardsCopy = DeepCopyStewards(stewards);

                    var schedule = priorityScheduler.GenerateSchedule(flights, stewardsCopy, weekStart, newWeights);

                    priorityScheduler.ImproveSchedule(schedule, stewardsCopy);

                    if (HasValidFlightPairs(schedule) && !HasOverlappingFlights(schedule))
                    {
                        schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);
                        population.Add(schedule);
                    }
                }
            }

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

                    // Additional validation for flight pairs
                    if (!HasValidFlightPairs(bestEver))
                    {
                        Console.WriteLine("WARNING: Best solution has invalid flight pairs!");
                    }
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

                        // Verify the child has valid flight pairs
                        if (!HasValidFlightPairs(child) || HasOverlappingFlights(child))
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
                        var mutatedChild = MutatePreservingPairs(child.Clone(), flights, stewards, currentMutationRate);

                        // Only use the mutated child if it's valid
                        if (HasValidFlightPairs(mutatedChild) && !HasOverlappingFlights(mutatedChild) &&
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

                // Validate all schedules in new population
                newPopulation = newPopulation.Where(s => HasValidFlightPairs(s)).ToList();

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

            // Final validity check
            if (!HasValidFlightPairs(population[0]))
            {
                Console.WriteLine("WARNING: Final solution has invalid flight pairs. Using best valid solution.");

                // Find best valid solution
                var bestValid = population.FirstOrDefault(s => HasValidFlightPairs(s));

                if (bestValid != null)
                {
                    return bestValid;
                }

                // If no valid solution in final population, use best ever
                if (HasValidFlightPairs(bestEver))
                {
                    return bestEver;
                }

                // Last resort: repair the best solution
                var repaired = RepairFlightPairs(population[0], flights, stewards);
                repaired.FitnessScore = FitnessCalculator.CalculateScheduleFitness(repaired, stewards);
                return repaired;
            }

            return population[0];
        }

        #region Helper Methods for Flight Pair Handling

        // Helper method to get a map of flight pairs
        private Dictionary<int, FlightAssignment> GetFlightPairMap(WeeklySchedule schedule)
        {
            var pairMap = new Dictionary<int, FlightAssignment>();

            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    var returnId = assignment.Flight.ReturnFlightId.Value;
                    var returnAssignment = schedule.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnId);

                    if (returnAssignment != null)
                    {
                        pairMap[assignment.Flight.FlightId] = returnAssignment;
                    }
                }
            }

            return pairMap;
        }

        // Helper method to check if a flight is part of a pair
        private bool IsPartOfPair(FlightAssignment assignment, Dictionary<int, FlightAssignment> pairMap)
        {
            return pairMap.ContainsKey(assignment.Flight.FlightId) ||
                   pairMap.Values.Any(a => a.Flight.FlightId == assignment.Flight.FlightId);
        }

        // Helper method to get the paired flight assignment
        private FlightAssignment GetPairedFlight(FlightAssignment assignment, WeeklySchedule schedule)
        {
            // If this flight has a return flight, find it
            if (assignment.Flight.ReturnFlightId.HasValue)
            {
                return schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == assignment.Flight.ReturnFlightId.Value);
            }

            // If this is a return flight, find the outbound flight
            return schedule.FlightAssignments
                .FirstOrDefault(fa => fa.Flight.ReturnFlightId.HasValue &&
                               fa.Flight.ReturnFlightId.Value == assignment.Flight.FlightId);
        }

        // Validate that all flight pairs have consistent steward assignments
        private bool HasValidFlightPairs(WeeklySchedule schedule)
        {
            var flightPairs = new Dictionary<int, int>();

            // Build the flight pair map
            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    flightPairs[assignment.Flight.FlightId] = assignment.Flight.ReturnFlightId.Value;
                }
            }

            // Check each pair to ensure stewards are the same
            foreach (var pair in flightPairs)
            {
                var outboundAssignment = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Key);

                var returnAssignment = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Value);

                if (outboundAssignment != null && returnAssignment != null)
                {
                    // Compare business stewards
                    var outboundBusinessIds = outboundAssignment.BusinessStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    var returnBusinessIds = returnAssignment.BusinessStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    if (!outboundBusinessIds.SequenceEqual(returnBusinessIds))
                        return false;

                    // Compare economy stewards
                    var outboundEconomyIds = outboundAssignment.EconomyStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    var returnEconomyIds = returnAssignment.EconomyStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    if (!outboundEconomyIds.SequenceEqual(returnEconomyIds))
                        return false;
                }
            }

            return true;
        }

        // Repair a schedule by fixing broken flight pairs
        private WeeklySchedule RepairFlightPairs(WeeklySchedule schedule, List<FlightDto> flights, List<StewardDto> stewards)
        {
            var repairedSchedule = schedule.Clone();
            var flightPairs = new Dictionary<int, int>();

            // Build the flight pair map
            foreach (var assignment in repairedSchedule.FlightAssignments)
            {
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    flightPairs[assignment.Flight.FlightId] = assignment.Flight.ReturnFlightId.Value;
                }
            }

            // Check and fix each pair
            foreach (var pair in flightPairs)
            {
                var outboundAssignment = repairedSchedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Key);

                var returnAssignment = repairedSchedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Value);

                if (outboundAssignment != null && returnAssignment != null)
                {
                    // Check if business stewards match
                    var outboundBusinessIds = outboundAssignment.BusinessStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    var returnBusinessIds = returnAssignment.BusinessStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    if (!outboundBusinessIds.SequenceEqual(returnBusinessIds))
                    {
                        // Fix business stewards - use the outbound flight's stewards
                        returnAssignment.BusinessStewards.Clear();
                        foreach (var steward in outboundAssignment.BusinessStewards)
                        {
                            returnAssignment.BusinessStewards.Add(steward);
                        }
                    }

                    // Check if economy stewards match
                    var outboundEconomyIds = outboundAssignment.EconomyStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    var returnEconomyIds = returnAssignment.EconomyStewards
                        .Select(s => s.StewardId)
                        .OrderBy(id => id)
                        .ToList();

                    if (!outboundEconomyIds.SequenceEqual(returnEconomyIds))
                    {
                        // Fix economy stewards - use the outbound flight's stewards
                        returnAssignment.EconomyStewards.Clear();
                        foreach (var steward in outboundAssignment.EconomyStewards)
                        {
                            returnAssignment.EconomyStewards.Add(steward);
                        }
                    }
                }
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(repairedSchedule);

            return repairedSchedule;
        }

        #endregion

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

        // Improved crossover that preserves flight pairs
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            var child = new WeeklySchedule
            {
                WeekStart = parent1.WeekStart,
                WeekEnd = parent1.WeekEnd
            };

            // Get flight pairs from both parents
            var parent1PairMap = GetFlightPairMap(parent1);
            var parent2PairMap = GetFlightPairMap(parent2);

            // Find all flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .ToList();

            // Track processed flights
            var processedFlights = new HashSet<int>();

            // First, process paired flights to ensure they stay together
            foreach (var flightId in allFlightIds)
            {
                if (processedFlights.Contains(flightId))
                    continue;

                // Check if this flight is part of a pair in either parent
                int? returnFlightId = null;

                if (parent1PairMap.TryGetValue(flightId, out var returnFlight1))
                {
                    returnFlightId = returnFlight1.Flight.FlightId;
                }
                else if (parent2PairMap.TryGetValue(flightId, out var returnFlight2))
                {
                    returnFlightId = returnFlight2.Flight.FlightId;
                }

                if (returnFlightId.HasValue)
                {
                    // This is a paired flight, handle both flights together
                    var parent1OutboundAssignment = parent1.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == flightId);
                    var parent1ReturnAssignment = parent1.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnFlightId.Value);

                    var parent2OutboundAssignment = parent2.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == flightId);
                    var parent2ReturnAssignment = parent2.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnFlightId.Value);

                    // Both parents have both flights
                    bool parent1HasBoth = parent1OutboundAssignment != null && parent1ReturnAssignment != null;
                    bool parent2HasBoth = parent2OutboundAssignment != null && parent2ReturnAssignment != null;

                    // Choose which parent to inherit from (bias towards the better parent)
                    bool useParent1 = (parent1HasBoth && !parent2HasBoth) ||
                                      (parent1HasBoth && parent2HasBoth && _random.NextDouble() < 0.6);

                    if (useParent1 && parent1HasBoth)
                    {
                        // Inherit both flights from parent1
                        child.FlightAssignments.Add(CloneFlightAssignment(parent1OutboundAssignment));
                        child.FlightAssignments.Add(CloneFlightAssignment(parent1ReturnAssignment));
                    }
                    else if (parent2HasBoth)
                    {
                        // Inherit both flights from parent2
                        child.FlightAssignments.Add(CloneFlightAssignment(parent2OutboundAssignment));
                        child.FlightAssignments.Add(CloneFlightAssignment(parent2ReturnAssignment));
                    }
                    else if (parent1OutboundAssignment != null)
                    {
                        // Only one flight exists in parent1
                        child.FlightAssignments.Add(CloneFlightAssignment(parent1OutboundAssignment));
                    }
                    else if (parent2OutboundAssignment != null)
                    {
                        // Only one flight exists in parent2
                        child.FlightAssignments.Add(CloneFlightAssignment(parent2OutboundAssignment));
                    }

                    // Mark both flights as processed
                    processedFlights.Add(flightId);
                    if (returnFlightId.HasValue)
                        processedFlights.Add(returnFlightId.Value);
                }
            }

            // Now handle unpaired flights
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

        // Mutation operator that preserves flight pairs
        private WeeklySchedule MutatePreservingPairs(WeeklySchedule schedule, List<FlightDto> allFlights,
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
                        // Swap two stewards between flights while preserving pairs
                        MutateByStewardSwap(schedule);
                    }
                    else if (randomValue < 0.7) // 30% chance
                    {
                        // Replace a steward with another qualified one
                        MutateByReplacementPreservingPairs(schedule, allStewards);
                    }
                    else if (randomValue < 0.9) // 20% chance
                    {
                        // Add a flight that's not currently in the schedule
                        MutateByAddingFlightPair(schedule, allFlights, allStewards);
                    }
                    else // 10% chance
                    {
                        // Remove a flight from the schedule (more dramatic change)
                        MutateByRemovingFlightPair(schedule);
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

            return schedule;
        }

        // Swap stewards between flights while preserving flight pairs
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count < 2)
                return;

            // Create a map of flight pairs
            var pairMap = GetFlightPairMap(schedule);

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

                // Skip if either flight is the return flight of the other
                if (flight1.Flight.ReturnFlightId.HasValue && flight1.Flight.ReturnFlightId.Value == flight2.Flight.FlightId ||
                    flight2.Flight.ReturnFlightId.HasValue && flight2.Flight.ReturnFlightId.Value == flight1.Flight.FlightId)
                    continue;

                // Find paired flights if they exist
                var flight1Pair = GetPairedFlight(flight1, schedule);
                var flight2Pair = GetPairedFlight(flight2, schedule);

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

                        // Also check paired flights if applicable
                        if (flight1Pair != null)
                            canSwap &= steward2.HasLicenseForAircraft(flight1Pair.Flight.AircraftType);

                        if (flight2Pair != null)
                            canSwap &= steward1.HasLicenseForAircraft(flight2Pair.Flight.AircraftType);

                        if (canSwap)
                        {
                            // Perform swap on first flights
                            flight1.BusinessStewards.RemoveAt(steward1Idx);
                            flight2.BusinessStewards.RemoveAt(steward2Idx);

                            flight1.BusinessStewards.Add(steward2);
                            flight2.BusinessStewards.Add(steward1);

                            // Also swap on paired flights
                            if (flight1Pair != null)
                            {
                                int steward1PairIdx = flight1Pair.BusinessStewards.FindIndex(s => s.StewardId == steward1.StewardId);
                                if (steward1PairIdx >= 0)
                                {
                                    flight1Pair.BusinessStewards.RemoveAt(steward1PairIdx);
                                    flight1Pair.BusinessStewards.Add(steward2);
                                }
                            }

                            if (flight2Pair != null)
                            {
                                int steward2PairIdx = flight2Pair.BusinessStewards.FindIndex(s => s.StewardId == steward2.StewardId);
                                if (steward2PairIdx >= 0)
                                {
                                    flight2Pair.BusinessStewards.RemoveAt(steward2PairIdx);
                                    flight2Pair.BusinessStewards.Add(steward1);
                                }
                            }

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

                        // Also check paired flights
                        if (flight1Pair != null)
                            canSwap &= steward2.HasLicenseForAircraft(flight1Pair.Flight.AircraftType);

                        if (flight2Pair != null)
                            canSwap &= steward1.HasLicenseForAircraft(flight2Pair.Flight.AircraftType);

                        if (canSwap)
                        {
                            // Perform swap on first flights
                            flight1.EconomyStewards.RemoveAt(steward1Idx);
                            flight2.EconomyStewards.RemoveAt(steward2Idx);

                            flight1.EconomyStewards.Add(steward2);
                            flight2.EconomyStewards.Add(steward1);

                            // Also swap on paired flights
                            if (flight1Pair != null)
                            {
                                int steward1PairIdx = flight1Pair.EconomyStewards.FindIndex(s => s.StewardId == steward1.StewardId);
                                if (steward1PairIdx >= 0)
                                {
                                    flight1Pair.EconomyStewards.RemoveAt(steward1PairIdx);
                                    flight1Pair.EconomyStewards.Add(steward2);
                                }
                            }

                            if (flight2Pair != null)
                            {
                                int steward2PairIdx = flight2Pair.EconomyStewards.FindIndex(s => s.StewardId == steward2.StewardId);
                                if (steward2PairIdx >= 0)
                                {
                                    flight2Pair.EconomyStewards.RemoveAt(steward2PairIdx);
                                    flight2Pair.EconomyStewards.Add(steward1);
                                }
                            }

                            // Successfully swapped
                            return;
                        }
                    }
                }
            }
        }

        // Replace a steward with another qualified one, maintaining flight pairs
        private void MutateByReplacementPreservingPairs(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return;

            // Try several attempts to find a valid replacement
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // Pick a random flight
                int flightIdx = _random.Next(schedule.FlightAssignments.Count);
                var flightAssignment = schedule.FlightAssignments[flightIdx];

                // Find paired flight if it exists
                var pairedAssignment = GetPairedFlight(flightAssignment, schedule);

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

                    // Check paired flight compatibility
                    if (pairedAssignment != null)
                    {
                        candidates = candidates
                            .Where(s => s.HasLicenseForAircraft(pairedAssignment.Flight.AircraftType))
                            .ToList();
                    }

                    if (candidates.Any())
                    {
                        // Pick a random replacement
                        var replacement = candidates[_random.Next(candidates.Count)];

                        // Replace in the flight assignment
                        flightAssignment.BusinessStewards.Remove(stewardToReplace);
                        flightAssignment.BusinessStewards.Add(replacement);

                        // Replace in paired flight if it exists
                        if (pairedAssignment != null)
                        {
                            pairedAssignment.BusinessStewards.Remove(stewardToReplace);
                            pairedAssignment.BusinessStewards.Add(replacement);
                        }

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

                    // Check paired flight compatibility
                    if (pairedAssignment != null)
                    {
                        candidates = candidates
                            .Where(s => s.HasLicenseForAircraft(pairedAssignment.Flight.AircraftType))
                            .ToList();
                    }

                    if (candidates.Any())
                    {
                        // Pick a random replacement
                        var replacement = candidates[_random.Next(candidates.Count)];

                        // Replace in the flight assignment
                        flightAssignment.EconomyStewards.Remove(stewardToReplace);
                        flightAssignment.EconomyStewards.Add(replacement);

                        // Replace in paired flight if it exists
                        if (pairedAssignment != null)
                        {
                            pairedAssignment.EconomyStewards.Remove(stewardToReplace);
                            pairedAssignment.EconomyStewards.Add(replacement);
                        }

                        return;
                    }
                }
            }
        }

        // Add a flight pair that's not in the schedule
        private void MutateByAddingFlightPair(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
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

            // Find unscheduled flights that are part of a pair
            var unscheduledPairs = new Dictionary<int, FlightDto>();

            foreach (var flight in weekFlights)
            {
                if (!scheduledFlightIds.Contains(flight.FlightId) &&
                    flight.ReturnFlightId.HasValue &&
                    !scheduledFlightIds.Contains(flight.ReturnFlightId.Value))
                {
                    var returnFlight = allFlights.FirstOrDefault(f => f.FlightId == flight.ReturnFlightId.Value);

                    if (returnFlight != null && returnFlight.DepartureTime < schedule.WeekEnd)
                    {
                        unscheduledPairs[flight.FlightId] = returnFlight;
                    }
                }
            }

            if (!unscheduledPairs.Any())
                return;

            // Try to add one of the unscheduled pairs
            foreach (var pair in unscheduledPairs)
            {
                var outboundFlight = weekFlights.First(f => f.FlightId == pair.Key);
                var returnFlight = pair.Value;

                var outboundAssignment = new FlightAssignment { Flight = outboundFlight };
                var returnAssignment = new FlightAssignment { Flight = returnFlight };

                // Find senior steward for both flights
                var availableSeniors = allStewards
                    .Where(s => s.Role == "Business" && s.IsSenior &&
                          s.HasLicenseForAircraft(outboundFlight.AircraftType) &&
                          s.HasLicenseForAircraft(returnFlight.AircraftType) &&
                          CanStewardWorkFlight(s, outboundFlight, schedule) &&
                          CanStewardWorkFlight(s, returnFlight, schedule))
                    .ToList();

                if (!availableSeniors.Any())
                    continue;

                // Assign a senior steward to both flights
                var seniorSteward = availableSeniors[_random.Next(availableSeniors.Count)];
                outboundAssignment.BusinessStewards.Add(seniorSteward);
                returnAssignment.BusinessStewards.Add(seniorSteward);

                // Find regular business stewards
                int businessNeeded = outboundFlight.RequiredBusinessCrew - 1; // -1 for the senior

                var availableBusinessStewards = allStewards
                    .Where(s => s.Role == "Business" && !s.IsSenior &&
                          s.HasLicenseForAircraft(outboundFlight.AircraftType) &&
                          s.HasLicenseForAircraft(returnFlight.AircraftType) &&
                          CanStewardWorkFlight(s, outboundFlight, schedule) &&
                          CanStewardWorkFlight(s, returnFlight, schedule))
                    .Take(businessNeeded)
                    .ToList();

                // Add business stewards to both flights
                foreach (var steward in availableBusinessStewards)
                {
                    outboundAssignment.BusinessStewards.Add(steward);
                    returnAssignment.BusinessStewards.Add(steward);
                }

                // Find economy stewards
                var availableEconomyStewards = allStewards
                    .Where(s => s.Role == "Economy" &&
                          s.HasLicenseForAircraft(outboundFlight.AircraftType) &&
                          s.HasLicenseForAircraft(returnFlight.AircraftType) &&
                          CanStewardWorkFlight(s, outboundFlight, schedule) &&
                          CanStewardWorkFlight(s, returnFlight, schedule))
                    .Take(outboundFlight.RequiredEconomyCrew)
                    .ToList();

                // Add economy stewards to both flights
                foreach (var steward in availableEconomyStewards)
                {
                    outboundAssignment.EconomyStewards.Add(steward);
                    returnAssignment.EconomyStewards.Add(steward);
                }

                // Add flights if they meet minimum staffing requirements
                if (outboundAssignment.BusinessStewards.Any(s => s.IsSenior) &&
                    outboundAssignment.EconomyStewards.Any())
                {
                    schedule.FlightAssignments.Add(outboundAssignment);
                    schedule.FlightAssignments.Add(returnAssignment);
                    return;
                }
            }
        }

        // Remove a flight pair from the schedule
        private void MutateByRemovingFlightPair(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count <= 2)
                return;

            // Get flight pairs
            var pairMap = GetFlightPairMap(schedule);

            if (!pairMap.Any())
                return;

            // Pick a random pair
            int pairIndex = _random.Next(pairMap.Count);
            var selectedPair = pairMap.ElementAt(pairIndex);

            int outboundId = selectedPair.Key;
            int returnId = selectedPair.Value.Flight.FlightId;

            // Find and remove both flights
            var outboundAssignment = schedule.FlightAssignments
                .FirstOrDefault(fa => fa.Flight.FlightId == outboundId);

            var returnAssignment = schedule.FlightAssignments
                .FirstOrDefault(fa => fa.Flight.FlightId == returnId);

            if (outboundAssignment != null)
                schedule.FlightAssignments.Remove(outboundAssignment);

            if (returnAssignment != null)
                schedule.FlightAssignments.Remove(returnAssignment);
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
            // If steward isn't scheduled yet, they can work any flight (assuming proper licenses)
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                return true;

            // Check if steward has license for this aircraft
            if (!steward.HasLicenseForAircraft(flight.AircraftType))
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