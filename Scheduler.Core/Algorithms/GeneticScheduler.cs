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

                // Only add valid schedules without overlapping flights
                if (!HasOverlappingFlights(newSchedule))
                {
                    population.Add(newSchedule);
                }
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
                    if (_random.NextDouble() < _config.MutationRate)
                    {
                        var mutatedChild = Mutate(child.Clone(), flights, stewards);

                        // Only use the mutated child if it's valid
                        if (!HasOverlappingFlights(mutatedChild))
                        {
                            child = mutatedChild;
                        }
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
                            bool steward1CanWorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, schedule);
                            bool steward2CanWorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, schedule);

                            // If paired flights exist, check those too
                            if (flight1Pair != null)
                            {
                                steward2CanWorkFlight1 = steward2CanWorkFlight1 &&
                                    CanStewardWorkFlight(steward2, flight1Pair.Flight, schedule);
                            }

                            if (flight2Pair != null)
                            {
                                steward1CanWorkFlight2 = steward1CanWorkFlight2 &&
                                    CanStewardWorkFlight(steward1, flight2Pair.Flight, schedule);
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
                        bool steward1CanWorkFlight2 = CanStewardWorkFlight(steward1, flight2.Flight, schedule);
                        bool steward2CanWorkFlight1 = CanStewardWorkFlight(steward2, flight1.Flight, schedule);

                        // If paired flights exist, check those too
                        if (flight1Pair != null)
                        {
                            steward2CanWorkFlight1 = steward2CanWorkFlight1 &&
                                CanStewardWorkFlight(steward2, flight1Pair.Flight, schedule);
                        }

                        if (flight2Pair != null)
                        {
                            steward1CanWorkFlight2 = steward1CanWorkFlight2 &&
                                CanStewardWorkFlight(steward1, flight2Pair.Flight, schedule);
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

                copies.Add(copy);
            }

            return copies;
        }
    }
}