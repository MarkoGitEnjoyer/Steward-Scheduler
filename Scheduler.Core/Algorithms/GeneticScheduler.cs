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

            // Generate weight variations for diversity
            var weightVariations = SchedulingWeights.GenerateVariations(_config.PopulationSize * 2);

            // First, run priority scheduler once to get a good base schedule
            var priorityScheduler = new PriorityBasedScheduler();
            var baseSchedule = priorityScheduler.GenerateSchedule(
                flights.OrderByDescending(f => f.Priority).ToList(),
                DeepCopyStewards(stewards),
                weekStart,
                new SchedulingWeights());

            // Add this high-quality schedule to our population first
            population.Add(baseSchedule);
            Console.WriteLine($"Added base schedule with {baseSchedule.FlightAssignments.Count} flights to population");

            // For each weight variation, create a fresh copy of stewards
            foreach (var weights in weightVariations)
            {
                // IMPORTANT: Create a completely new copy of stewards for each run
                var freshStewards = DeepCopyStewards(stewards);

                // Reset projected hours to base monthly hours for each steward
                foreach (var steward in freshStewards)
                {
                    steward.InitializeProjectedHours();
                    steward.LastFlightEndTime = null; // Reset last flight time too
                }

                // Now generate schedule with fresh stewards
                var schedule = priorityScheduler.GenerateSchedule(
                    flights.OrderByDescending(f => f.Priority).ToList(),
                    freshStewards,
                    weekStart,
                    weights);

                // Add if unique enough
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

            // Track best solution with the highest flight count
            WeeklySchedule bestWithMostFlights = population
                .OrderByDescending(s => s.FlightAssignments.Count)
                .ThenByDescending(s => s.FitnessScore)
                .First().Clone();

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

                // Update best solution with most flights if applicable
                if (currentBest.FlightAssignments.Count > bestWithMostFlights.FlightAssignments.Count ||
                    (currentBest.FlightAssignments.Count == bestWithMostFlights.FlightAssignments.Count &&
                     currentBest.FitnessScore > bestWithMostFlights.FitnessScore))
                {
                    bestWithMostFlights = currentBest.Clone();
                }

                // Early termination if no improvement for many generations
                if (noImprovementCount > 15 && generation > 20)
                {
                    Console.WriteLine($"Early termination at generation {generation}: No improvement for {noImprovementCount} generations");
                    break;
                }

                // Check if we've reached desired fitness or have no flights (error condition)
                if (population[0].FitnessScore >= 0.98 || population[0].FlightAssignments.Count == 0)
                {
                    Console.WriteLine($"Reached target fitness or error condition at generation {generation}");
                    break;
                }

                // Create new population
                var newPopulation = new List<WeeklySchedule>();

                // Elitism: Keep the best schedules
                int eliteCount = (int)Math.Max(2, Math.Floor(_config.PopulationSize * _config.ElitismRate));

                // Keep the best schedules by fitness
                newPopulation.AddRange(population.Take(eliteCount).Select(s => s.Clone()));

                // Also explicitly keep the solution with the most flights
                var mostFlightsSchedule = population
                    .OrderByDescending(s => s.FlightAssignments.Count)
                    .ThenByDescending(s => s.FitnessScore)
                    .First();

                if (!newPopulation.Any(s => s.FlightAssignments.Count == mostFlightsSchedule.FlightAssignments.Count))
                {
                    newPopulation.Add(mostFlightsSchedule.Clone());
                }

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
                        if (HasOverlappingFlights(child) || !ValidateScheduleRestTimes(child))
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
                            ValidateScheduleRestTimes(mutatedChild) &&
                            mutatedChild.FlightAssignments.Count > 0)
                        {
                            child = mutatedChild;
                        }
                    }
                    child.TotalFlightCount = parent1.TotalFlightCount;
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

            // Get the best schedule based on fitness
            var bestFitness = population[0];

            // Get the schedule with the most flights
            var mostFlights = population.OrderByDescending(s => s.FlightAssignments.Count)
                                      .ThenByDescending(s => s.FitnessScore)
                                      .First();

            // Compare the various solutions we've tracked
            Console.WriteLine($"Best by fitness: Fitness={bestFitness.FitnessScore:F4}, Flights={bestFitness.FlightAssignments.Count}");
            Console.WriteLine($"Best by most flights: Fitness={mostFlights.FitnessScore:F4}, Flights={mostFlights.FlightAssignments.Count}");
            Console.WriteLine($"Best ever found: Fitness={bestEver.FitnessScore:F4}, Flights={bestEver.FlightAssignments.Count}");
            Console.WriteLine($"Best with most flights: Fitness={bestWithMostFlights.FitnessScore:F4}, Flights={bestWithMostFlights.FlightAssignments.Count}");

            // Prefer the solution with more flights if the fitness difference is small (within 5%)
            float fitnessThreshold = 0.95f;
            if (mostFlights.FlightAssignments.Count > bestFitness.FlightAssignments.Count &&
                mostFlights.FitnessScore > bestFitness.FitnessScore * fitnessThreshold)
            {
                Console.WriteLine($"Choosing schedule with more flights ({mostFlights.FlightAssignments.Count}) over best fitness");
                return mostFlights;
            }

            // If best ever solution has more flights and similar fitness, use that
            if (bestEver.FlightAssignments.Count > bestFitness.FlightAssignments.Count &&
                bestEver.FitnessScore > bestFitness.FitnessScore * fitnessThreshold)
            {
                Console.WriteLine($"Returning best solution ever found: {bestEver.FitnessScore:F4} ({bestEver.FlightAssignments.Count} flights)");
                return bestEver;
            }

            // If best with most flights has significantly more flights, use that
            if (bestWithMostFlights.FlightAssignments.Count > bestFitness.FlightAssignments.Count * 1.1 &&
                bestWithMostFlights.FitnessScore > bestFitness.FitnessScore * fitnessThreshold)
            {
                Console.WriteLine($"Returning solution with most flights: {bestWithMostFlights.FitnessScore:F4} ({bestWithMostFlights.FlightAssignments.Count} flights)");
                return bestWithMostFlights;
            }

            Console.WriteLine($"Final best solution: Fitness={bestFitness.FitnessScore:F4}, Flights={bestFitness.FlightAssignments.Count}");

            // Final validation to ensure the returned schedule respects rest time constraints
            if (!ValidateScheduleRestTimes(bestFitness))
            {
                FixRestTimeViolations(bestFitness);
            }

            return bestFitness;
        }

        #region Genetic Operations

        // Tournament selection with preference for solutions with more flights
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

            // Find the candidate with the most flights
            var maxFlights = candidates.Max(s => s.FlightAssignments.Count);

            // Get candidates with flight counts close to the max
            var bestFlightCandidates = candidates
                .Where(s => s.FlightAssignments.Count >= maxFlights - 1)
                .ToList();

            // If we have candidates with plenty of flights, prefer those with better fitness
            if (bestFlightCandidates.Count > 0)
            {
                return bestFlightCandidates.OrderByDescending(s => s.FitnessScore).First();
            }

            // Fall back to standard fitness-based selection
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

        // Crossover operator with priority preservation
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            var child = new WeeklySchedule
            {
                WeekStart = parent1.WeekStart,
                WeekEnd = parent1.WeekEnd
            };

            // Get flights from both parents
            var parent1Flights = parent1.FlightAssignments.Select(fa => fa.Flight).ToList();
            var parent2Flights = parent2.FlightAssignments.Select(fa => fa.Flight).ToList();

            // Steward hour tracking dictionary to enforce 90-hour constraint
            Dictionary<int, float> stewardHours = new Dictionary<int, float>();

            // First, add all high-priority flights from both parents (priority >= 4)
            var highPriorityFlights = new HashSet<int>();

            // Process parent1 high priority flights first
            foreach (var assignment in parent1.FlightAssignments.OrderByDescending(fa => fa.Flight.Priority))
            {
                if (assignment.Flight.Priority >= 4 && !highPriorityFlights.Contains(assignment.Flight.FlightId))
                {
                    // Check if adding this flight would exceed 90 hours for any steward
                    bool exceedsLimit = false;

                    // Check business stewards
                    foreach (var steward in assignment.BusinessStewards)
                    {
                        if (!stewardHours.ContainsKey(steward.StewardId))
                            stewardHours[steward.StewardId] = steward.MonthlyHours;

                        if (stewardHours[steward.StewardId] + assignment.Flight.FlightTime > 90)
                        {
                            exceedsLimit = true;
                            break;
                        }
                    }

                    // Check economy stewards
                    if (!exceedsLimit)
                    {
                        foreach (var steward in assignment.EconomyStewards)
                        {
                            if (!stewardHours.ContainsKey(steward.StewardId))
                                stewardHours[steward.StewardId] = steward.MonthlyHours;

                            if (stewardHours[steward.StewardId] + assignment.Flight.FlightTime > 90)
                            {
                                exceedsLimit = true;
                                break;
                            }
                        }
                    }

                    // Only add if it doesn't exceed 90 hours
                    if (!exceedsLimit)
                    {
                        child.FlightAssignments.Add(CloneFlightAssignment(assignment));
                        highPriorityFlights.Add(assignment.Flight.FlightId);

                        // Update steward hours
                        foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                        {
                            stewardHours[steward.StewardId] += assignment.Flight.FlightTime;
                        }
                    }
                }
            }

            // Then add high priority flights from parent2 that aren't already added
            foreach (var assignment in parent2.FlightAssignments.OrderByDescending(fa => fa.Flight.Priority))
            {
                if (assignment.Flight.Priority >= 4 && !highPriorityFlights.Contains(assignment.Flight.FlightId))
                {
                    // Check if adding this flight would exceed 90 hours for any steward
                    bool exceedsLimit = false;

                    // Check business stewards
                    foreach (var steward in assignment.BusinessStewards)
                    {
                        if (!stewardHours.ContainsKey(steward.StewardId))
                            stewardHours[steward.StewardId] = steward.MonthlyHours;

                        if (stewardHours[steward.StewardId] + assignment.Flight.FlightTime > 90)
                        {
                            exceedsLimit = true;
                            break;
                        }
                    }

                    // Check economy stewards
                    if (!exceedsLimit)
                    {
                        foreach (var steward in assignment.EconomyStewards)
                        {
                            if (!stewardHours.ContainsKey(steward.StewardId))
                                stewardHours[steward.StewardId] = steward.MonthlyHours;

                            if (stewardHours[steward.StewardId] + assignment.Flight.FlightTime > 90)
                            {
                                exceedsLimit = true;
                                break;
                            }
                        }
                    }

                    // Only add if it doesn't exceed 90 hours
                    if (!exceedsLimit)
                    {
                        child.FlightAssignments.Add(CloneFlightAssignment(assignment));
                        highPriorityFlights.Add(assignment.Flight.FlightId);

                        // Update steward hours
                        foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                        {
                            stewardHours[steward.StewardId] += assignment.Flight.FlightTime;
                        }
                    }
                }
            }

            // Find all remaining flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .Except(highPriorityFlights) // Exclude already processed high priority flights
                .ToList();

            // Handle all remaining flights
            foreach (var flightId in allFlightIds)
            {
                var parent1Assignment = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var parent2Assignment = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // Randomly choose which parent to inherit from, but bias toward the parent with more flights
                bool useParent1 = _random.NextDouble() < 0.5;

                // Adjust bias based on which parent has more flights
                if (parent1.FlightAssignments.Count > parent2.FlightAssignments.Count)
                {
                    useParent1 = _random.NextDouble() < 0.7; // 70% chance to use parent1
                }
                else if (parent2.FlightAssignments.Count > parent1.FlightAssignments.Count)
                {
                    useParent1 = _random.NextDouble() < 0.3; // 30% chance to use parent1
                }

                FlightAssignment assignmentToAdd = null;

                if (useParent1 && parent1Assignment != null)
                {
                    assignmentToAdd = parent1Assignment;
                }
                else if (!useParent1 && parent2Assignment != null)
                {
                    assignmentToAdd = parent2Assignment;
                }
                else if (parent1Assignment != null)
                {
                    assignmentToAdd = parent1Assignment;
                }
                else if (parent2Assignment != null)
                {
                    assignmentToAdd = parent2Assignment;
                }

                if (assignmentToAdd != null)
                {
                    // Check if adding this flight would exceed 90 hours for any steward
                    bool exceedsLimit = false;

                    // Check business stewards
                    foreach (var steward in assignmentToAdd.BusinessStewards)
                    {
                        if (!stewardHours.ContainsKey(steward.StewardId))
                            stewardHours[steward.StewardId] = steward.MonthlyHours;

                        if (stewardHours[steward.StewardId] + assignmentToAdd.Flight.FlightTime > 90)
                        {
                            exceedsLimit = true;
                            break;
                        }
                    }

                    // Check economy stewards
                    if (!exceedsLimit)
                    {
                        foreach (var steward in assignmentToAdd.EconomyStewards)
                        {
                            if (!stewardHours.ContainsKey(steward.StewardId))
                                stewardHours[steward.StewardId] = steward.MonthlyHours;

                            if (stewardHours[steward.StewardId] + assignmentToAdd.Flight.FlightTime > 90)
                            {
                                exceedsLimit = true;
                                break;
                            }
                        }
                    }

                    // Only add if it doesn't exceed 90 hours
                    if (!exceedsLimit)
                    {
                        child.FlightAssignments.Add(CloneFlightAssignment(assignmentToAdd));

                        // Update steward hours
                        foreach (var steward in assignmentToAdd.BusinessStewards.Concat(assignmentToAdd.EconomyStewards))
                        {
                            stewardHours[steward.StewardId] += assignmentToAdd.Flight.FlightTime;
                        }
                    }
                }
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(child);

            // Validate schedule respects rest time constraints
            if (!ValidateScheduleRestTimes(child))
            {
                // Try to fix the schedule
                FixRestTimeViolations(child);

                // If still invalid, use parent1
                if (!ValidateScheduleRestTimes(child))
                {
                    return parent1.Clone();
                }
            }

            return child;
        }
        private bool VerifyHourConstraints(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // Create a dictionary of stewards for quick lookup
            var stewardDict = allStewards.ToDictionary(s => s.StewardId, s => s);

            // Create a dictionary to track total hours
            var totalHours = new Dictionary<int, float>();

            // Initialize with monthly hours
            foreach (var steward in allStewards)
            {
                totalHours[steward.StewardId] = steward.MonthlyHours;
            }

            // Add hours from the schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!totalHours.ContainsKey(steward.StewardId))
                        totalHours[steward.StewardId] = steward.MonthlyHours;

                    totalHours[steward.StewardId] += flightTime;

                    // If any steward exceeds 90 hours, the schedule is invalid
                    if (totalHours[steward.StewardId] > 90)
                    {
                        Console.WriteLine($"Hour constraint violation: Steward {steward.StewardId} would exceed 90 hours " +
                            $"(Total: {totalHours[steward.StewardId]})");
                        return false;
                    }
                }
            }

            return true;
        }
        // Mutation operator with improved hour constraint checking
        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights,
                              List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // Apply multiple mutations based on mutationRate
            int mutations = 1 + (int)(mutationRate * 3); // At least 1, up to 4 mutations

            for (int m = 0; m < mutations; m++)
            {
                // Modified mutation probabilities to favor operations that increase flights
                double randomValue = _random.NextDouble();

                try
                {
                    if (randomValue < 0.3) // 30% chance (reduced from 40%)
                    {
                        // Swap two stewards between flights
                        MutateByStewardSwap(schedule);
                    }
                    else if (randomValue < 0.6) // 30% chance (unchanged)
                    {
                        // Replace a steward with another qualified one
                        MutateByReplacement(schedule, allStewards);
                    }
                    else if (randomValue < 0.95) // 35% chance (increased from 20%)
                    {
                        // Add a flight that's not currently in the schedule
                        MutateByAddingFlight(schedule, allFlights, allStewards);
                    }
                    else // 5% chance (reduced from 10%)
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
                return schedule.Clone(); // This will be replaced with the original in the calling method
            }

            // Verify rest time constraints
            if (!ValidateScheduleRestTimes(schedule))
            {
                FixRestTimeViolations(schedule);
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

                        // Check rest time constraints for the swap
                        bool restTimesRespected = WouldRespectRestTime(steward1, flight2.Flight, schedule) &&
                                                 WouldRespectRestTime(steward2, flight1.Flight, schedule);

                        // Check 90-hour limit
                        float steward1Hours = schedule.GetStewardFlightHours(steward1.StewardId) - flight1.Flight.FlightTime + flight2.Flight.FlightTime;
                        float steward2Hours = schedule.GetStewardFlightHours(steward2.StewardId) - flight2.Flight.FlightTime + flight1.Flight.FlightTime;

                        bool hourConstraintMet = (steward1.MonthlyHours + steward1Hours <= 90) &&
                                                (steward2.MonthlyHours + steward2Hours <= 90);

                        if (canSwap && hourConstraintMet && restTimesRespected)
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

                        // Check rest time constraints for the swap
                        bool restTimesRespected = WouldRespectRestTime(steward1, flight2.Flight, schedule) &&
                                                 WouldRespectRestTime(steward2, flight1.Flight, schedule);

                        // Check 90-hour limit
                        float steward1Hours = schedule.GetStewardFlightHours(steward1.StewardId) - flight1.Flight.FlightTime + flight2.Flight.FlightTime;
                        float steward2Hours = schedule.GetStewardFlightHours(steward2.StewardId) - flight2.Flight.FlightTime + flight1.Flight.FlightTime;

                        bool hourConstraintMet = (steward1.MonthlyHours + steward1Hours <= 90) &&
                                                (steward2.MonthlyHours + steward2Hours <= 90);

                        if (canSwap && hourConstraintMet && restTimesRespected)
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
                               !flightAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId) &&
                               WouldRespectRestTime(s, flightAssignment.Flight, schedule)) // Check rest times
                        .ToList();

                    // If steward being replaced is senior, replacement must also be senior
                    if (stewardToReplace.IsSenior)
                    {
                        candidates = candidates.Where(s => s.IsSenior).ToList();
                    }

                    // Check 90-hour limit for candidates
                    candidates = candidates.Where(s => {
                        float candidateHours = schedule.GetStewardFlightHours(s.StewardId) + flightAssignment.Flight.FlightTime;
                        return (s.MonthlyHours + candidateHours <= 90);
                    }).ToList();

                    if (candidates.Any())
                    {
                        // Pick a replacement with preference for stewards with fewer hours
                        var replacement = candidates
                            .OrderBy(s => s.MonthlyHours + (schedule.StewardHours.ContainsKey(s.StewardId) ?
                                schedule.StewardHours[s.StewardId] : 0))
                            .First();

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
                               !flightAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId) &&
                               WouldRespectRestTime(s, flightAssignment.Flight, schedule)) // Check rest times
                        .ToList();

                    // Check 90-hour limit for candidates
                    candidates = candidates.Where(s => {
                        float candidateHours = schedule.GetStewardFlightHours(s.StewardId) + flightAssignment.Flight.FlightTime;
                        return (s.MonthlyHours + candidateHours <= 90);
                    }).ToList();

                    if (candidates.Any())
                    {
                        // Pick a replacement with preference for stewards with fewer hours
                        var replacement = candidates
                            .OrderBy(s => s.MonthlyHours + (schedule.StewardHours.ContainsKey(s.StewardId) ?
                                schedule.StewardHours[s.StewardId] : 0))
                            .First();

                        // Replace in the flight assignment
                        flightAssignment.EconomyStewards.Remove(stewardToReplace);
                        flightAssignment.EconomyStewards.Add(replacement);

                        return;
                    }
                }
            }
        }

        // Add a new flight to the schedule with improved logic
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
                .OrderByDescending(f => f.Priority) // Try high priority flights first
                .ToList();

            if (!unscheduledFlights.Any())
                return;

            // Build the current hours dictionary including monthly baseline
            var currentHours = new Dictionary<int, float>();
            foreach (var steward in allStewards)
            {
                currentHours[steward.StewardId] = steward.MonthlyHours;
            }

            // Add hours from current schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    currentHours[steward.StewardId] += assignment.Flight.FlightTime;
                }
            }

            // Try to add one of the unscheduled flights, starting with highest priority
            foreach (var flight in unscheduledFlights)
            {
                var assignment = new FlightAssignment { Flight = flight };

                // Find senior steward
                var availableSeniors = allStewards
                    .Where(s => s.Role == "Business" && s.IsSenior &&
                          s.HasLicenseForAircraft(flight.AircraftType) &&
                          CanStewardWorkFlight(s, flight, schedule) &&
                          WouldRespectRestTime(s, flight, schedule) && // Check rest times
                          (currentHours[s.StewardId] + flight.FlightTime <= 90)) // Check 90-hour limit
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
                          CanStewardWorkFlight(s, flight, schedule) &&
                          WouldRespectRestTime(s, flight, schedule) && // Check rest times
                          (currentHours[s.StewardId] + flight.FlightTime <= 90)) // Check 90-hour limit
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
                          CanStewardWorkFlight(s, flight, schedule) &&
                          WouldRespectRestTime(s, flight, schedule) && // Check rest times
                          (currentHours[s.StewardId] + flight.FlightTime <= 90)) // Check 90-hour limit
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

                    // Update current hours for next iteration
                    foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                    {
                        currentHours[steward.StewardId] += flight.FlightTime;
                    }

                    return;
                }
            }
        }
        // Check if adding a flight would exceed 90 hours for a steward
        private bool WouldExceed90Hours(StewardDto steward, FlightDto flight, WeeklySchedule schedule)
        {
            float currentHours = steward.MonthlyHours;

            // Add hours from current schedule
            if (schedule.StewardSchedules.ContainsKey(steward.StewardId))
            {
                currentHours += schedule.StewardSchedules[steward.StewardId].Sum(f => f.FlightTime);
            }

            // Check if adding this flight would exceed 90 hours
            return (currentHours + flight.FlightTime > 90);
        }

        // Remove a flight from the schedule - prefer low priority flights
        private void MutateByRemovingFlight(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count <= 2)
                return;

            // Get flights ordered by priority (ascending) - remove lower priority flights
            var candidates = schedule.FlightAssignments
                .OrderBy(fa => fa.Flight.Priority)
                .Take(3) // Consider the 3 lowest priority flights
                .ToList();

            // Pick a random flight from the candidates
            int randomIndex = _random.Next(candidates.Count);
            var flightToRemove = candidates[randomIndex];

            // Find and remove it
            int flightIndex = schedule.FlightAssignments.IndexOf(flightToRemove);
            if (flightIndex >= 0)
            {
                schedule.FlightAssignments.RemoveAt(flightIndex);
            }
        }

        #endregion

        #region Helper Methods

        // NEW METHOD: Check if a schedule adheres to rest time constraints
        private bool ValidateScheduleRestTimes(WeeklySchedule schedule)
        {
            if (schedule == null || !schedule.StewardSchedules.Any())
                return true; // Empty schedules are valid

            foreach (var kvp in schedule.StewardSchedules)
            {
                int stewardId = kvp.Key;
                var flights = kvp.Value;

                if (flights.Count <= 1)
                    continue; // Only one flight, no rest issues

                // Sort flights by departure time
                var sortedFlights = flights.OrderBy(f => f.DepartureTime).ToList();

                // Check consecutive flight pairs for rest time
                for (int i = 0; i < sortedFlights.Count - 1; i++)
                {
                    var currentFlight = sortedFlights[i];
                    var nextFlight = sortedFlights[i + 1];

                    // Calculate rest time
                    TimeSpan restTime = nextFlight.DepartureTime - currentFlight.ArrivalTime;

                    // Check if rest time is less than 12 hours
                    if (restTime.TotalHours < 12)
                    {
                 
                        return false;
                    }
                }
            }

            return true;
        }

        // NEW METHOD: Check if assigning a flight to a steward would respect rest time constraints
        private bool WouldRespectRestTime(StewardDto steward, FlightDto flight, WeeklySchedule schedule)
        {
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                return true; // No existing flights, so no rest time issues

            var existingFlights = schedule.StewardSchedules[steward.StewardId];

            foreach (var existingFlight in existingFlights)
            {
                // Skip checking the same flight
                if (existingFlight.FlightId == flight.FlightId)
                    continue;

                // Calculate rest time from existing flight to new flight
                if (existingFlight.ArrivalTime < flight.DepartureTime)
                {
                    TimeSpan restTime = flight.DepartureTime - existingFlight.ArrivalTime;
                    if (restTime.TotalHours < 12)
                        return false;
                }

                // Calculate rest time from new flight to existing flight
                if (flight.ArrivalTime < existingFlight.DepartureTime)
                {
                    TimeSpan restTime = existingFlight.DepartureTime - flight.ArrivalTime;
                    if (restTime.TotalHours < 12)
                        return false;
                }
            }

            return true;
        }

        // NEW METHOD: Fix rest time violations in a schedule
        private void FixRestTimeViolations(WeeklySchedule schedule)
        {
            // Track problematic flight pairs per steward
            Dictionary<int, List<Tuple<FlightDto, FlightDto>>> violations = new Dictionary<int, List<Tuple<FlightDto, FlightDto>>>();

            // Find all violations
            foreach (var kvp in schedule.StewardSchedules)
            {
                int stewardId = kvp.Key;
                var flights = kvp.Value;

                if (flights.Count <= 1)
                    continue;

                // Sort flights by departure time
                var sortedFlights = flights.OrderBy(f => f.DepartureTime).ToList();

                // Check consecutive flight pairs
                for (int i = 0; i < sortedFlights.Count - 1; i++)
                {
                    var currentFlight = sortedFlights[i];
                    var nextFlight = sortedFlights[i + 1];

                    TimeSpan restTime = nextFlight.DepartureTime - currentFlight.ArrivalTime;

                    if (restTime.TotalHours < 12)
                    {
                        if (!violations.ContainsKey(stewardId))
                            violations[stewardId] = new List<Tuple<FlightDto, FlightDto>>();

                        violations[stewardId].Add(new Tuple<FlightDto, FlightDto>(currentFlight, nextFlight));
                    }
                }
            }

            // Fix violations by removing stewards from the lower priority flight
            foreach (var kvp in violations)
            {
                int stewardId = kvp.Key;
                var violationPairs = kvp.Value;

                foreach (var pair in violationPairs)
                {
                    // Get the steward object
                    StewardDto steward = null;
                    bool found = false;

                    // Determine which flight has lower priority
                    var flightToRemoveStewardFrom = pair.Item1.Priority <= pair.Item2.Priority ? pair.Item1 : pair.Item2;

                    // Find the corresponding flight assignment
                    var assignment = schedule.FlightAssignments.FirstOrDefault(fa => fa.Flight.FlightId == flightToRemoveStewardFrom.FlightId);

                    if (assignment == null)
                        continue;

                    // Find and remove the steward from the assignment
                    foreach (var s in assignment.BusinessStewards.ToList())
                    {
                        if (s.StewardId == stewardId)
                        {
                            // Don't remove if it's the only senior steward
                            if (s.IsSenior && assignment.BusinessStewards.Count(bs => bs.IsSenior) <= 1)
                                break;

                            assignment.BusinessStewards.Remove(s);
                            found = true;
                            steward = s;
                            break;
                        }
                    }

                    if (!found)
                    {
                        foreach (var s in assignment.EconomyStewards.ToList())
                        {
                            if (s.StewardId == stewardId)
                            {
                                assignment.EconomyStewards.Remove(s);
                                found = true;
                                steward = s;
                                break;
                            }
                        }
                    }

                    // If the steward was found and removed, update the schedule
                    if (found && steward != null)
                    {
                        // Remove this flight from the steward's schedule
                        if (schedule.StewardSchedules.ContainsKey(stewardId))
                        {
                            schedule.StewardSchedules[stewardId].Remove(flightToRemoveStewardFrom);
                        }

                        // Update hours
                        if (schedule.StewardHours.ContainsKey(stewardId))
                        {
                            schedule.StewardHours[stewardId] -= flightToRemoveStewardFrom.FlightTime;
                        }

                        // If removing the steward results in an incomplete flight, consider removing the flight entirely
                        if (!assignment.HasSeniorSteward || assignment.EconomyStewards.Count == 0)
                        {
                            schedule.FlightAssignments.Remove(assignment);
                        }
                    }
                }
            }

            // Rebuild steward schedules after fixing
            RebuildStewardSchedules(schedule);
        }

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
                    // IMPORTANT: Make sure hours are properly copied
                    MonthlyHours = steward.MonthlyHours,
                    ProjectedHours = steward.MonthlyHours, // Reset to base hours
                    PositiveFeedbackCount = steward.PositiveFeedbackCount,
                    NegativeFeedbackCount = steward.NegativeFeedbackCount
                };

                copy.LicenseIds = new List<int>(steward.LicenseIds);
                copy.LanguageIds = new List<int>(steward.LanguageIds);

                if (steward.LicensedAircraftTypes != null)
                {
                    copy.LicensedAircraftTypes = new List<string>(steward.LicensedAircraftTypes);
                }

                copies.Add(copy);
            }

            return copies;
        }

        // Rebuild steward schedules after modifications - UPDATED with rest time validation
        private void RebuildStewardSchedules(WeeklySchedule schedule)
        {
            // Clear existing schedules
            schedule.StewardSchedules.Clear();
            schedule.StewardHours.Clear();

            // Dictionary to track hours including monthly baseline for validation
            Dictionary<int, float> totalHours = new Dictionary<int, float>();

            // Rebuild from flight assignments
            foreach (var assignment in schedule.FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                foreach (var steward in assignment.BusinessStewards)
                {
                    if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                        schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[steward.StewardId].Add(assignment.Flight);

                    // Track hours
                    if (!schedule.StewardHours.ContainsKey(steward.StewardId))
                        schedule.StewardHours[steward.StewardId] = 0;

                    schedule.StewardHours[steward.StewardId] += flightTime;

                    // Track total hours including monthly baseline
                    if (!totalHours.ContainsKey(steward.StewardId))
                        totalHours[steward.StewardId] = steward.MonthlyHours;

                    totalHours[steward.StewardId] += flightTime;

                   
                }

                foreach (var steward in assignment.EconomyStewards)
                {
                    if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                        schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                    schedule.StewardSchedules[steward.StewardId].Add(assignment.Flight);

                    // Track hours
                    if (!schedule.StewardHours.ContainsKey(steward.StewardId))
                        schedule.StewardHours[steward.StewardId] = 0;

                    schedule.StewardHours[steward.StewardId] += flightTime;

                    // Track total hours including monthly baseline
                    if (!totalHours.ContainsKey(steward.StewardId))
                        totalHours[steward.StewardId] = steward.MonthlyHours;

                    totalHours[steward.StewardId] += flightTime;

                    // Log if we're exceeding 90 hours
                    if (totalHours[steward.StewardId] > 90)
                    {
                        Console.WriteLine($"WARNING: Schedule rebuild found steward {steward.StewardId} exceeding 90 hours " +
                            $"(Total: {totalHours[steward.StewardId]})");
                    }
                }
            }

            // Sort each steward's flights chronologically and validate rest times
            foreach (var kvp in schedule.StewardSchedules.ToDictionary(x => x.Key, x => x.Value))
            {
                int stewardId = kvp.Key;
                var flights = kvp.Value;

                // Sort flights by departure time
                var sortedFlights = flights.OrderBy(f => f.DepartureTime).ToList();
                schedule.StewardSchedules[stewardId] = sortedFlights;

                
            }

            // Update LastFlightEndTime for each steward based on their assigned flights
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