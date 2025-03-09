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

                // Check if we've reached desired fitness
                if (population[0].FitnessScore >= 0.95)
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
                .ToList();

            // For each flight, randomly choose assignments from either parent
            foreach (int flightId in allFlightIds)
            {
                var flightAssignment1 = parent1.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                var flightAssignment2 = parent2.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == flightId);

                if (flightAssignment1 == null && flightAssignment2 == null)
                    continue;

                // If one parent doesn't have this flight, use the other
                if (flightAssignment1 == null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(flightAssignment2));
                    continue;
                }

                if (flightAssignment2 == null)
                {
                    child.FlightAssignments.Add(CloneFlightAssignment(flightAssignment1));
                    continue;
                }

                // Randomly choose business and economy crews from either parent
                var newAssignment = new FlightAssignment
                {
                    Flight = flightAssignment1.Flight // Both should have the same flight
                };

                // Randomly choose business crew
                if (_random.NextDouble() < 0.5)
                {
                    newAssignment.BusinessStewards.AddRange(flightAssignment1.BusinessStewards);
                }
                else
                {
                    newAssignment.BusinessStewards.AddRange(flightAssignment2.BusinessStewards);
                }

                // Randomly choose economy crew
                if (_random.NextDouble() < 0.5)
                {
                    newAssignment.EconomyStewards.AddRange(flightAssignment1.EconomyStewards);
                }
                else
                {
                    newAssignment.EconomyStewards.AddRange(flightAssignment2.EconomyStewards);
                }

                child.FlightAssignments.Add(newAssignment);
            }

            // Rebuild steward schedules
            RebuildStewardSchedules(child);

            return child;
        }

        private WeeklySchedule Mutate(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Choose a mutation type
            int mutationType = _random.Next(3);

            // Store original schedule to revert if mutation creates an invalid schedule
            var originalSchedule = schedule.Clone();

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

                // Validate the mutation didn't create an invalid schedule
                if (IsValidSchedule(schedule, allStewards))
                {
                    return schedule;
                }
                else
                {
                    // If invalid, revert to original schedule
                    return originalSchedule;
                }
            }
            catch (Exception)
            {
                // If any error occurs, revert to original schedule
                return originalSchedule;
            }
        }

        // Verification for validity of schedules
        private bool IsValidSchedule(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // Create a dictionary to track monthly hours
            var stewardHours = allStewards.ToDictionary(s => s.StewardId, s => s.MonthlyHours);

            // Add hours from this schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                // Check for multiple senior stewards - invalid state
                if (assignment.BusinessStewards.Count(s => s.IsSenior) > 1)
                {
                    return false;
                }

                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!stewardHours.ContainsKey(steward.StewardId))
                        stewardHours[steward.StewardId] = 0;

                    stewardHours[steward.StewardId] += assignment.Flight.FlightTime;

                    // Check for hours exceeding 90 - invalid state
                    if (stewardHours[steward.StewardId] > 90)
                    {
                        return false;
                    }
                }
            }

            // Check return flight pairing consistency
            var flightPairs = new Dictionary<int, int>();
            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    flightPairs[assignment.Flight.FlightId] = assignment.Flight.ReturnFlightId.Value;
                }
            }

            foreach (var pair in flightPairs)
            {
                var flight1 = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Key);

                var flight2 = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Value);

                if (flight1 != null && flight2 != null)
                {
                    // Check if crews match for paired flights
                    var business1 = flight1.BusinessStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();
                    var business2 = flight2.BusinessStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();

                    var economy1 = flight1.EconomyStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();
                    var economy2 = flight2.EconomyStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();

                    if (!business1.SequenceEqual(business2) || !economy1.SequenceEqual(economy2))
                    {
                        return false;
                    }
                }
            }

            return true;
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
            while (idx1 == idx2)
                idx2 = _random.Next(schedule.FlightAssignments.Count);

            var flight1 = schedule.FlightAssignments[idx1];
            var flight2 = schedule.FlightAssignments[idx2];

            // Choose whether to swap business or economy stewards
            bool swapBusiness = _random.NextDouble() < 0.5;

            if (swapBusiness)
            {
                if (flight1.BusinessStewards.Count > 0 && flight2.BusinessStewards.Count > 0)
                {
                    // Pick random stewards from each flight
                    int stewardIdx1 = _random.Next(flight1.BusinessStewards.Count);
                    int stewardIdx2 = _random.Next(flight2.BusinessStewards.Count);

                    // Swap them
                    var temp = flight1.BusinessStewards[stewardIdx1];
                    flight1.BusinessStewards[stewardIdx1] = flight2.BusinessStewards[stewardIdx2];
                    flight2.BusinessStewards[stewardIdx2] = temp;
                }
            }
            else
            {
                if (flight1.EconomyStewards.Count > 0 && flight2.EconomyStewards.Count > 0)
                {
                    // Pick random stewards from each flight
                    int stewardIdx1 = _random.Next(flight1.EconomyStewards.Count);
                    int stewardIdx2 = _random.Next(flight2.EconomyStewards.Count);

                    // Swap them
                    var temp = flight1.EconomyStewards[stewardIdx1];
                    flight1.EconomyStewards[stewardIdx1] = flight2.EconomyStewards[stewardIdx2];
                    flight2.EconomyStewards[stewardIdx2] = temp;
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
            var flight = schedule.FlightAssignments[flightIdx];

            // Choose whether to replace business or economy steward
            bool replaceBusiness = _random.NextDouble() < 0.5;

            if (replaceBusiness && flight.BusinessStewards.Count > 0)
            {
                // Pick a random steward to replace
                int stewardIdx = _random.Next(flight.BusinessStewards.Count);
                var stewardToReplace = flight.BusinessStewards[stewardIdx];

                // Find a replacement from all business stewards not in this flight
                var replacements = allStewards
                    .Where(s => s.Role == "Business" &&
                           !flight.BusinessStewards.Any(bs => bs.StewardId == s.StewardId))
                    .ToList();

                if (replacements.Count > 0)
                {
                    int replacementIdx = _random.Next(replacements.Count);
                    flight.BusinessStewards[stewardIdx] = replacements[replacementIdx];
                }
            }
            else if (!replaceBusiness && flight.EconomyStewards.Count > 0)
            {
                // Pick a random steward to replace
                int stewardIdx = _random.Next(flight.EconomyStewards.Count);
                var stewardToReplace = flight.EconomyStewards[stewardIdx];

                // Find a replacement from all economy stewards not in this flight
                var replacements = allStewards
                    .Where(s => s.Role == "Economy" &&
                           !flight.EconomyStewards.Any(es => es.StewardId == s.StewardId))
                    .ToList();

                if (replacements.Count > 0)
                {
                    int replacementIdx = _random.Next(replacements.Count);
                    flight.EconomyStewards[stewardIdx] = replacements[replacementIdx];
                }
            }
        }

        // Add a flight that's not currently in the schedule
        private void MutateByAddingFlight(WeeklySchedule schedule, List<FlightDto> allFlights, List<StewardDto> allStewards)
        {
            // Find flights that are in the current week but not in the schedule
            var scheduledFlightIds = schedule.FlightAssignments.Select(fa => fa.Flight.FlightId).ToHashSet();

            var unscheduledFlights = allFlights
                .Where(f => !scheduledFlightIds.Contains(f.FlightId) &&
                       f.DepartureTime >= schedule.WeekStart &&
                       f.DepartureTime < schedule.WeekEnd)
                .ToList();

            if (unscheduledFlights.Count == 0)
                return;

            // Pick a random unscheduled flight
            int flightIdx = _random.Next(unscheduledFlights.Count);
            var flightToAdd = unscheduledFlights[flightIdx];

            // Create a new flight assignment
            var newAssignment = new FlightAssignment { Flight = flightToAdd };

            // Assign business stewards (including at least one senior)
            var businessStewards = allStewards
                .Where(s => s.Role == "Business")
                .OrderByDescending(s => s.IsSenior) // Senior stewards first
                .Take(flightToAdd.RequiredBusinessCrew)
                .ToList();

            if (businessStewards.Any())
                newAssignment.BusinessStewards.AddRange(businessStewards);

            // Assign economy stewards
            var economyStewards = allStewards
                .Where(s => s.Role == "Economy")
                .Take(flightToAdd.RequiredEconomyCrew)
                .ToList();

            if (economyStewards.Any())
                newAssignment.EconomyStewards.AddRange(economyStewards);

            // Add the new assignment
            schedule.FlightAssignments.Add(newAssignment);
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
