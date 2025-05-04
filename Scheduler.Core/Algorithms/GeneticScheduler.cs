using Scheduler.Core.Models;
using Scheduler.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

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

            // Add base schedule from priority scheduler
            var baseSchedule = GenerateBaseSchedule(flights, stewards, weekStart);
            population.Add(baseSchedule);

            Console.WriteLine($"Added base schedule with {baseSchedule.FlightAssignments.Count} flights to population");

            // Generate diverse schedules with different weights
            population = GenerateDiverseSchedules(population, flights, stewards, weekStart, weightVariations);

            // Log fitness scores of initial population
            LogInitialPopulationFitness(population);

            return population;
        }

        private WeeklySchedule GenerateBaseSchedule(List<FlightDto> flights, List<StewardDto> stewards, DateTime weekStart)
        {
            // First, run priority scheduler once to get a good base schedule
            var priorityScheduler = new PriorityBasedScheduler();
            return priorityScheduler.GenerateSchedule(
                flights.OrderByDescending(f => f.Priority).ToList(),
                DeepCopyStewards(stewards),
                weekStart,
                new SchedulingWeights());
        }

        private List<WeeklySchedule> GenerateDiverseSchedules(
            List<WeeklySchedule> population,
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart,
            List<SchedulingWeights> weightVariations)
        {
            var priorityScheduler = new PriorityBasedScheduler();

            // For each weight variation, create a fresh copy of stewards
            foreach (var weights in weightVariations)
            {
                // IMPORTANT: Create a completely new copy of stewards for each run
                var freshStewards = DeepCopyStewards(stewards);

                // Reset last flight time for each steward
                foreach (var steward in freshStewards)
                {
                    steward.LastFlightEndTime = null; // Reset last flight time
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
            return population;
        }

        private void LogInitialPopulationFitness(List<WeeklySchedule> population)
        {
            Console.WriteLine("Initial population fitness scores:");
            foreach (var schedule in population.OrderByDescending(s => s.FitnessScore))
            {
                Console.WriteLine($"Fitness: {schedule.FitnessScore}, Flights: {schedule.FlightAssignments.Count}");
            }
        }

        // Run the genetic algorithm
        public WeeklySchedule OptimizeSchedule(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            // Generate initial population
            var population = GenerateInitialPopulation(flights, stewards, weekStart);

            // Setup tracking variables
            var bestEver = population.OrderByDescending(s => s.FitnessScore).First().Clone();
            int noImprovementCount = 0;

            // Track best solution with the highest flight count
            WeeklySchedule bestWithMostFlights = TrackBestWithMostFlights(population);

            Console.WriteLine($"Starting optimization with {_config.MaxGenerations} generations");

            // Evolution loop
            for (int generation = 0; generation < _config.MaxGenerations; generation++)
            {
                // Sort by fitness (descending)
                population = population.OrderByDescending(s => s.FitnessScore).ToList();

                var currentBest = population[0];

                // Update tracking variables
                bool improved = UpdateBestSolutions(ref bestEver, ref bestWithMostFlights,
                    currentBest, ref noImprovementCount, generation);

                // Early termination checks
                if (ShouldTerminateEarly(noImprovementCount, generation, population[0]))
                {
                    break;
                }

                // Create new population
                population = CreateNewGeneration(population, stewards, flights, noImprovementCount);

                // Occasional logging
                LogGenerationProgress(generation, improved, population, averageFitness:
                    population.Average(s => s.FitnessScore));
            }

            // Return the best solution
            return SelectBestSolution(population, bestEver, bestWithMostFlights);
        }

        private WeeklySchedule TrackBestWithMostFlights(List<WeeklySchedule> population)
        {
            return population
                .OrderByDescending(s => s.FlightAssignments.Count)
                .ThenByDescending(s => s.FitnessScore)
                .First().Clone();
        }

        private bool UpdateBestSolutions(
            ref WeeklySchedule bestEver,
            ref WeeklySchedule bestWithMostFlights,
            WeeklySchedule currentBest,
            ref int noImprovementCount,
            int generation)
        {
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

            return improved;
        }

        private bool ShouldTerminateEarly(int noImprovementCount, int generation, WeeklySchedule bestSchedule)
        {
            // Early termination if no improvement for many generations
            if (noImprovementCount > 15 && generation > 20)
            {
                Console.WriteLine($"Early termination at generation {generation}: No improvement for {noImprovementCount} generations");
                return true;
            }

            // Check if we've reached desired fitness or have no flights (error condition)
            if (bestSchedule.FitnessScore >= 0.98 || bestSchedule.FlightAssignments.Count == 0)
            {
                Console.WriteLine($"Reached target fitness or error condition at generation {generation}");
                return true;
            }

            return false;
        }

        private List<WeeklySchedule> CreateNewGeneration(
            List<WeeklySchedule> population,
            List<StewardDto> stewards,
            List<FlightDto> flights,
            int noImprovementCount)
        {
            var newPopulation = new List<WeeklySchedule>();

            // Apply elitism - keep best schedules
            AddEliteSchedules(population, newPopulation);

            // Keep schedule with most flights
            AddScheduleWithMostFlights(population, newPopulation);

            // Calculate adaptive mutation rate
            float currentMutationRate = CalculateAdaptiveMutationRate(noImprovementCount);

            // Fill the rest with crossover and mutation
            while (newPopulation.Count < _config.PopulationSize)
            {
                // Create a new child schedule through selection, crossover, and mutation
                var child = CreateChildSchedule(population, flights, stewards, currentMutationRate);

                // Calculate fitness of the new schedule
                child.FitnessScore = FitnessCalculator.CalculateScheduleFitness(child, stewards);

                // Only add if it's a decent solution
                if (child.FitnessScore > 0.1)
                {
                    newPopulation.Add(child);
                }
            }

            // If we lost schedules due to validation, replace them
            ReplaceInvalidSchedules(newPopulation, stewards);

            return newPopulation;
        }

        private void AddEliteSchedules(List<WeeklySchedule> population, List<WeeklySchedule> newPopulation)
        {
            // Elitism: Keep the best schedules
            int eliteCount = (int)Math.Max(2, Math.Floor(_config.PopulationSize * _config.ElitismRate));

            // Keep the best schedules by fitness
            newPopulation.AddRange(population.Take(eliteCount).Select(s => s.Clone()));

            Console.WriteLine($"Keeping {eliteCount} elite schedules");
        }

        private void AddScheduleWithMostFlights(List<WeeklySchedule> population, List<WeeklySchedule> newPopulation)
        {
            // Also explicitly keep the solution with the most flights
            var mostFlightsSchedule = population
                .OrderByDescending(s => s.FlightAssignments.Count)
                .ThenByDescending(s => s.FitnessScore)
                .First();

            if (!newPopulation.Any(s => s.FlightAssignments.Count == mostFlightsSchedule.FlightAssignments.Count))
            {
                newPopulation.Add(mostFlightsSchedule.Clone());
            }
        }

        private float CalculateAdaptiveMutationRate(int noImprovementCount)
        {
            // Adaptive mutation rate - increase if we're not improving
            float currentMutationRate = _config.MutationRate;
            if (noImprovementCount > 5)
            {
                currentMutationRate = Math.Min(0.5f, _config.MutationRate * (1.0f + noImprovementCount * 0.05f));
                Console.WriteLine($"Increasing mutation rate to {currentMutationRate} due to stagnation");
            }
            return currentMutationRate;
        }

        private WeeklySchedule CreateChildSchedule(
            List<WeeklySchedule> population,
            List<FlightDto> flights,
            List<StewardDto> stewards,
            float currentMutationRate)
        {
            // Tournament selection: pick two parents
            var parent1 = SelectParent(population);
            var parent2 = SelectParent(population);

            // Avoid same parent
            while (object.ReferenceEquals(parent1, parent2) && population.Count > 1)
            {
                parent2 = SelectParent(population);
            }

            WeeklySchedule child = ApplyCrossover(parent1, parent2);

            // Apply mutation if needed
            if (_random.NextDouble() < currentMutationRate)
            {
                child = ApplyMutation(child, flights, stewards, currentMutationRate);
            }

            child.TotalFlightCount = parent1.TotalFlightCount;

            return child;
        }

        private WeeklySchedule ApplyCrossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            WeeklySchedule child = null;

            // Crossover
            if (_random.NextDouble() < _config.CrossoverRate)
            {
                child = Crossover(parent1, parent2);

                // Verify the child is valid
                if (!ValidateSchedule(child))
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

            return child;
        }

        private WeeklySchedule ApplyMutation(
            WeeklySchedule child,
            List<FlightDto> flights,
            List<StewardDto> stewards,
            float currentMutationRate)
        {
            var mutatedChild = Mutate(child.Clone(), flights, stewards, currentMutationRate);

            // Only use the mutated child if it's valid
            if (ValidateSchedule(mutatedChild) && mutatedChild.FlightAssignments.Count > 0)
            {
                return mutatedChild;
            }
            return child;
        }

        private void ReplaceInvalidSchedules(List<WeeklySchedule> newPopulation, List<StewardDto> stewards)
        {
            // If we lost schedules due to validation, replace them
            while (newPopulation.Count < _config.PopulationSize)
            {
                // Clone a valid schedule
                var replacement = newPopulation[_random.Next(newPopulation.Count)].Clone();
                replacement.FitnessScore = FitnessCalculator.CalculateScheduleFitness(replacement, stewards);
                newPopulation.Add(replacement);
            }
        }

        private void LogGenerationProgress(int generation, bool improved, List<WeeklySchedule> population, float averageFitness)
        {
            if (generation % 5 == 0 || improved)
            {
                Console.WriteLine($"Gen {generation}: Best={population[0].FitnessScore:F4}, Avg={averageFitness:F4}, Flights={population[0].FlightAssignments.Count}");
            }
        }

        private WeeklySchedule SelectBestSolution(
            List<WeeklySchedule> population,
            WeeklySchedule bestEver,
            WeeklySchedule bestWithMostFlights)
        {
            // Sort by fitness and get the best
            population = population.OrderByDescending(s => s.FitnessScore).ToList();
            var bestFitness = population[0];

            // Get the schedule with the most flights
            var mostFlights = population.OrderByDescending(s => s.FlightAssignments.Count)
                                      .ThenByDescending(s => s.FitnessScore)
                                      .First();

            // Compare the various solutions
            LogFinalSolutionComparison(bestFitness, mostFlights, bestEver, bestWithMostFlights);

            // Fitness threshold for comparing solutions
            float fitnessThreshold = 0.95f;

            // Prefer the solution with more flights if the fitness difference is small
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
            return bestFitness;
        }

        private void LogFinalSolutionComparison(
            WeeklySchedule bestFitness,
            WeeklySchedule mostFlights,
            WeeklySchedule bestEver,
            WeeklySchedule bestWithMostFlights)
        {
            Console.WriteLine($"Best by fitness: Fitness={bestFitness.FitnessScore:F4}, Flights={bestFitness.FlightAssignments.Count}");
            Console.WriteLine($"Best by most flights: Fitness={mostFlights.FitnessScore:F4}, Flights={mostFlights.FlightAssignments.Count}");
            Console.WriteLine($"Best ever found: Fitness={bestEver.FitnessScore:F4}, Flights={bestEver.FlightAssignments.Count}");
            Console.WriteLine($"Best with most flights: Fitness={bestWithMostFlights.FitnessScore:F4}, Flights={bestWithMostFlights.FlightAssignments.Count}");
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

                if (assignment2 != null && AreAssignmentsSimilar(assignment1, assignment2))
                {
                    matchingAssignments++;
                }
            }

            float similarity = (float)matchingAssignments / totalAssignments;
            return similarity >= similarityThreshold;
        }

        private bool AreAssignmentsSimilar(FlightAssignment assignment1, FlightAssignment assignment2)
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

            return businessMatch && economyMatch;
        }

        // Crossover operator with constraint preservation
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            var child = new WeeklySchedule
            {
                WeekStart = parent1.WeekStart,
                WeekEnd = parent1.WeekEnd
            };

            // Create steward schedule tracking for validation during construction
            Dictionary<int, List<FlightDto>> tempStewardSchedules = new Dictionary<int, List<FlightDto>>();
            Dictionary<int, float> stewardHours = new Dictionary<int, float>();

            // Initialize steward hour tracking
            InitializeHourTracking(parent1, parent2, stewardHours);

            // First, add high-priority flights
            AddHighPriorityFlights(parent1, parent2, child, tempStewardSchedules, stewardHours);

            // Handle remaining flights
            AddRemainingFlights(parent1, parent2, child, tempStewardSchedules, stewardHours);

            // Rebuild steward schedules from our temp tracking
            child.StewardSchedules = tempStewardSchedules;
            child.StewardHours = stewardHours;

            return child;
        }

        private void InitializeHourTracking(WeeklySchedule parent1, WeeklySchedule parent2, Dictionary<int, float> stewardHours)
        {
            foreach (var assignment in parent1.FlightAssignments.Concat(parent2.FlightAssignments))
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!stewardHours.ContainsKey(steward.StewardId))
                    {
                        stewardHours[steward.StewardId] = 0;
                    }
                }
            }
        }

        private void AddHighPriorityFlights(
            WeeklySchedule parent1,
            WeeklySchedule parent2,
            WeeklySchedule child,
            Dictionary<int, List<FlightDto>> tempStewardSchedules,
            Dictionary<int, float> stewardHours)
        {
            var highPriorityFlights = new HashSet<int>();

            // Process parent1 high priority flights first
            ProcessHighPriorityFlightsFromParent(parent1, child, tempStewardSchedules, stewardHours, highPriorityFlights);

            // Then add high priority flights from parent2 that aren't already added
            ProcessHighPriorityFlightsFromParent(parent2, child, tempStewardSchedules, stewardHours, highPriorityFlights);
        }

        private void ProcessHighPriorityFlightsFromParent(
            WeeklySchedule parent,
            WeeklySchedule child,
            Dictionary<int, List<FlightDto>> tempStewardSchedules,
            Dictionary<int, float> stewardHours,
            HashSet<int> highPriorityFlights)
        {
            foreach (var assignment in parent.FlightAssignments.OrderByDescending(fa => fa.Flight.Priority))
            {
                if (assignment.Flight.Priority >= 4 && !highPriorityFlights.Contains(assignment.Flight.FlightId))
                {
                    TryAddFlightAssignment(assignment, child, tempStewardSchedules, stewardHours, highPriorityFlights);
                }
            }
        }

        private void TryAddFlightAssignment(
            FlightAssignment parentAssignment,
            WeeklySchedule child,
            Dictionary<int, List<FlightDto>> tempStewardSchedules,
            Dictionary<int, float> stewardHours,
            HashSet<int> processedFlights)
        {
            var newAssignment = new FlightAssignment { Flight = parentAssignment.Flight };
            bool validAssignment = true;

            // Process business stewards
            validAssignment = TryAddStewardsToAssignment(
                parentAssignment.BusinessStewards,
                newAssignment.BusinessStewards,
                parentAssignment.Flight,
                tempStewardSchedules,
                stewardHours);

            // Process economy stewards if business were valid
            if (validAssignment)
            {
                validAssignment = TryAddStewardsToAssignment(
                    parentAssignment.EconomyStewards,
                    newAssignment.EconomyStewards,
                    parentAssignment.Flight,
                    tempStewardSchedules,
                    stewardHours);
            }

            // Only add if assignment has minimum required crew
            if (validAssignment && newAssignment.HasSeniorSteward && newAssignment.EconomyStewards.Any())
            {
                child.FlightAssignments.Add(newAssignment);
                processedFlights.Add(parentAssignment.Flight.FlightId);
            }
        }

        private bool TryAddStewardsToAssignment(
            List<StewardDto> sourceStewards,
            List<StewardDto> targetStewards,
            FlightDto flight,
            Dictionary<int, List<FlightDto>> tempStewardSchedules,
            Dictionary<int, float> stewardHours)
        {
            foreach (var steward in sourceStewards)
            {
                // Skip if adding this steward would exceed 90 hours
                if (!CanAddStewardToFlight(steward, flight, tempStewardSchedules, stewardHours))
                {
                    return false;
                }

                // Add steward to assignment
                targetStewards.Add(steward);

                // Track in our temporary schedule
                if (!tempStewardSchedules.ContainsKey(steward.StewardId))
                    tempStewardSchedules[steward.StewardId] = new List<FlightDto>();

                tempStewardSchedules[steward.StewardId].Add(flight);

                // Update steward hours
                if (!stewardHours.ContainsKey(steward.StewardId))
                    stewardHours[steward.StewardId] = 0;

                stewardHours[steward.StewardId] += flight.FlightTime;
            }
            return true;
        }

        private void AddRemainingFlights(
            WeeklySchedule parent1,
            WeeklySchedule parent2,
            WeeklySchedule child,
            Dictionary<int, List<FlightDto>> tempStewardSchedules,
            Dictionary<int, float> stewardHours)
        {
            // Find all remaining flight IDs from both parents
            var processedFlights = child.FlightAssignments.Select(fa => fa.Flight.FlightId).ToHashSet();

            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .Except(processedFlights)
                .ToList();

            // Handle all remaining flights
            foreach (var flightId in allFlightIds)
            {
                var parent1Assignment = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var parent2Assignment = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // Select which parent to use for this flight
                var sourceAssignment = SelectSourceAssignment(parent1, parent2, parent1Assignment, parent2Assignment);

                if (sourceAssignment != null)
                {
                    TryAddFlightAssignment(sourceAssignment, child, tempStewardSchedules, stewardHours, new HashSet<int>());
                }
            }
        }

        private FlightAssignment SelectSourceAssignment(
            WeeklySchedule parent1,
            WeeklySchedule parent2,
            FlightAssignment parent1Assignment,
            FlightAssignment parent2Assignment)
        {
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

            if (useParent1 && parent1Assignment != null)
            {
                return parent1Assignment;
            }
            else if (!useParent1 && parent2Assignment != null)
            {
                return parent2Assignment;
            }
            else if (parent1Assignment != null)
            {
                return parent1Assignment;
            }
            else if (parent2Assignment != null)
            {
                return parent2Assignment;
            }

            return null;
        }

        // Mutation operator with constraint preservation
        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights,
                              List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // Apply multiple mutations based on mutationRate
            int mutations = 1 + (int)(mutationRate * 3); // At least 1, up to 4 mutations

            for (int m = 0; m < mutations; m++)
            {
                ApplySingleMutation(schedule, allFlights, allStewards);
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(schedule);

            return schedule;
        }

        private void ApplySingleMutation(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Modified mutation probabilities to favor operations that increase flights
            double randomValue = _random.NextDouble();

            try
            {
                if (randomValue < 0.3) // 30% chance 
                {
                    // Swap two stewards between flights
                    MutateByStewardSwap(schedule);
                }
                else if (randomValue < 0.6) // 30% chance 
                {
                    // Replace a steward with another qualified one
                    MutateByReplacement(schedule, allStewards);
                }
                else if (randomValue < 0.95) // 35% chance
                {
                    // Add a flight that's not currently in the schedule
                    MutateByAddingFlight(schedule, allFlights, allStewards);
                }
                else // 5% chance
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

        // Swap stewards between flights with constraint checking
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

                if (AttemptStewardSwap(flight1, flight2, swapBusiness, schedule))
                {
                    return; // Successfully swapped
                }
            }
        }

        private bool AttemptStewardSwap(
            FlightAssignment flight1,
            FlightAssignment flight2,
            bool swapBusiness,
            WeeklySchedule schedule)
        {
            if (swapBusiness)
            {
                // Swap business stewards
                return AttemptBusinessStewardSwap(flight1, flight2, schedule);
            }
            else
            {
                // Swap economy stewards
                return AttemptEconomyStewardSwap(flight1, flight2, schedule);
            }
        }

        private bool AttemptBusinessStewardSwap(
            FlightAssignment flight1,
            FlightAssignment flight2,
            WeeklySchedule schedule)
        {
            if (flight1.BusinessStewards.Count > 0 && flight2.BusinessStewards.Count > 0)
            {
                int steward1Idx = _random.Next(flight1.BusinessStewards.Count);
                int steward2Idx = _random.Next(flight2.BusinessStewards.Count);

                var steward1 = flight1.BusinessStewards[steward1Idx];
                var steward2 = flight2.BusinessStewards[steward2Idx];

                // Skip senior stewards if they're the only senior
                if ((steward1.IsSenior && flight1.BusinessStewards.Count(s => s.IsSenior) <= 1) ||
                    (steward2.IsSenior && flight2.BusinessStewards.Count(s => s.IsSenior) <= 1))
                    return false;

                // Check if both stewards can work on the other's flights
                bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
                bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

                if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
                {
                    // Perform swap
                    flight1.BusinessStewards.RemoveAt(steward1Idx);
                    flight2.BusinessStewards.RemoveAt(steward2Idx);

                    flight1.BusinessStewards.Add(steward2);
                    flight2.BusinessStewards.Add(steward1);

                    return true;
                }
            }
            return false;
        }

        private bool AttemptEconomyStewardSwap(
            FlightAssignment flight1,
            FlightAssignment flight2,
            WeeklySchedule schedule)
        {
            if (flight1.EconomyStewards.Count > 0 && flight2.EconomyStewards.Count > 0)
            {
                int steward1Idx = _random.Next(flight1.EconomyStewards.Count);
                int steward2Idx = _random.Next(flight2.EconomyStewards.Count);

                var steward1 = flight1.EconomyStewards[steward1Idx];
                var steward2 = flight2.EconomyStewards[steward2Idx];

                // Check if stewards can work on the other's flights
                bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
                bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

                if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
                {
                    // Perform swap
                    flight1.EconomyStewards.RemoveAt(steward1Idx);
                    flight2.EconomyStewards.RemoveAt(steward2Idx);

                    flight1.EconomyStewards.Add(steward2);
                    flight2.EconomyStewards.Add(steward1);

                    return true;
                }
            }
            return false;
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

                if (attemptStewardReplacement(flightAssignment, allStewards, replaceBusiness, schedule))
                {
                    return; // Successfully replaced
                }
            }
        }

        private bool attemptStewardReplacement(
            FlightAssignment flightAssignment,
            List<StewardDto> allStewards,
            bool replaceBusiness,
            WeeklySchedule schedule)
        {
            if (replaceBusiness && flightAssignment.BusinessStewards.Count > 0)
            {
                return AttemptBusinessStewardReplacement(flightAssignment, allStewards, schedule);
            }
            else if (!replaceBusiness && flightAssignment.EconomyStewards.Count > 0)
            {
                return AttemptEconomyStewardReplacement(flightAssignment, allStewards, schedule);
            }
            return false;
        }

        private bool AttemptBusinessStewardReplacement(
            FlightAssignment flightAssignment,
            List<StewardDto> allStewards,
            WeeklySchedule schedule)
        {
            // Don't replace senior stewards if there's only one
            var replaceable = flightAssignment.BusinessStewards
                .Where(s => !s.IsSenior || flightAssignment.BusinessStewards.Count(bs => bs.IsSenior) > 1)
                .ToList();

            if (replaceable.Count == 0)
                return false;

            // Pick a random steward to replace
            int stewardIdx = _random.Next(replaceable.Count);
            var stewardToReplace = replaceable[stewardIdx];

            // Find potential replacements
            var candidates = allStewards
                .Where(s => s.Role == "Business" &&
                       s.StewardId != stewardToReplace.StewardId &&
                       CanStewardWorkFlight(s, flightAssignment.Flight, null, schedule))
                .ToList();

            // If steward being replaced is senior, replacement must also be senior
            if (stewardToReplace.IsSenior)
            {
                candidates = candidates.Where(s => s.IsSenior).ToList();
            }

            if (candidates.Any())
            {
                // Pick a replacement with preference for stewards with fewer hours
                var replacement = candidates
                    .OrderBy(s => s.MonthlyHours + schedule.GetStewardScheduledHours(s.StewardId))
                    .First();

                // Replace in the flight assignment
                flightAssignment.BusinessStewards.Remove(stewardToReplace);
                flightAssignment.BusinessStewards.Add(replacement);

                return true;
            }
            return false;
        }

        private bool AttemptEconomyStewardReplacement(
            FlightAssignment flightAssignment,
            List<StewardDto> allStewards,
            WeeklySchedule schedule)
        {
            // Pick a random economy steward to replace
            int stewardIdx = _random.Next(flightAssignment.EconomyStewards.Count);
            var stewardToReplace = flightAssignment.EconomyStewards[stewardIdx];

            // Find potential replacements
            var candidates = allStewards
                .Where(s => s.Role == "Economy" &&
                       s.StewardId != stewardToReplace.StewardId &&
                       CanStewardWorkFlight(s, flightAssignment.Flight, null, schedule))
                .ToList();

            if (candidates.Any())
            {
                // Pick a replacement with preference for stewards with fewer hours
                var replacement = candidates
                    .OrderBy(s => s.MonthlyHours + schedule.GetStewardScheduledHours(s.StewardId))
                    .First();

                // Replace in the flight assignment
                flightAssignment.EconomyStewards.Remove(stewardToReplace);
                flightAssignment.EconomyStewards.Add(replacement);

                return true;
            }
            return false;
        }

        // Add a new flight to the schedule
        private void MutateByAddingFlight(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Find unscheduled flights
            var unscheduledFlights = FindUnscheduledFlights(schedule, allFlights);

            if (!unscheduledFlights.Any())
                return;

            // Try each unscheduled flight, starting with highest priority
            foreach (var flight in unscheduledFlights)
            {
                if (TryAddFlightToSchedule(flight, schedule, allStewards))
                {
                    return; // Successfully added a flight
                }
            }
        }

        private List<FlightDto> FindUnscheduledFlights(WeeklySchedule schedule, List<FlightDto> allFlights)
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

            // Return unscheduled flights, prioritizing high-priority ones
            return weekFlights
                .Where(f => !scheduledFlightIds.Contains(f.FlightId))
                .OrderByDescending(f => f.Priority) // Try high priority flights first
                .ToList();
        }

        private bool TryAddFlightToSchedule(FlightDto flight, WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            var newAssignment = new FlightAssignment { Flight = flight };
            var tempSchedules = new Dictionary<int, List<FlightDto>>(schedule.StewardSchedules);
            var tempHours = new Dictionary<int, float>(schedule.StewardHours);

            // Find and assign a senior steward
            var availableSeniors = GetAvailableStewards(
                allStewards,
                flight,
                "Business",
                true,
                schedule,
                tempSchedules,
                tempHours);

            if (!availableSeniors.Any())
                return false;

            // Assign a senior steward
            var seniorSteward = availableSeniors[_random.Next(availableSeniors.Count)];
            newAssignment.BusinessStewards.Add(seniorSteward);
            UpdateStewardScheduleForFlight(seniorSteward, flight, tempSchedules, tempHours);

            // Assign regular business stewards
            if (!AssignBusinessStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours))
            {
                return false;
            }

            // Assign economy stewards
            if (!AssignEconomyStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours))
            {
                return false;
            }

            // Add flight if it meets minimum staffing requirements
            if (newAssignment.BusinessStewards.Any(s => s.IsSenior) && newAssignment.EconomyStewards.Any())
            {
                schedule.FlightAssignments.Add(newAssignment);
                schedule.StewardSchedules = tempSchedules;
                schedule.StewardHours = tempHours;
                return true;
            }

            return false;
        }

        private void UpdateStewardScheduleForFlight(
            StewardDto steward,
            FlightDto flight,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> tempHours)
        {
            if (!tempSchedules.ContainsKey(steward.StewardId))
                tempSchedules[steward.StewardId] = new List<FlightDto>();
            tempSchedules[steward.StewardId].Add(flight);

            if (!tempHours.ContainsKey(steward.StewardId))
                tempHours[steward.StewardId] = 0;
            tempHours[steward.StewardId] += flight.FlightTime;
        }

        private bool AssignBusinessStewards(
            FlightAssignment newAssignment,
            FlightDto flight,
            List<StewardDto> allStewards,
            WeeklySchedule schedule,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> tempHours)
        {
            int businessNeeded = flight.RequiredBusinessCrew - 1; // -1 for the senior
            if (businessNeeded <= 0)
                return true;

            var availableBusinessStewards = GetAvailableStewards(
                allStewards,
                flight,
                "Business",
                false,
                schedule,
                tempSchedules,
                tempHours);

            // Add business stewards
            foreach (var steward in availableBusinessStewards.Take(businessNeeded))
            {
                newAssignment.BusinessStewards.Add(steward);
                UpdateStewardScheduleForFlight(steward, flight, tempSchedules, tempHours);
            }

            return newAssignment.BusinessStewards.Count >= flight.RequiredBusinessCrew;
        }

        private bool AssignEconomyStewards(
            FlightAssignment newAssignment,
            FlightDto flight,
            List<StewardDto> allStewards,
            WeeklySchedule schedule,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> tempHours)
        {
            var availableEconomyStewards = GetAvailableStewards(
                allStewards,
                flight,
                "Economy",
                false,
                schedule,
                tempSchedules,
                tempHours);

            // Add economy stewards
            foreach (var steward in availableEconomyStewards.Take(flight.RequiredEconomyCrew))
            {
                newAssignment.EconomyStewards.Add(steward);
                UpdateStewardScheduleForFlight(steward, flight, tempSchedules, tempHours);
            }

            return newAssignment.EconomyStewards.Count >= flight.RequiredEconomyCrew;
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

        // Checks if a steward can be added to a flight considering all constraints
        private bool CanAddStewardToFlight(
            StewardDto steward,
            FlightDto flight,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> stewardHours)
        {
            // Check aircraft license
            if (!steward.HasLicenseForAircraft(flight.AircraftType))
                return false;

            // Check 90-hour constraint
            float currentHours = steward.MonthlyHours;
            if (stewardHours.ContainsKey(steward.StewardId))
                currentHours += stewardHours[steward.StewardId];

            if (currentHours + flight.FlightTime > 90)
                return false;

            // No existing flights to check
            if (!tempSchedules.ContainsKey(steward.StewardId))
                return true;

            // Check rest time constraints with existing flights
            foreach (var existingFlight in tempSchedules[steward.StewardId])
            {
                // Skip checking same flight
                if (existingFlight.FlightId == flight.FlightId)
                    continue;

                // Check overlap
                if (StewardDto.DoFlightsOverlap(existingFlight, flight))
                    return false;

                // Check rest time
                if (!StewardDto.HasEnoughRestBetween(existingFlight, flight))
                    return false;
            }

            return true;
        }

        // Check if a steward can work a flight, potentially ignoring a flight they're being swapped from
        private bool CanStewardWorkFlight(
            StewardDto steward,
            FlightDto newFlight,
            FlightDto flightToIgnore,
            WeeklySchedule schedule)
        {
            // Check aircraft license
            if (!steward.HasLicenseForAircraft(newFlight.AircraftType))
                return false;

            // Check 90-hour constraint
            float currentHours = CalculateHoursWithFlightSwap(steward, newFlight, flightToIgnore, schedule);
            if (currentHours > 90)
                return false;

            // If steward isn't scheduled yet, they're available (subject to license & hour checks done above)
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                return true;

            // Check rest time constraints with existing flights
            foreach (var existingFlight in schedule.StewardSchedules[steward.StewardId])
            {
                // Skip the flight we're ignoring (for swap operations)
                if (flightToIgnore != null && existingFlight.FlightId == flightToIgnore.FlightId)
                    continue;

                // Skip checking same flight
                if (existingFlight.FlightId == newFlight.FlightId)
                    continue;

                // Check overlap and rest time
                if (StewardDto.DoFlightsOverlap(existingFlight, newFlight) ||
                    !StewardDto.HasEnoughRestBetween(existingFlight, newFlight))
                {
                    return false;
                }
            }

            return true;
        }

        private float CalculateHoursWithFlightSwap(
            StewardDto steward,
            FlightDto newFlight,
            FlightDto flightToIgnore,
            WeeklySchedule schedule)
        {
            float currentHours = steward.MonthlyHours;

            if (schedule.StewardHours.ContainsKey(steward.StewardId))
            {
                currentHours += schedule.StewardHours[steward.StewardId];

                // If we're swapping flights, remove the hours from the flight we're ignoring
                if (flightToIgnore != null && schedule.StewardSchedules.ContainsKey(steward.StewardId) &&
                    schedule.StewardSchedules[steward.StewardId].Any(f => f.FlightId == flightToIgnore.FlightId))
                {
                    currentHours -= flightToIgnore.FlightTime;
                }
            }

            return currentHours + newFlight.FlightTime;
        }

        // Get available stewards for a flight with all constraints checked
        private List<StewardDto> GetAvailableStewards(
            List<StewardDto> allStewards,
            FlightDto flight,
            string role,
            bool requireSenior,
            WeeklySchedule schedule,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> tempHours)
        {
            // Use existing temp schedules and hours if provided, otherwise use schedule's
            var stewardSchedules = tempSchedules ?? schedule.StewardSchedules;
            var stewardHours = tempHours ?? schedule.StewardHours;

            return allStewards
                .Where(s =>
                    // Role filter
                    s.Role == role &&

                    // Senior filter if required
                    (!requireSenior || s.IsSenior) &&

                    // Aircraft license
                    s.HasLicenseForAircraft(flight.AircraftType) &&

                    // Not already assigned to this flight
                    (!stewardSchedules.ContainsKey(s.StewardId) ||
                     !stewardSchedules[s.StewardId].Any(f => f.FlightId == flight.FlightId)) &&

                    // Check rest time with all existing flights
                    (!stewardSchedules.ContainsKey(s.StewardId) ||
                     stewardSchedules[s.StewardId].All(f =>
                        !StewardDto.DoFlightsOverlap(f, flight) &&
                        StewardDto.HasEnoughRestBetween(f, flight))) &&

                    // 90-hour constraint
                    (s.MonthlyHours +
                     (stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0) +
                     flight.FlightTime <= 90)
                )
                .OrderBy(s => s.MonthlyHours +
                          (stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0))
                .ToList();
        }

        // Validate that a schedule respects all constraints
        private bool ValidateSchedule(WeeklySchedule schedule)
        {
            return !HasOverlappingFlights(schedule) &&
                   ValidateScheduleRestTimes(schedule) &&
                   VerifyHourConstraints(schedule) &&
                   schedule.FlightAssignments.Count > 0;
        }

        // Check hour constraints for all stewards
        private bool VerifyHourConstraints(WeeklySchedule schedule)
        {
            // Create a dictionary to track total hours
            var totalHours = new Dictionary<int, float>();

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
                        return false;
                    }
                }
            }

            return true;
        }

        // Check if there are any overlapping flights in the schedule
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
                        if (StewardDto.DoFlightsOverlap(orderedFlights[i], orderedFlights[j]))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Check if a schedule adheres to rest time constraints
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

        // Rebuild steward schedules after modifications
        private void RebuildStewardSchedules(WeeklySchedule schedule)
        {
            // Clear existing schedules
            schedule.StewardSchedules.Clear();
            schedule.StewardHours.Clear();

            // Rebuild from flight assignments
            foreach (var assignment in schedule.FlightAssignments)
            {
                float flightTime = assignment.Flight.FlightTime;

                // Process business stewards
                UpdateStewardSchedulesForGroup(schedule, assignment.BusinessStewards, assignment.Flight, flightTime);

                // Process economy stewards
                UpdateStewardSchedulesForGroup(schedule, assignment.EconomyStewards, assignment.Flight, flightTime);
            }

            // Sort each steward's flights chronologically
            SortStewardFlights(schedule);

            // Update LastFlightEndTime for each steward
            UpdateStewardsLastFlightTime(schedule);
        }

        private void UpdateStewardSchedulesForGroup(
            WeeklySchedule schedule,
            List<StewardDto> stewards,
            FlightDto flight,
            float flightTime)
        {
            foreach (var steward in stewards)
            {
                if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                    schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                schedule.StewardSchedules[steward.StewardId].Add(flight);

                // Track hours
                if (!schedule.StewardHours.ContainsKey(steward.StewardId))
                    schedule.StewardHours[steward.StewardId] = 0;

                schedule.StewardHours[steward.StewardId] += flightTime;
            }
        }

        private void SortStewardFlights(WeeklySchedule schedule)
        {
            foreach (var kvp in schedule.StewardSchedules.ToDictionary(x => x.Key, x => x.Value))
            {
                int stewardId = kvp.Key;
                var flights = kvp.Value;

                // Sort flights by departure time
                var sortedFlights = flights.OrderBy(f => f.DepartureTime).ToList();
                schedule.StewardSchedules[stewardId] = sortedFlights;
            }
        }

        private void UpdateStewardsLastFlightTime(WeeklySchedule schedule)
        {
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