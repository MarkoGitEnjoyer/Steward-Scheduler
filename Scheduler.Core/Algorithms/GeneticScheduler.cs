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

            // Generate weight variations - more variations for more diversity
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

                population.Add(schedule);

                // If we have enough schedules, stop
                if (population.Count >= _config.PopulationSize)
                    break;
            }

            // Create additional random variations to ensure diversity
            while (population.Count < _config.PopulationSize)
            {
                // Get a random schedule from existing population
                var baseSchedule = population[_random.Next(population.Count)];

                // Create a mutated copy with higher mutation rate for diversity
                var newSchedule = Mutate(baseSchedule.Clone(), flights, stewards, 0.4f);

                // Only add valid schedules
                if (!HasOverlappingFlights(newSchedule) && newSchedule.FlightAssignments.Count > 0)
                {
                    // Calculate fitness
                    newSchedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(newSchedule, stewards);
                    population.Add(newSchedule);
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

                        // Verify the child has no overlapping flights
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
                        if (!HasOverlappingFlights(mutatedChild) && mutatedChild.FlightAssignments.Count > 0)
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

        // Crossover two parent schedules to create a child - improved version
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

            // Improved crossover strategy - take complete flights from one parent,
            // and flights that need improvement from the other parent preferentially

            // Determine which parent is better
            bool parent1IsBetter = parent1.FitnessScore >= parent2.FitnessScore;
            var betterParent = parent1IsBetter ? parent1 : parent2;
            var worseParent = parent1IsBetter ? parent2 : parent1;

            foreach (int flightId in allFlightIds)
            {
                // Skip if already processed as part of a pair
                if (processedFlights.Contains(flightId))
                    continue;

                var betterAssignment = betterParent.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var worseAssignment = worseParent.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // Decide which parent's assignment to use
                bool useBetterParent = true;

                // If both parents have this flight, prefer the better parent, but
                // if the worse parent has a complete assignment and the better doesn't,
                // use the worse parent's assignment
                if (betterAssignment != null && worseAssignment != null)
                {
                    if (!betterAssignment.IsComplete() && worseAssignment.IsComplete())
                    {
                        useBetterParent = false;
                    }
                    else if (betterAssignment.IsComplete() && worseAssignment.IsComplete())
                    {
                        // Both complete, randomly mix crew members
                        useBetterParent = _random.NextDouble() < 0.7; // Slight preference for better parent
                    }
                    else
                    {
                        // Randomly decide, with preference for better parent
                        useBetterParent = _random.NextDouble() < 0.7;
                    }
                }
                else if (betterAssignment == null && worseAssignment != null)
                {
                    useBetterParent = false;
                }
                else if (betterAssignment == null && worseAssignment == null)
                {
                    continue; // Skip if neither parent has this flight
                }

                FlightAssignment selectedAssignment = useBetterParent ? betterAssignment : worseAssignment;
                WeeklySchedule selectedParent = useBetterParent ? betterParent : worseParent;

                // Add this flight's assignment
                if (selectedAssignment != null)
                {
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
            }

            // Mix some crew members between flights for additional diversity - 30% chance
            if (_random.NextDouble() < 0.3)
            {
                // Only attempt this if we have enough flights
                if (child.FlightAssignments.Count >= 2)
                {
                    int tries = Math.Min(5, child.FlightAssignments.Count);

                    for (int i = 0; i < tries; i++)
                    {
                        int idx1 = _random.Next(child.FlightAssignments.Count);
                        int idx2 = _random.Next(child.FlightAssignments.Count);

                        // Ensure different flights
                        if (idx1 != idx2)
                        {
                            var flight1 = child.FlightAssignments[idx1];
                            var flight2 = child.FlightAssignments[idx2];

                            // Try to swap an economy steward
                            if (flight1.EconomyStewards.Count > 0 && flight2.EconomyStewards.Count > 0)
                            {
                                int steward1Idx = _random.Next(flight1.EconomyStewards.Count);
                                int steward2Idx = _random.Next(flight2.EconomyStewards.Count);

                                var steward1 = flight1.EconomyStewards[steward1Idx];
                                var steward2 = flight2.EconomyStewards[steward2Idx];

                                // Swap if both stewards have license for both aircraft
                                if (steward1.HasLicenseForAircraft(flight2.Flight.AircraftType) &&
                                    steward2.HasLicenseForAircraft(flight1.Flight.AircraftType))
                                {
                                    flight1.EconomyStewards.RemoveAt(steward1Idx);
                                    flight2.EconomyStewards.RemoveAt(steward2Idx);

                                    flight1.EconomyStewards.Add(steward2);
                                    flight2.EconomyStewards.Add(steward1);
                                }
                            }
                        }
                    }
                }
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(child);

            return child;
        }

        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            // If schedule has no flights, don't try to mutate
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

            return schedule;
        }

        // New mutation method to remove a flight
        private void MutateByRemovingFlight(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count <= 2)
                return; // Don't remove if we have very few flights

            // Select a random flight to remove, prioritizing incomplete flights
            var incompleteFlights = schedule.FlightAssignments.Where(fa => !fa.IsComplete()).ToList();

            if (incompleteFlights.Count > 0 && _random.NextDouble() < 0.7)
            {
                // 70% chance to remove an incomplete flight
                int idx = _random.Next(incompleteFlights.Count);
                var flightToRemove = incompleteFlights[idx];

                // Find and remove the flight
                schedule.FlightAssignments.Remove(flightToRemove);

                // If it has a return flight, remove that too
                if (flightToRemove.Flight.ReturnFlightId.HasValue)
                {
                    var returnFlightId = flightToRemove.Flight.ReturnFlightId.Value;
                    var returnFlight = schedule.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnFlightId);

                    if (returnFlight != null)
                    {
                        schedule.FlightAssignments.Remove(returnFlight);
                    }
                }
            }
            else if (schedule.FlightAssignments.Count > 0)
            {
                // Otherwise remove a random flight
                int idx = _random.Next(schedule.FlightAssignments.Count);
                var flightToRemove = schedule.FlightAssignments[idx];

                // Find and remove the flight
                schedule.FlightAssignments.Remove(flightToRemove);

                // If it has a return flight, remove that too
                if (flightToRemove.Flight.ReturnFlightId.HasValue)
                {
                    var returnFlightId = flightToRemove.Flight.ReturnFlightId.Value;
                    var returnFlight = schedule.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == returnFlightId);

                    if (returnFlight != null)
                    {
                        schedule.FlightAssignments.Remove(returnFlight);
                    }
                }
            }
        }

        // Swap stewards between flights
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count < 2)
                return;

            // Attempt several times to find a valid swap
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // Pick two random flights
                int idx1 = _random.Next(schedule.FlightAssignments.Count);
                int idx2 = _random.Next(schedule.FlightAssignments.Count);

                // Make sure they're different
                int subAttempts = 0;
                while (idx1 == idx2 && subAttempts < 5)
                {
                    idx2 = _random.Next(schedule.FlightAssignments.Count);
                    subAttempts++;
                }

                if (idx1 == idx2)
                    continue; // Try another main attempt

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

                            // Check if stewards can work the other flights without conflicts
                            bool steward1CanWorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, schedule) &&
                                                         steward1.HasLicenseForAircraft(flight2.Flight.AircraftType);
                            bool steward2CanWorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, schedule) &&
                                                         steward2.HasLicenseForAircraft(flight1.Flight.AircraftType);

                            // If paired flights exist, check those too
                            if (flight1Pair != null)
                            {
                                steward2CanWorkFlight1 = steward2CanWorkFlight1 &&
                                    CanStewardWorkFlight(steward2, flight1Pair.Flight, schedule) &&
                                    steward2.HasLicenseForAircraft(flight1Pair.Flight.AircraftType);
                            }

                            if (flight2Pair != null)
                            {
                                steward1CanWorkFlight2 = steward1CanWorkFlight2 &&
                                    CanStewardWorkFlight(steward1, flight2Pair.Flight, schedule) &&
                                    steward1.HasLicenseForAircraft(flight2Pair.Flight.AircraftType);
                            }

                            // Only perform swap if it doesn't create conflicts
                            if (steward1CanWorkFlight2 && steward2CanWorkFlight1)
                            {
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

                                // Successful swap, exit the attempt loop
                                return;
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

                        // Check if stewards can work the other flights without conflicts
                        bool steward1CanWorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, schedule) &&
                                                     steward1.HasLicenseForAircraft(flight2.Flight.AircraftType);
                        bool steward2CanWorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, schedule) &&
                                                     steward2.HasLicenseForAircraft(flight1.Flight.AircraftType);

                        // If paired flights exist, check those too
                        if (flight1Pair != null)
                        {
                            steward2CanWorkFlight1 = steward2CanWorkFlight1 &&
                                CanStewardWorkFlight(steward2, flight1Pair.Flight, schedule) &&
                                steward2.HasLicenseForAircraft(flight1Pair.Flight.AircraftType);
                        }

                        if (flight2Pair != null)
                        {
                            steward1CanWorkFlight2 = steward1CanWorkFlight2 &&
                                CanStewardWorkFlight(steward1, flight2Pair.Flight, schedule) &&
                                steward1.HasLicenseForAircraft(flight2Pair.Flight.AircraftType);
                        }

                        // Only perform swap if it doesn't create conflicts
                        if (steward1CanWorkFlight2 && steward2CanWorkFlight1)
                        {
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

                            // Successful swap, exit the attempt loop
                            return;
                        }
                    }
                }
            }
        }

        // Replace a steward with another not in the schedule
        private void MutateByReplacement(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return;

            // Try several attempts to find a valid replacement
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // Pick a random flight, with higher chance for incomplete flights
                List<FlightAssignment> candidateFlights = schedule.FlightAssignments;
                var incompleteFlights = schedule.FlightAssignments.Where(fa => !fa.IsComplete()).ToList();

                int flightIdx;

                if (incompleteFlights.Count > 0 && _random.NextDouble() < 0.7)
                {
                    // Higher chance to select an incomplete flight
                    flightIdx = schedule.FlightAssignments.IndexOf(incompleteFlights[_random.Next(incompleteFlights.Count)]);
                }
                else
                {
                    // Random flight
                    flightIdx = _random.Next(schedule.FlightAssignments.Count);
                }

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
                        continue;

                    // Pick a random steward to replace
                    int stewardIdx = _random.Next(nonSeniorStewards.Count);
                    var stewardToReplace = nonSeniorStewards[stewardIdx];

                    // Find a replacement from all business stewards not in this flight
                    // and who have the proper license for this aircraft type
                    var possibleReplacements = allStewards
                        .Where(s => s.Role == "Business" &&
                                  !flightAssignment.BusinessStewards.Any(bs => bs.StewardId == s.StewardId) &&
                                  s.HasLicenseForAircraft(flightAssignment.Flight.AircraftType) &&
                                  !HasScheduleConflict(s, flightAssignment.Flight, pairedAssignment?.Flight, schedule))
                        .ToList();

                    // For senior replacement, we need to ensure the replacement is also senior
                    if (stewardToReplace.IsSenior)
                    {
                        possibleReplacements = possibleReplacements.Where(s => s.IsSenior).ToList();
                    }

                    if (possibleReplacements.Count > 0)
                    {
                        // Find a replacement that can work this flight without conflicts
                        var validReplacements = possibleReplacements
                            .Where(s => CanStewardWorkFlight(s, flightAssignment.Flight, schedule))
                            .ToList();

                        // If there's a paired flight, also check that the steward can work that
                        if (pairedAssignment != null && validReplacements.Count > 0)
                        {
                            validReplacements = validReplacements
                                .Where(s => CanStewardWorkFlight(s, pairedAssignment.Flight, schedule))
                                .ToList();
                        }

                        if (validReplacements.Count > 0)
                        {
                            int replacementIdx = _random.Next(validReplacements.Count);
                            var replacement = validReplacements[replacementIdx];

                            // Replace in the flight assignment
                            flightAssignment.BusinessStewards.Remove(stewardToReplace);
                            flightAssignment.BusinessStewards.Add(replacement);

                            // Replace in paired flight if it exists
                            if (pairedAssignment != null)
                            {
                                pairedAssignment.BusinessStewards.Remove(stewardToReplace);
                                pairedAssignment.BusinessStewards.Add(replacement);
                            }

                            // Successful replacement, exit the attempt loop
                            return;
                        }
                    }
                }
                else if (!replaceBusiness && flightAssignment.EconomyStewards.Count > 0)
                {
                    // Pick a random steward to replace
                    int stewardIdx = _random.Next(flightAssignment.EconomyStewards.Count);
                    var stewardToReplace = flightAssignment.EconomyStewards[stewardIdx];

                    // Find a replacement from all economy stewards not in this flight
                    // and who have the proper license for this aircraft type
                    var possibleReplacements = allStewards
                        .Where(s => s.Role == "Economy" &&
                                  !flightAssignment.EconomyStewards.Any(es => es.StewardId == s.StewardId) &&
                                  s.HasLicenseForAircraft(flightAssignment.Flight.AircraftType) &&
                                  !HasScheduleConflict(s, flightAssignment.Flight, pairedAssignment?.Flight, schedule))
                        .ToList();

                    if (possibleReplacements.Count > 0)
                    {
                        // Find a replacement that can work this flight without conflicts
                        var validReplacements = possibleReplacements
                            .Where(s => CanStewardWorkFlight(s, flightAssignment.Flight, schedule))
                            .ToList();

                        // If there's a paired flight, also check that the steward can work that
                        if (pairedAssignment != null && validReplacements.Count > 0)
                        {
                            validReplacements = validReplacements
                                .Where(s => CanStewardWorkFlight(s, pairedAssignment.Flight, schedule))
                                .ToList();
                        }

                        if (validReplacements.Count > 0)
                        {
                            int replacementIdx = _random.Next(validReplacements.Count);
                            var replacement = validReplacements[replacementIdx];

                            // Replace in the flight assignment
                            flightAssignment.EconomyStewards.Remove(stewardToReplace);
                            flightAssignment.EconomyStewards.Add(replacement);

                            // Replace in paired flight if it exists
                            if (pairedAssignment != null)
                            {
                                pairedAssignment.EconomyStewards.Remove(stewardToReplace);
                                pairedAssignment.EconomyStewards.Add(replacement);
                            }

                            // Successful replacement, exit the attempt loop
                            return;
                        }
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

            // Try several unscheduled flights until we find one we can successfully add
            for (int attempt = 0; attempt < 5 && attempt < unscheduledFlights.Count; attempt++)
            {
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
                                 (returnFlightToAdd?.FlightTime ?? 0) <= 90 &&
                              s.HasLicenseForAircraft(flightToAdd.AircraftType) &&
                              !HasScheduleConflict(s, flightToAdd, returnFlightToAdd, schedule))
                    .ToList();

                if (availableSeniors.Count == 0)
                    continue; // Can't staff this flight without a senior steward

                // Filter seniors who can actually work this flight without conflicts
                var validSeniors = availableSeniors
                    .Where(s => CanStewardWorkFlight(s, flightToAdd, schedule))
                    .ToList();

                // If there's a return flight, also check if the steward can work that
                if (returnFlightToAdd != null && validSeniors.Count > 0)
                {
                    validSeniors = validSeniors
                        .Where(s => CanStewardWorkFlight(s, returnFlightToAdd, schedule))
                        .ToList();
                }

                if (validSeniors.Count == 0)
                    continue;

                // Assign a senior steward
                var seniorSteward = validSeniors[_random.Next(validSeniors.Count)];
                newAssignment.BusinessStewards.Add(seniorSteward);

                if (returnAssignment != null)
                {
                    returnAssignment.BusinessStewards.Add(seniorSteward);
                }

                // Find regular business stewards
                int remainingBusinessNeeded = flightToAdd.RequiredBusinessCrew - 1; // We already added a senior

                List<StewardDto> selectedBusinessStewards = new List<StewardDto>();

                if (remainingBusinessNeeded > 0)
                {
                    var availableBusinessStewards = allStewards
                        .Where(s => s.Role == "Business" && !s.IsSenior &&
                                 stewardHours.GetValueOrDefault(s.StewardId, 0) + flightToAdd.FlightTime +
                                    (returnFlightToAdd?.FlightTime ?? 0) <= 90 &&
                                 s.HasLicenseForAircraft(flightToAdd.AircraftType) &&
                                 !HasScheduleConflict(s, flightToAdd, returnFlightToAdd, schedule))
                        .Take(remainingBusinessNeeded)
                        .ToList();

                    // Filter to those who can actually work these flights without conflicts
                    var validBusinessStewards = availableBusinessStewards
                        .Where(s => CanStewardWorkFlight(s, flightToAdd, schedule))
                        .ToList();

                    // If there's a return flight, also check if the stewards can work that
                    if (returnFlightToAdd != null && validBusinessStewards.Count > 0)
                    {
                        validBusinessStewards = validBusinessStewards
                            .Where(s => CanStewardWorkFlight(s, returnFlightToAdd, schedule))
                            .ToList();
                    }

                    // Get as many as needed, or as many as available
                    selectedBusinessStewards = validBusinessStewards
                        .Take(remainingBusinessNeeded)
                        .ToList();

                    // Add them to the assignment
                    foreach (var steward in selectedBusinessStewards)
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
                List<StewardDto> selectedEconomyStewards = new List<StewardDto>();

                if (economyNeeded > 0)
                {
                    var availableEconomyStewards = allStewards
                        .Where(s => s.Role == "Economy" &&
                                 stewardHours.GetValueOrDefault(s.StewardId, 0) + flightToAdd.FlightTime +
                                    (returnFlightToAdd?.FlightTime ?? 0) <= 90 &&
                                 s.HasLicenseForAircraft(flightToAdd.AircraftType) &&
                                 !HasScheduleConflict(s, flightToAdd, returnFlightToAdd, schedule))
                        .Take(economyNeeded)
                        .ToList();

                    // Filter to those who can actually work these flights without conflicts
                    var validEconomyStewards = availableEconomyStewards
                        .Where(s => CanStewardWorkFlight(s, flightToAdd, schedule))
                        .ToList();

                    // If there's a return flight, also check if the stewards can work that
                    if (returnFlightToAdd != null && validEconomyStewards.Count > 0)
                    {
                        validEconomyStewards = validEconomyStewards
                            .Where(s => CanStewardWorkFlight(s, returnFlightToAdd, schedule))
                            .ToList();
                    }

                    // Get as many as needed, or as many as available
                    selectedEconomyStewards = validEconomyStewards
                        .Take(economyNeeded)
                        .ToList();

                    // Add them to the assignment
                    foreach (var steward in selectedEconomyStewards)
                    {
                        newAssignment.EconomyStewards.Add(steward);

                        if (returnAssignment != null)
                        {
                            returnAssignment.EconomyStewards.Add(steward);
                        }
                    }
                }

                // Only add the flight if we have a senior steward and at least one economy steward
                // (minimum staffing requirements)
                if (newAssignment.BusinessStewards.Any(s => s.IsSenior) &&
                    newAssignment.EconomyStewards.Any())
                {
                    schedule.FlightAssignments.Add(newAssignment);

                    if (returnAssignment != null &&
                        returnAssignment.BusinessStewards.Any(s => s.IsSenior) &&
                        returnAssignment.EconomyStewards.Any())
                    {
                        schedule.FlightAssignments.Add(returnAssignment);
                    }

                    // Successful addition, exit the attempt loop
                    return;
                }
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
        private bool HasScheduleConflict(StewardDto steward, FlightDto flight, FlightDto returnFlight,
                               WeeklySchedule schedule)
        {
            // If steward isn't scheduled yet, they're available (subject to license/rest checks)
            if (!schedule.StewardSchedules.TryGetValue(steward.StewardId, out var existingFlights))
                return false;

            // Check last flight end time for rest requirements
            if (steward.LastFlightEndTime.HasValue)
            {
                TimeSpan restTime = flight.DepartureTime - steward.LastFlightEndTime.Value;
                if (restTime.TotalHours < 12)
                    return true; // Conflict - not enough rest
            }

            // Check for overlap with existing flights
            foreach (var existingFlight in existingFlights)
            {
                // Check for direct time overlap
                if (DoFlightsOverlap(existingFlight, flight))
                    return true; // Conflict - flights overlap

                // Check for sufficient rest between flights
                if (!HasEnoughRestBetween(existingFlight, flight))
                    return true; // Conflict - not enough rest

                // If there's a return flight, also check it
                if (returnFlight != null)
                {
                    if (DoFlightsOverlap(existingFlight, returnFlight))
                        return true; // Conflict with return flight

                    if (!HasEnoughRestBetween(existingFlight, returnFlight))
                        return true; // Not enough rest before return flight
                }
            }

            // If there's a return flight, check if it has sufficient time after the outbound flight
            if (returnFlight != null)
            {
                TimeSpan timeBetweenFlights = returnFlight.DepartureTime - flight.ArrivalTime;
                if (timeBetweenFlights.TotalHours < 0)
                    return true; // Return flight departs before outbound flight arrives

                // Also ensure the flights don't overlap
                if (DoFlightsOverlap(flight, returnFlight))
                    return true;
            }

            return false; // No conflicts found
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
    }
}