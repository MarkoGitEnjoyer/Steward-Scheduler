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

        // generate initial population using different weight configurations
        public List<WeeklySchedule> GenerateInitialPopulation(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            var population = new List<WeeklySchedule>();

            // generate weight variations for diversity
            var weightVariations = SchedulingWeights.GenerateVariations(_config.PopulationSize);

            // generate diverse schedules with different weights
            population = GenerateDiverseSchedules(population, flights, stewards, weekStart, weightVariations);

            // log fitness scores of initial population
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

            // for each weight variation, create a fresh copy of stewards
            while (population.Count < _config.PopulationSize)
            {
                var weights = weightVariations[index];
                index++;

                // create a completely new copy of stewards for each run
                var freshStewards = DeepCopyStewards(stewards);

                // reset last flight time for each steward so i wont mess with database
                foreach (var steward in freshStewards)
                {
                    steward.LastFlightEndTime = null; 
                }

                // now generate schedule with fresh stewards
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

        // run the genetic algorithm
        public WeeklySchedule OptimizeSchedule(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            // generate initial population
            var population = GenerateInitialPopulation(flights, stewards, weekStart);

            // setup tracking variables
            var bestEver = population.OrderByDescending(s => s.FitnessScore).First().Clone();
            int noImprovementCount = 0;

            // track best solution with the highest flight count
            WeeklySchedule bestWithMostFlights = TrackBestWithMostFlights(population);

            Console.WriteLine($"Starting optimization with {_config.MaxGenerations} generations");

            // evolution loop
            for (int generation = 0; generation < _config.MaxGenerations; generation++)
            {
                // sort by fitness (descending)
                population = population.OrderByDescending(s => s.FitnessScore).ToList();

                var currentBest = population[0];

                // update tracking variables
                bool improved = UpdateBestSolutions(ref bestEver, ref bestWithMostFlights,
                    currentBest, ref noImprovementCount, generation);

                // early termination checks
                bool shouldTerminate = ShouldTerminateEarly(noImprovementCount, generation, population[0]);
                if (shouldTerminate)
                {
                    Console.WriteLine($"Early termination at generation {generation}: No improvement for {noImprovementCount} generations");
                    return SelectBestSolution(population, bestEver, bestWithMostFlights);
                }

                // create new population
                population = CreateNewGeneration(population, stewards, flights, noImprovementCount);

                LogGenerationProgress(generation, improved, population);
            }

            // return the best solution
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

            // check if improved the best solution
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
            // update best solution with most flights if applicable
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
            // early termination if no improvement for many generations
            if (noImprovementCount > 15 && generation > 20)
            {
                return true;
            }

            // check if we've reached desired fitness or have no flights
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

            // keep best schedules
            AddEliteSchedules(population, newPopulation);

            // keep schedule with most flights
            AddScheduleWithMostFlights(population, newPopulation);

            // calculate adaptive mutation rate
            float currentMutationRate = CalculateAdaptiveMutationRate(noImprovementCount);

            // fill the rest with crossover and mutation
            while (newPopulation.Count < _config.PopulationSize)
            {
                // create a new child schedule through selection, crossover, and mutation
                var child = CreateChildSchedule(population, flights, stewards, currentMutationRate);

                // calculate fitness of the new schedule
                child.FitnessScore = FitnessCalculator.CalculateScheduleFitness(child, stewards);

                // add if it's a decent solution
                if (child.FitnessScore > 0.1)
                {
                    newPopulation.Add(child);
                }
            }

            return newPopulation;
        }

        private void AddEliteSchedules(List<WeeklySchedule> population, List<WeeklySchedule> newPopulation)
        {
            // keep the best schedules
            int eliteCount = (int)Math.Max(2, Math.Floor(_config.PopulationSize * _config.ElitismRate));

            // keep the best schedules by fitness
            newPopulation.AddRange(population.Take(eliteCount).Select(s => s.Clone()));

            Console.WriteLine($"Keeping {eliteCount} elite schedules");
        }

        private void AddScheduleWithMostFlights(List<WeeklySchedule> population, List<WeeklySchedule> newPopulation)
        {
            // also keep the solution with the most flights
            var mostFlightsSchedule = population
                .OrderByDescending(s => s.FlightAssignments.Where(fl=>fl.IsComplete()).Count())
                .ThenByDescending(s => s.FitnessScore)
                .First();

            // if still no added yet
            if (!newPopulation.Any(s => s.FlightAssignments.Where(fl => fl.IsComplete()).Count() == mostFlightsSchedule.FlightAssignments.Where(fl => fl.IsComplete()).Count()))
            {
                newPopulation.Add(mostFlightsSchedule.Clone());
            }
        }

        private float CalculateAdaptiveMutationRate(int noImprovementCount)
        {
            // adaptive mutation rate - increase if we're not improving
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
            // pick two parents
            var parent1 = SelectParent(population);
            var parent2 = SelectParent(population);

            // avoid same parent
            bool sameParents = object.ReferenceEquals(parent1, parent2);
            while (sameParents && population.Count > 1)
            {
                parent2 = SelectParent(population);
                sameParents = object.ReferenceEquals(parent1, parent2);
            }

            WeeklySchedule child = ApplyCrossover(parent1, parent2);

            // apply mutation if random says so
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

            // crossover with some chance
            if (_random.NextDouble() < _config.CrossoverRate)
            {
                child = Crossover(parent1, parent2);

                // verify the child is valid
                if (!child.ValidateSchedule())
                {
                    // if invalid, just clone one parent
                    child = _random.NextDouble() < 0.5 ? parent1.Clone() : parent2.Clone();
                }
            }
            else
            {
                // no crossover, just clone one parent
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

            // only use the mutated child if it's valid
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
            // compare the various solutions
            LogFinalSolutionComparison(bestFitness,bestWithMostFlights);

            var bestFitnessFlightsC = bestFitness.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            var bestWithMostFlightsFlightsC = bestWithMostFlights.FlightAssignments.Where(fl => fl.IsComplete()).Count();
            // ftness threshold for comparing solutions
            float fitnessThreshold = 0.95f;

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

        // tournament selection with preference for solutions with more fitness
        private WeeklySchedule SelectParent(List<WeeklySchedule> population)
        {
            // pick tournament candidates
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

        // crossover operator with constraint preservation
        private WeeklySchedule Crossover(WeeklySchedule parent1, WeeklySchedule parent2)
        {
            // create instance of schedule
            var child = WeeklySchedule.InitializeSchedule(parent1.WeekStart);

            // initialize dictionaries 
            child.StewardSchedules = new Dictionary<int, List<FlightDto>>();
            child.StewardHours = new Dictionary<int, float>();

            // get all unique flight IDs from both parents
            var allFlightIds = parent1.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .Union(parent2.FlightAssignments.Select(fa => fa.Flight.FlightId))
                .Distinct()
                .ToList();

            // process all flights
            foreach (var flightId in allFlightIds)
            {
                // getting assignments from parent 1 if there is, else its null
                var parent1Assignment = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var parent2Assignment = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                // select which parent to use for this flight
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

            // process business stewards
            validAssignment = TryAddStewardsToAssignment(
                parentAssignment.BusinessStewards,
                newAssignment.BusinessStewards,
                parentAssignment.Flight,
                child);

            // process economy stewards if business were valid
            if (validAssignment)
            {
                validAssignment = TryAddStewardsToAssignment(
                    parentAssignment.EconomyStewards,
                    newAssignment.EconomyStewards,
                    parentAssignment.Flight,
                    child);
            }

            // only add if assignment has minimum required crew
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

                // add steward to assignment
                targetStewards.Add(steward);

                child.AddFlightToStewardSchedule(steward.StewardId, flight);
            }
            return true;
        }

        private FlightAssignment SelectSourceAssignment(
            WeeklySchedule parent1,
            WeeklySchedule parent2,
            FlightAssignment parent1Assignment,
            FlightAssignment parent2Assignment)
        {
            // determine bias based on fitness scores
            double bias;
            if (parent1.FitnessScore > parent2.FitnessScore)
                bias = 0.7;
            else if (parent2.FitnessScore > parent1.FitnessScore)
                bias = 0.3;
            else
                bias = 0.5;

            bool useParent1 = _random.NextDouble() < bias;

            // try to return the chosen parent's assignment first
            FlightAssignment chosen = useParent1 ? parent1Assignment : parent2Assignment;

            // if chosen parent's assignment is null, try the other parent
            if (chosen == null)
            {
                chosen = useParent1 ? parent2Assignment : parent1Assignment;
            }

            return chosen; // may still be null if both assignments are null
        }


        // mutation operator 
        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights,
                              List<StewardDto> allStewards, float mutationRate = 0.0f)
        {
            if (schedule.FlightAssignments.Count == 0)
                return schedule;

            // apply multiple mutations based on mutationRate
            int mutations = (int)(1 + (mutationRate - 0.3) * (3 / 0.2)); // at least 1, up to 4 mutations

            for (int m = 0; m < mutations; m++)
            {
                ApplySingleMutation(schedule, allFlights, allStewards);
            }

            return schedule;
        }

        private void ApplySingleMutation(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            double randomValue = _random.NextDouble();

            try
            {
                if (randomValue < 0.3) // 30% chance 
                {
                    // swap two stewards between flights
                    MutateByStewardSwap(schedule);
                }
                else if (randomValue < 0.6) // 30% chance 
                {
                    // replace a steward with another qualified one
                    MutateByReplacement(schedule, allStewards);
                }
                else if (randomValue < 0.95) // 35% chance
                {
                    // add a flight that's not currently in the schedule
                    MutateByAddingFlight(schedule, allFlights, allStewards);
                }
                else // 5% chance
                {
                    // remove a flight from the schedule (more dramatic change)
                    MutateByRemovingFlight(schedule);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Mutation error: {ex.Message}");
            }
        }

        // swap stewards between flights with constraint checking
        private void MutateByStewardSwap(WeeklySchedule schedule)
        {
            // can't swap if only 2 flights
            if (schedule.FlightAssignments.Count < 2)
                return;

            // attempt several times to find a valid swap
            for (int attempt = 0; attempt < 10; attempt++)
            {
                // pick two random flights
                int idx1 = _random.Next(schedule.FlightAssignments.Count);
                int idx2 = _random.Next(schedule.FlightAssignments.Count);

                // make sure they're different
                if (idx1 == idx2)
                {
                    // select new index to avoid same flight
                    idx2 = (idx2 + 1) % schedule.FlightAssignments.Count;
                }

                var flight1 = schedule.FlightAssignments[idx1];
                var flight2 = schedule.FlightAssignments[idx2];

                // choose steward type to swap
                bool swapBusiness = _random.NextDouble() < 0.5;

                bool swapSucceeded = AttemptStewardSwap(flight1, flight2, swapBusiness, schedule);
                if (swapSucceeded)
                {
                    return; // successfully swapped
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
                // swap business stewards
                return AttemptBusinessStewardSwap(flight1, flight2, schedule);
            }
            else
            {
                // swap economy stewards
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

            // skip senior stewards if they're the only senior
            if ((steward1.IsSenior && flight1.BusinessStewards.Count(s => s.IsSenior) <= 1) ||
                (steward2.IsSenior && flight2.BusinessStewards.Count(s => s.IsSenior) <= 1))
                return false;

            // check if both stewards can work on the other's flights, the second's parameter of function is flight to ignore in the schedule
            bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
            bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

            if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
            {
                // perform swap
                flight1.BusinessStewards.RemoveAt(steward1Idx);
                flight2.BusinessStewards.RemoveAt(steward2Idx);
                // remove from schedule tracking
                schedule.RemoveFlightFromStewardSchedule(steward1.StewardId, flight1.Flight);
                schedule.RemoveFlightFromStewardSchedule(steward2.StewardId, flight2.Flight);

                flight1.BusinessStewards.Add(steward2);
                flight2.BusinessStewards.Add(steward1);
                // update schedule tracking
                schedule.AddFlightToStewardSchedule(steward2.StewardId, flight1.Flight);
                schedule.AddFlightToStewardSchedule(steward1.StewardId, flight2.Flight);

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

            // check if both stewards can work on the other's flights, the second's parameter of function is flight to ignore in the schedule
            bool canSteward1WorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, flight1.Flight, schedule);
            bool canSteward2WorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, flight2.Flight, schedule);

            if (canSteward1WorkFlight2 && canSteward2WorkFlight1)
            {
                // perform swap
                flight1.EconomyStewards.RemoveAt(steward1Idx);
                flight2.EconomyStewards.RemoveAt(steward2Idx);
                // remove from schedule tracking
                schedule.RemoveFlightFromStewardSchedule(steward1.StewardId, flight1.Flight);
                schedule.RemoveFlightFromStewardSchedule(steward2.StewardId, flight2.Flight);

                flight1.EconomyStewards.Add(steward2);
                flight2.EconomyStewards.Add(steward1);
                // update schedule tracking
                schedule.AddFlightToStewardSchedule(steward2.StewardId, flight1.Flight);
                schedule.AddFlightToStewardSchedule(steward1.StewardId, flight2.Flight);

                return true;
            }

            return false;
        }

        // replace a steward with another qualified one
        private void MutateByReplacement(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return;

            // try several attempts to find a valid replacement
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // pick a random flight
                int flightIdx = _random.Next(schedule.FlightAssignments.Count);
                var flightAssignment = schedule.FlightAssignments[flightIdx];

                // choose whether to replace business or economy steward
                bool replaceBusiness = _random.NextDouble() < 0.5;

                bool replaced = AttemptStewardReplacement(flightAssignment, allStewards, replaceBusiness, schedule);
                if (replaced)
                {
                    return; // successfully replaced
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
            // don't replace senior stewards if there's only one
            var replaceable = flightAssignment.BusinessStewards;

            if (replaceable.Count == 0)
                return false;

            // pick a random steward to replace
            int stewardIdx = _random.Next(replaceable.Count);
            var stewardToReplace = replaceable[stewardIdx];

            // find potential replacements
            var candidates = allStewards
                .Where(s => s.Role == "Business" &&
                       s.StewardId != stewardToReplace.StewardId &&
                       CanStewardWorkFlight(s, flightAssignment.Flight, null, schedule))
                .ToList();

            // if steward being replaced is senior, replacement must also be senior
            if (stewardToReplace.IsSenior&&replaceable.Where(s=>s.IsSenior).Count()==1)
            {
                candidates = candidates.Where(s => s.IsSenior).ToList();
            }

            if (candidates.Any())
            {
                // pick a replacement with preference for stewards with fewer hours
                var replacement = candidates
                    .OrderBy(s => s.MonthlyHours + schedule.GetStewardScheduledHours(s.StewardId))
                    .First();

                // replace in the flight assignment
                flightAssignment.BusinessStewards.Remove(stewardToReplace);
                schedule.RemoveFlightFromStewardSchedule(stewardToReplace.StewardId, flightAssignment.Flight);

                flightAssignment.BusinessStewards.Add(replacement);
                schedule.AddFlightToStewardSchedule(replacement.StewardId, flightAssignment.Flight);

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

            // pick a random economy steward to replace
            int stewardIdx = _random.Next(flightAssignment.EconomyStewards.Count);
            var stewardToReplace = flightAssignment.EconomyStewards[stewardIdx];

            // find potential replacements
            var candidates = allStewards
                .Where(s => s.Role == "Economy" &&
                       s.StewardId != stewardToReplace.StewardId &&
                       CanStewardWorkFlight(s, flightAssignment.Flight, null, schedule))
                .ToList();

            if (candidates.Any())
            {
                // pick a replacement with preference for stewards with fewer hours
                var replacement = candidates
                    .OrderBy(s => s.MonthlyHours + schedule.GetStewardScheduledHours(s.StewardId))
                    .First();

                // replace in the flight assignment
                flightAssignment.EconomyStewards.Remove(stewardToReplace);
                schedule.RemoveFlightFromStewardSchedule(stewardToReplace.StewardId, flightAssignment.Flight);

                flightAssignment.EconomyStewards.Add(replacement);
                schedule.AddFlightToStewardSchedule(replacement.StewardId, flightAssignment.Flight);

                return true;
            }
            return false;
        }

        // add a new flight to the schedule
        private void MutateByAddingFlight(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // find unscheduled flights
            var unscheduledFlights = FindUnscheduledFlights(schedule, allFlights);

            if (!unscheduledFlights.Any())
                return;

            // try each unscheduled flight, starting with highest priority
            foreach (var flight in unscheduledFlights)
            {
                bool added = TryAddFlightToSchedule(flight, schedule, allStewards);
                if (added)
                {
                    return; // successfully added a flight
                }
            }
        }

        private List<FlightDto> FindUnscheduledFlights(WeeklySchedule schedule, List<FlightDto> allFlights)
        {
        
            // find already scheduled flight IDs
            var scheduledFlightIds = schedule.FlightAssignments
                .Select(fa => fa.Flight.FlightId)
                .ToHashSet();

            // return unscheduled flights, prioritizing high-priority ones
            return allFlights
                .Where(f => !scheduledFlightIds.Contains(f.FlightId))
                .OrderByDescending(f => f.Priority) // try high priority flights first
                .ToList();
        }

        private bool TryAddFlightToSchedule(FlightDto flight, WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            var newAssignment = new FlightAssignment { Flight = flight };
            var tempSchedules = new Dictionary<int, List<FlightDto>>(schedule.StewardSchedules);
            var tempHours = new Dictionary<int, float>(schedule.StewardHours);

            // find and assign a senior steward
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

            // assign a senior steward
            var seniorSteward = availableSeniors[0];
            newAssignment.BusinessStewards.Add(seniorSteward);
            UpdateStewardScheduleForFlight(seniorSteward, flight, tempSchedules, tempHours);

            // Assign regular business stewards
            bool assignedBusiness = AssignBusinessStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours);
            if (!assignedBusiness)
            {
                return false;
            }

            // assign economy stewards
            bool assignedEconomy = AssignEconomyStewards(newAssignment, flight, allStewards, schedule, tempSchedules, tempHours);
            if (!assignedEconomy)
            {
                return false;
            }

            // add flight if it meets minimum staffing requirements
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

            // add business stewards
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

            // add economy stewards
            foreach (var steward in availableEconomyStewards.Take(flight.RequiredEconomyCrew))
            {
                newAssignment.EconomyStewards.Add(steward);
                UpdateStewardScheduleForFlight(steward, flight, tempSchedules, tempHours);
            }

            return newAssignment.EconomyStewards.Count >= flight.RequiredEconomyCrew;
        }

        // remove a flight from the schedule - prefer low priority flights
        private void MutateByRemovingFlight(WeeklySchedule schedule)
        {
            if (schedule.FlightAssignments.Count <= 2)
                return;

            // get flights ordered by priority (ascending)
            var candidates = schedule.FlightAssignments
                .OrderBy(fa => fa.Flight.Priority)
                .Take(3) // Consider the 3 lowest priority flights
                .ToList();

            // pick a random flight from the candidates
            int randomIndex = _random.Next(candidates.Count);
            var flightToRemove = candidates[randomIndex];

            // find and remove it
            int flightIndex = schedule.FlightAssignments.IndexOf(flightToRemove);
            if (flightIndex >= 0)
            {
                schedule.FlightAssignments.RemoveAt(flightIndex);
                schedule.CleanupFailedAssignment(flightToRemove);
            }
        }

        #endregion

        #region Helper Methods

        // check if a steward can work a flight,  ignoring a flight they're being swapped from
        private bool CanStewardWorkFlight(
             StewardDto steward,
             FlightDto newFlight,
             FlightDto flightToIgnore,
             WeeklySchedule schedule)
        {
            // check aircraft license
            if (!steward.HasLicenseForAircraft(newFlight.AircraftType))
                return false;

            // check 90-hour constraint
            float currentHours = CalculateHoursWithFlightSwap(steward, newFlight, flightToIgnore, schedule);
            if (currentHours > 90)
                return false;

            // if steward isn't scheduled yet, they're available 
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                return true;

            // check rest time constraints with existing flights 
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
                    // check for conflicts with this flight
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

                // if we're swapping flights, remove the hours from the flight we're ignoring
                if (flightToIgnore != null && schedule.StewardSchedules.ContainsKey(steward.StewardId) &&
                    schedule.StewardSchedules[steward.StewardId].Any(f => f.FlightId == flightToIgnore.FlightId))
                {
                    currentHours -= flightToIgnore.FlightTime;
                }
            }

            // add hours from new flight 
            return currentHours + newFlight.FlightTime;
        }

        // get available stewards for a flight with all constraints checked
        private List<StewardDto> GetAvailableStewards(
            List<StewardDto> allStewards,
            FlightDto flight,
            string role,
            bool requireSenior,
            WeeklySchedule schedule,
            Dictionary<int, List<FlightDto>> tempSchedules,
            Dictionary<int, float> tempHours)
        {
            // use existing temp schedules and hours if provided, otherwise use schedule's
            var stewardSchedules = tempSchedules ?? schedule.StewardSchedules;
            var stewardHours = tempHours ?? schedule.StewardHours;

            return allStewards
                .Where(s =>
                    // role filter
                    s.Role == role &&

                    // senior filter if required
                    (!requireSenior || s.IsSenior) &&

                    // aircraft license
                    s.HasLicenseForAircraft(flight.AircraftType) &&

                    // check rest time with all existing flights
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
        
        #endregion
    }
}