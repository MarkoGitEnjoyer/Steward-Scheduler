using Scheduler.Core.Models;
using Scheduler.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scheduler.Core.Algorithms
{
    public class GeneticScheduler
    {
        #region Constructor and Fields

        private readonly Random _random = new Random();
        private readonly GeneticAlgorithmConfig _config;

        public GeneticScheduler(GeneticAlgorithmConfig config = null)
        {
            _config = config ?? new GeneticAlgorithmConfig();
        }

        #endregion

        #region Initial Population Generation

        // Generate initial population using different weight configurations
        public List<WeeklySchedule> GenerateInitialPopulation(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            var population = new List<WeeklySchedule>();

            // Generate weight variations for diversity
            var weightVariations = SchedulingWeights.GenerateVariations(_config.PopulationSize);

            // Generate diverse schedules with different weights
            population = GenerateDiverseSchedules(population, flights, stewards, weekStart, weightVariations);

            // Log fitness scores of initial population
            LogInitialPopulationFitness(population);

            return population;
        }

        private List<WeeklySchedule> GenerateDiverseSchedules(
            List<WeeklySchedule> population,
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart,
            List<SchedulingWeights> weightVariations)
        {
            // creating instance of greedy schedule
            var priorityScheduler = new PriorityBasedScheduler();
            int index = 0;

            // For each weight variation, create a fresh copy of stewards
            while (population.Count < _config.PopulationSize)
            {
                var weights = weightVariations[index];
                index++;

                // IMPORTANT: Create a completely new copy of stewards for each run
                var freshStewards = DeepCopyStewards(stewards);

                // Reset last flight time for each steward so i wont mess with database
                foreach (var steward in freshStewards)
                {
                    steward.LastFlightEndTime = null; 
                }

                // Now generate schedule with fresh stewards
                var schedule = priorityScheduler.GenerateSchedule(
                    flights,
                    freshStewards,
                    weekStart,
                    weights);

                population.Add(schedule);
                
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

        #endregion

        #region Main Optimization Process

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
                bool shouldTerminate = ShouldTerminateEarly(noImprovementCount, generation, population[0]);
                if (shouldTerminate)
                {
                    Console.WriteLine($"Early termination at generation {generation}: No improvement for {noImprovementCount} generations");
                    return SelectBestSolution(population, bestEver, bestWithMostFlights);
                }

                // Create new population
                population = CreateNewGeneration(population, stewards, flights, noImprovementCount);

                // Occasional logging
                LogGenerationProgress(generation, improved, population);
            }

            // Return the best solution
            return SelectBestSolution(population, bestEver, bestWithMostFlights);
        }

        private WeeklySchedule TrackBestWithMostFlights(List<WeeklySchedule> population)
        {
            return population
                .OrderByDescending(s => s.FlightAssignments.Where(fl => fl.IsComplete()).Count())
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

            int curBestFlightAssignments = currentBest.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            int bestWithMostFlightAssignments = bestWithMostFlights.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            // Update best solution with most flights if applicable
            if (curBestFlightAssignments > bestWithMostFlightAssignments ||
                (curBestFlightAssignments == bestWithMostFlightAssignments &&
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
                return true;
            }

            // Check if we've reached desired fitness or have no flights (error condition)
            if (bestSchedule.FitnessScore >= 0.98 || bestSchedule.FlightAssignments.Count == 0)
            {
                return true;
            }

            return false;
        }

        private void LogGenerationProgress(int generation, bool improved, List<WeeklySchedule> population)
        {
            if (generation % 5 == 0 || improved)
            {
                Console.WriteLine($"Gen {generation}: Best={population[0].FitnessScore:F4}, Flights={population[0].FlightAssignments.Count}");
            }
        }

        #endregion

        #region Population Evolution

        private List<WeeklySchedule> CreateNewGeneration(
            List<WeeklySchedule> population,
            List<StewardDto> stewards,
            List<FlightDto> flights,
            int noImprovementCount)
        {
            // creating instance of schedule
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
                .OrderByDescending(s => s.FlightAssignments.Where(fl=>fl.IsComplete()).Count())
                .ThenByDescending(s => s.FitnessScore)
                .First();

            if (!newPopulation.Any(s => s.FlightAssignments.Where(fl => fl.IsComplete()).Count() == mostFlightsSchedule.FlightAssignments.Where(fl => fl.IsComplete()).Count()))
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
            bool sameParents = object.ReferenceEquals(parent1, parent2);
            while (sameParents && population.Count > 1)
            {
                parent2 = SelectParent(population);
                sameParents = object.ReferenceEquals(parent1, parent2);
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
            // creating new child
            WeeklySchedule child = null;

            // Crossover with some chance
            if (_random.NextDouble() < _config.CrossoverRate)
            {
                child = Crossover(parent1, parent2);

                // Verify the child is valid
                if (!child.ValidateSchedule())
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
            if (mutatedChild.ValidateSchedule() && mutatedChild.FlightAssignments.Count > 0)
            {
                return mutatedChild;
            }
            return child;
        }

        private WeeklySchedule SelectBestSolution(
            List<WeeklySchedule> population,
            WeeklySchedule bestFitness,
            WeeklySchedule bestWithMostFlights)
        {
            // Compare the various solutions
            LogFinalSolutionComparison(bestFitness,bestWithMostFlights);

            var bestFitnessFlightsC = bestFitness.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            var bestWithMostFlightsFlightsC = bestWithMostFlights.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            // Fitness threshold for comparing solutions
            float fitnessThreshold = 0.95f;

            // If best ever solution has more flights and similar fitness, use that
            if (bestWithMostFlightsFlightsC > bestFitnessFlightsC &&
                bestFitness.FitnessScore > bestFitness.FitnessScore * fitnessThreshold)
            {
                Console.WriteLine($"Returning best solution ever found: {bestFitness.FitnessScore:F4} ({bestFitness.FlightAssignments.Where(fl => fl.IsComplete()).Count()} flights)");
                return bestFitness;
            }

            // If best with most flights has significantly more flights, use that
            if (bestWithMostFlightsFlightsC > bestFitnessFlightsC * 1.1 &&
                bestWithMostFlights.FitnessScore > bestFitness.FitnessScore * fitnessThreshold)
            {
                Console.WriteLine($"Returning solution with most flights: {bestWithMostFlights.FitnessScore:F4} ({bestWithMostFlightsFlightsC} flights)");
                return bestWithMostFlights;
            }

            Console.WriteLine($"Final best solution: Fitness={bestFitness.FitnessScore:F4}, Flights={bestFitnessFlightsC}");
            return bestFitness;
        }

        private void LogFinalSolutionComparison(
            WeeklySchedule bestFitness,
            WeeklySchedule bestWithMostFlights)
        {
            Console.WriteLine($"Best by fitness: Fitness={bestFitness.FitnessScore:F4}, Flights={bestFitness.FlightAssignments.Where(fl=>fl.IsComplete()).Count()}");
            Console.WriteLine($"Best with most flights: Fitness={bestWithMostFlights.FitnessScore:F4}, Flights={bestWithMostFlights.FlightAssignments.Where(fl => fl.IsComplete()).Count()}");
        }

        #endregion

        #region Genetic Operations

        // Tournament selection with preference for solutions with more fitness
        private WeeklySchedule SelectParent(List<WeeklySchedule> population)
        {
            // Pick tournament candidates (larger tournament size = more selection pressure)
            int tournamentSize = Math.Min(3, population.Count);
            var candidates = new List<WeeklySchedule>();

            for (int i = 0; i < tournamentSize; i++)
            {
                int idx = _random.Next(population.Count);
                if (!candidates.Any(candidate => object.ReferenceEquals(candidate, population[idx])))
                {
                    candidates.Add(population[idx]);
                }
            }

            return candidates.OrderByDescending(s => s.FitnessScore).First();
        }

        // Crossover operator with constraint preservation
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            // create instance of schedule
            var child = WeeklySchedule.InitializeSchedule(parent1.WeekStart);

            // Initialize dictionaries directly in the child object 
            child.StewardSchedules = new Dictionary<int, List<FlightDto>>();
            child.StewardHours = new Dictionary<int, float>();

            // Get all unique flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .ToList();

            // Process all flights
            foreach (var flightId in allFlightIds)
            {
                // getting assignments from parent 1 if there is, else its null
                var parent1Assignment = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var parent2Assignment = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // Select which parent to use for this flight
                var sourceAssignment = SelectSourceAssignment(parent1, parent2, parent1Assignment, parent2Assignment);

                if (sourceAssignment != null)
                {
                    TryAddFlightAssignment(sourceAssignment, child);
                }
            }

            return child;
        }

        private bool TryAddFlightAssignment(FlightAssignment parentAssignment, WeeklySchedule child)
        {
            var newAssignment = new FlightAssignment { Flight = parentAssignment.Flight };
            bool validAssignment = true;

            // Process business stewards
            validAssignment = TryAddStewardsToAssignment(
                parentAssignment.BusinessStewards,
                newAssignment.BusinessStewards,
                parentAssignment.Flight,
                child);

            // Process economy stewards if business were valid
            if (validAssignment)
            {
                validAssignment = TryAddStewardsToAssignment(
                    parentAssignment.EconomyStewards,
                    newAssignment.EconomyStewards,
                    parentAssignment.Flight,
                    child);
            }

            // Only add if assignment has minimum required crew
            if (validAssignment && newAssignment.IsComplete())
            {
                child.FlightAssignments.Add(newAssignment);
                return true;
            }

            return false;
        }

        private bool TryAddStewardsToAssignment(
            List<StewardDto> sourceStewards,
            List<StewardDto> targetStewards,
            FlightDto flight,
            WeeklySchedule child)
        {
            foreach (var steward in sourceStewards)
            {
                if (!steward.IsAvailableForFlight(flight, child))
                {
                    return false;
                }

                // Add steward to assignment
                targetStewards.Add(steward);

                // Track in child's schedule
                if (!child.StewardSchedules.ContainsKey(steward.StewardId))
                    child.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                child.StewardSchedules[steward.StewardId].Add(flight);

                // Update steward hours directly in child
                if (!child.StewardHours.ContainsKey(steward.StewardId))
                    child.StewardHours[steward.StewardId] = 0;

                child.StewardHours[steward.StewardId] += flight.FlightTime;
            }
            return true;
        }

        private FlightAssignment SelectSourceAssignment(
            WeeklySchedule parent1,
            WeeklySchedule parent2,
            FlightAssignment parent1Assignment,
            FlightAssignment parent2Assignment)
        {
            // Determine bias based on fitness scores
            double bias;
            if (parent1.FitnessScore > parent2.FitnessScore)
                bias = 0.7;
            else if (parent2.FitnessScore > parent1.FitnessScore)
                bias = 0.3;
            else
                bias = 0.5;

            bool useParent1 = _random.NextDouble() < bias;

            // Try to return the chosen parent's assignment first
            FlightAssignment chosen = useParent1 ? parent1Assignment : parent2Assignment;

            // If chosen parent's assignment is null, try the other parent
            if (chosen == null)
            {
                chosen = useParent1 ? parent2Assignment : parent1Assignment;
            }

            return chosen; // May still be null if both assignments are null
        }


        // Mutation operator with constraint preservation
        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights,
                              List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // Apply multiple mutations based on mutationRate
            int mutations = (int)(1 + (mutationRate - 0.3) * (3 / 0.2)); // At least 1, up to 4 mutations

            for (int m = 0; m < mutations; m++)
            {
                ApplySingleMutation(schedule, allFlights, allStewards);
            }

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
                // Log the error 
                Console.WriteLine($"Mutation error: {ex.Message}");
            }
        }

        // Swap stewards between flights with constraint checking
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            // can't swap if only 2 flights
            if (schedule.FlightAssignments.Count < 2)
                return;

            // Attempt several times to find a valid swap
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // Pick two random flights
                int idx1 = _random.Next(schedule.FlightAssignments.Count);
                int idx2 = _random.Next(schedule.FlightAssignments.Count);

                // Make sure they're different
                if (idx1 == idx2)
                {
                    // Select new index to avoid same flight
                    idx2 = (idx2 + 1) % schedule.FlightAssignments.Count;
                }

                var flight1 = schedule.FlightAssignments[idx1];
                var flight2 = schedule.FlightAssignments[idx2];

                // Choose steward type to swap
                bool swapBusiness = _random.NextDouble() < 0.5;

                bool swapSucceeded = AttemptStewardSwap(flight1, flight2, swapBusiness, schedule);
                if (swapSucceeded)
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
            if (flight1.BusinessStewards.Count == 0 || flight2.BusinessStewards.Count == 0)
                return false;

            // taking ids randomly from 2 flights
            int steward1Idx = _random.Next(flight1.BusinessStewards.Count);
            int steward2Idx = _random.Next(flight2.BusinessStewards.Count);

            // getting stewards from ids
            var steward1 = flight1.BusinessStewards[steward1Idx];
            var steward2 = flight2.BusinessStewards[steward2Idx];

            // Skip senior stewards if they're the only senior
            if ((steward1.IsSenior && flight1.BusinessStewards.Count(s => s.IsSenior) <= 1) ||
                (steward2.IsSenior && flight2.BusinessStewards.Count(s => s.IsSenior) <= 1))
                return false;

            // Check if both stewards can work on the other's flights, the second's parameter of function is flight to ignore in the schedule
            bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
            bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

            if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
            {
                // Perform swap
                flight1.BusinessStewards.RemoveAt(steward1Idx);
                flight2.BusinessStewards.RemoveAt(steward2Idx);
                // Remove from schedule tracking
                RemoveFlightFromStewardSchedule(schedule, steward1.StewardId, flight1.Flight);
                RemoveFlightFromStewardSchedule(schedule, steward2.StewardId, flight2.Flight);

                flight1.BusinessStewards.Add(steward2);
                flight2.BusinessStewards.Add(steward1);
                // Update schedule tracking
                AddFlightToStewardSchedule(schedule, steward2.StewardId, flight1.Flight);
                AddFlightToStewardSchedule(schedule, steward1.StewardId, flight2.Flight);

                return true;
            }

            return false;
        }

        private bool AttemptEconomyStewardSwap(
            FlightAssignment flight1,
            FlightAssignment flight2,
            WeeklySchedule schedule)
        {
            if (flight1.EconomyStewards.Count == 0 || flight2.EconomyStewards.Count == 0)
                return false;

            // getting ids of stewards randomly
            int steward1Idx = _random.Next(flight1.EconomyStewards.Count);
            int steward2Idx = _random.Next(flight2.EconomyStewards.Count);

            // taking stewards from ids
            var steward1 = flight1.EconomyStewards[steward1Idx];
            var steward2 = flight2.EconomyStewards[steward2Idx];

            // Check if both stewards can work on the other's flights, the second's parameter of function is flight to ignore in the schedule
            bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
            bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

            if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
            {
                // Perform swap
                flight1.EconomyStewards.RemoveAt(steward1Idx);
                flight2.EconomyStewards.RemoveAt(steward2Idx);
                // Remove from schedule tracking
                RemoveFlightFromStewardSchedule(schedule, steward1.StewardId, flight1.Flight);
                RemoveFlightFromStewardSchedule(schedule, steward2.StewardId, flight2.Flight);

                flight1.EconomyStewards.Add(steward2);
                flight2.EconomyStewards.Add(steward1);
                // Update schedule tracking
                AddFlightToStewardSchedule(schedule, steward2.StewardId, flight1.Flight);
                AddFlightToStewardSchedule(schedule, steward1.StewardId, flight2.Flight);

                return true;
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

                bool replaced = AttemptStewardReplacement(flightAssignment, allStewards, replaceBusiness, schedule);
                if (replaced)
                {
                    return; // Successfully replaced
                }
            }
        }

        private bool AttemptStewardReplacement(
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
            var replaceable = flightAssignment.BusinessStewards;

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
            if (stewardToReplace.IsSenior&&replaceable.Where(s=>s.IsSenior).Count()==1)
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
                RemoveFlightFromStewardSchedule(schedule, stewardToReplace.StewardId, flightAssignment.Flight);

                flightAssignment.BusinessStewards.Add(replacement);
                AddFlightToStewardSchedule(schedule, replacement.StewardId, flightAssignment.Flight);

                return true;
            }
            return false;
        }

        private bool AttemptEconomyStewardReplacement(
            FlightAssignment flightAssignment,
            List<StewardDto> allStewards,
            WeeklySchedule schedule)
        {
            if (flightAssignment.EconomyStewards.Count == 0)
                return false;

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
                RemoveFlightFromStewardSchedule(schedule, stewardToReplace.StewardId, flightAssignment.Flight);

                flightAssignment.EconomyStewards.Add(replacement);
                AddFlightToStewardSchedule(schedule, replacement.StewardId, flightAssignment.Flight);

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
                bool added = TryAddFlightToSchedule(flight, schedule, allStewards);
                if (added)
                {
                    return; // Successfully added a flight
                }
            }
        }

        private List<FlightDto> FindUnscheduledFlights(WeeklySchedule schedule, List<FlightDto> allFlights)
        {
        
            // Find already scheduled flight IDs
            var scheduledFlightIds = schedule.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .ToHashSet();

            // Return unscheduled flights, prioritizing high-priority ones
            return allFlights
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
            var seniorSteward = availableSeniors[0];
            newAssignment.BusinessStewards.Add(seniorSteward);
            UpdateStewardScheduleForFlight(seniorSteward, flight, tempSchedules, tempHours);

            // Assign regular business stewards
            bool assignedBusiness = AssignBusinessStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours);
            if (!assignedBusiness)
            {
                return false;
            }

            // Assign economy stewards
            bool assignedEconomy = AssignEconomyStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours);
            if (!assignedEconomy)
            {
                return false;
            }

            // Add flight if it meets minimum staffing requirements
            if (newAssignment.IsComplete())
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

            // Check rest time constraints with existing flights using a loop and flag approach
            bool hasConflict = false;
            int i = 0;
            while (i < schedule.StewardSchedules[steward.StewardId].Count && !hasConflict)
            {
                // getting flight from id
                var existingFlight = schedule.StewardSchedules[steward.StewardId][i];
                // ignore if its flight to ignore or a new flight
                bool shouldIgnoreFlight =
                    (flightToIgnore != null && existingFlight.FlightId == flightToIgnore.FlightId) ||
                    (existingFlight.FlightId == newFlight.FlightId);

                if (!shouldIgnoreFlight)
                {
                    // Check for conflicts with this flight
                    hasConflict = StewardDto.DoFlightsOverlap(existingFlight, newFlight) ||
                                 !StewardDto.HasEnoughRestBetween(existingFlight, newFlight);
                }
                i++;
            }

            return !hasConflict;
        }

        private float CalculateHoursWithFlightSwap(
            StewardDto steward,
            FlightDto newFlight,
            FlightDto flightToIgnore,
            WeeklySchedule schedule)
        {
            // hours from db
            float currentHours = steward.MonthlyHours;

            if (schedule.StewardHours.ContainsKey(steward.StewardId))
            {
                // adding hours from schedule
                currentHours += schedule.StewardHours[steward.StewardId];

                // If we're swapping flights, remove the hours from the flight we're ignoring
                if (flightToIgnore != null && schedule.StewardSchedules.ContainsKey(steward.StewardId) &&
                    schedule.StewardSchedules[steward.StewardId].Any(f => f.FlightId == flightToIgnore.FlightId))
                {
                    currentHours -= flightToIgnore.FlightTime;
                }
            }

            // add hours from new flight 
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

                    // Check rest time with all existing flights
                    (!stewardSchedules.ContainsKey(s.StewardId) ||
                     stewardSchedules[s.StewardId].All(f =>
                        !StewardDto.DoFlightsOverlap(f, flight) && // check overlapping with flight
                        StewardDto.HasEnoughRestBetween(f, flight))) && // check rest between flight

                    // 90-hour constraint
                    (s.MonthlyHours +
                     (stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0) +
                     flight.FlightTime <= 90)
                )
                .OrderBy(s => s.MonthlyHours +
                          (stewardHours.ContainsKey(s.StewardId) ? stewardHours[s.StewardId] : 0))
                .ToList();
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
        // Helper method to add flight to steward's schedule
        private void AddFlightToStewardSchedule(WeeklySchedule schedule, int stewardId, FlightDto flight)
        {
            if (!schedule.StewardSchedules.ContainsKey(stewardId))
                schedule.StewardSchedules[stewardId] = new List<FlightDto>();

            schedule.StewardSchedules[stewardId].Add(flight);

            // Update hours
            if (!schedule.StewardHours.ContainsKey(stewardId))
                schedule.StewardHours[stewardId] = 0;

            schedule.StewardHours[stewardId] += flight.FlightTime;
        }

        // Helper method to remove flight from steward's schedule
        private void RemoveFlightFromStewardSchedule(WeeklySchedule schedule, int stewardId, FlightDto flight)
        {
            if (schedule.StewardSchedules.ContainsKey(stewardId))
            {
                // get flights from steward schedule
                var flights = schedule.StewardSchedules[stewardId];
                // flight we need to remove
                var flightToRemove = flights.FirstOrDefault(f => f.FlightId == flight.FlightId);

                if (flightToRemove != null)
                {
                    flights.Remove(flightToRemove);

                    // Update hours
                    if (schedule.StewardHours.ContainsKey(stewardId))
                    {
                        schedule.StewardHours[stewardId] -= flight.FlightTime;
                        if (schedule.StewardHours[stewardId] < 0)
                            schedule.StewardHours[stewardId] = 0;
                    }
                }
            }
        }
        #endregion
    }
}