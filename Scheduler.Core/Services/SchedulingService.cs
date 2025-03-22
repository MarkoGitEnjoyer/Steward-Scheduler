using Scheduler.Core.Algorithms;
using Scheduler.Core.Models;
using Scheduler.Core.Utils;
using Scheduler.Data.Models;
using Scheduler.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Services
{
    public class SchedulingService : ISchedulingService
    {
        private readonly IUnitOfWork _unitOfWork;

        public SchedulingService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        // Generate a weekly schedule
        public async Task<WeeklySchedule> GenerateWeeklyScheduleAsync(DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = weekStart.Date.AddDays(-(int)weekStart.DayOfWeek + 1);
            if (weekStart.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(1);

            var weekEnd = weekStart.AddDays(7);

            // Get all flights for the week
            var flights = await GetFlightsForWeekAsync(weekStart, weekEnd);
            int totalFlights = flights.Count;

            // Get all stewards with their current monthly hours
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);

            // Initialize projected hours to match current monthly hours
            foreach (var steward in stewards)
            {
                steward.InitializeProjectedHours();
            }

            Console.WriteLine($"Generating schedule with {stewards.Count} stewards and {flights.Count} flights.");

            // Run the priority-based scheduler first
            var priorityScheduler = new PriorityBasedScheduler();
            var initialSchedule = priorityScheduler.GenerateSchedule(
                flights,
                stewards,
                weekStart,
                new SchedulingWeights());

            // Verify hour constraints in initial schedule
            initialSchedule.InitializeStewardHours(stewards);
            if (!initialSchedule.VerifyHourConstraints())
            {
             
                RemoveFlightsFromOverworkedStewards(initialSchedule, stewards);
            }

            // Run the genetic scheduler to refine the schedule
            var geneticScheduler = new GeneticScheduler();
            var schedule = geneticScheduler.OptimizeSchedule(flights, stewards, weekStart);
            schedule.TotalFlightCount = totalFlights;

            // Final verification
            schedule.InitializeStewardHours(stewards);
            if (!schedule.VerifyHourConstraints())
            {
                return initialSchedule;
            }

            return schedule;
        }

        // Helper method to fix overworked stewards
        private void RemoveFlightsFromOverworkedStewards(WeeklySchedule schedule, List<StewardDto> stewards)
        {
            // Get all stewards who have more than 90 hours
            var overworkedStewardIds = schedule.StewardHours
                .Where(kv => kv.Value > 90)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var stewardId in overworkedStewardIds)
            {
                var steward = stewards.FirstOrDefault(s => s.StewardId == stewardId);
                if (steward == null) continue;


                // Get flights assigned to this steward
                if (!schedule.StewardSchedules.ContainsKey(stewardId)) continue;

                var assignments = schedule.StewardSchedules[stewardId].ToList();

                // Sort by priority (ascending)
                assignments.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                // Start removing lowest priority flights until hours are under 90
                foreach (var flight in assignments)
                {
                    // Find the assignment for this flight
                    var flightAssignment = schedule.FlightAssignments
                        .FirstOrDefault(fa => fa.Flight.FlightId == flight.FlightId);

                    if (flightAssignment == null) continue;

                    // Remove the steward from this flight
                    schedule.RemoveStewardFromFlight(steward, flightAssignment);

                    // Check if we've reduced hours enough
                    if (schedule.StewardHours[stewardId] <= 90)
                    {
                        break;
                    }
                }
            }
        }

        // Save a generated schedule to the database
        // Save a generated schedule to the database
        // Save a generated schedule to the database
        public async Task<bool> SaveScheduleAsync(WeeklySchedule schedule)
        {
            try
            {
                // STEP 1: Get the month for this scheduling period
                int scheduleMonth = schedule.WeekStart.Month;
                int scheduleYear = schedule.WeekStart.Year;

                Console.WriteLine($"Validating schedule for month: {scheduleYear}-{scheduleMonth}");
                Console.WriteLine($"Schedule contains {schedule.FlightAssignments.Count} flight assignments");

                // STEP 2: Create a dictionary to track additional hours for each steward
                var additionalHours = new Dictionary<int, float>();

                // STEP 3: Calculate total additional hours for each steward
                foreach (var flightAssignment in schedule.FlightAssignments)
                {
                    // Skip flights from different months (shouldn't happen with simplified approach)
                    if (flightAssignment.Flight.DepartureTime.Month != scheduleMonth ||
                        flightAssignment.Flight.DepartureTime.Year != scheduleYear)
                    {
                        Console.WriteLine($"WARNING: Flight {flightAssignment.Flight.FlightId} is in month {flightAssignment.Flight.DepartureTime.Month}, but schedule is for month {scheduleMonth}");
                        continue;
                    }

                    float flightTime = flightAssignment.Flight.FlightTime;

                    // Log flight information
                    Console.WriteLine($"Processing flight {flightAssignment.Flight.FlightId}: {flightAssignment.Flight.FlightTime}h");

                    // Add hours for all stewards assigned to this flight
                    foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
                    {
                        if (!additionalHours.ContainsKey(steward.StewardId))
                        {
                            additionalHours[steward.StewardId] = 0;
                        }

                        additionalHours[steward.StewardId] += flightTime;
                    }

                    // Log assignment counts
                    Console.WriteLine($"  - Business: {flightAssignment.BusinessStewards.Count}, Economy: {flightAssignment.EconomyStewards.Count}");
                }

                // STEP 4: Verify no steward exceeds 90 hours
                Console.WriteLine("Checking individual steward hours against database records...");

                // Sort stewards by ID for easier reading
                var sortedStewardIds = additionalHours.Keys.OrderBy(id => id).ToList();

                foreach (var stewardId in sortedStewardIds)
                {
                    float newHours = additionalHours[stewardId];

                    // Get current hours from database
                    float currentHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(
                        stewardId, scheduleYear, scheduleMonth);

                    float totalHours = currentHours + newHours;

                    Console.WriteLine($"Steward {stewardId}: Current {currentHours}h + New {newHours}h = Total {totalHours}h");

                    // Check if this exceeds 90 hours
                    if (totalHours > 90)
                    {
                        Console.WriteLine($"ERROR: Steward {stewardId} would exceed 90-hour monthly limit with {totalHours} hours. Schedule not saved.");
                        return false; // Don't save this invalid schedule
                    }
                }

                Console.WriteLine("All stewards' hours verified - no violations of 90-hour limit!");

                // STEP 5: Clear any existing assignments for the flights in this schedule
                var flightIds = schedule.FlightAssignments.Select(fa => fa.Flight.FlightId).ToList();
                Console.WriteLine($"Clearing {flightIds.Count} existing flight assignments...");
                var existingAssignments = await _unitOfWork.Assignments.FindAsync(a => flightIds.Contains(a.FlightId));

                Console.WriteLine($"Found {existingAssignments.Count()} existing assignments to remove");
                foreach (var assignment in existingAssignments)
                {
                    await _unitOfWork.Assignments.RemoveAsync(assignment);
                }

                // STEP 6: Create new assignments
                Console.WriteLine("Creating new assignments...");
                int assignmentCount = 0;

                foreach (var flightAssignment in schedule.FlightAssignments)
                {
                    var month = flightAssignment.Flight.DepartureTime.Month;
                    var year = flightAssignment.Flight.DepartureTime.Year;

                    // Add business stewards
                    foreach (var steward in flightAssignment.BusinessStewards)
                    {
                        try
                        {
                            var assignment = new Assignment
                            {
                                StewardId = steward.StewardId,
                                FlightId = flightAssignment.Flight.FlightId
                            };

                            await _unitOfWork.Assignments.AddAsync(assignment);
                            assignmentCount++;

                            // Update the steward's monthly hours
                            await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                                steward.StewardId,
                                year,
                                month,
                                flightAssignment.Flight.FlightTime);

                            // Update last flight end time
                            await _unitOfWork.Stewards.UpdateLastFlightTimeAsync(
                                steward.StewardId,
                                flightAssignment.Flight.ArrivalTime);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating assignment for business steward {steward.StewardId} on flight {flightAssignment.Flight.FlightId}: {ex.Message}");
                            throw; // Re-throw to be caught by outer exception handler
                        }
                    }

                    // Add economy stewards
                    foreach (var steward in flightAssignment.EconomyStewards)
                    {
                        try
                        {
                            var assignment = new Assignment
                            {
                                StewardId = steward.StewardId,
                                FlightId = flightAssignment.Flight.FlightId
                            };

                            await _unitOfWork.Assignments.AddAsync(assignment);
                            assignmentCount++;

                            // Update the steward's monthly hours
                            await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                                steward.StewardId,
                                year,
                                month,
                                flightAssignment.Flight.FlightTime);

                            // Update last flight end time
                            await _unitOfWork.Stewards.UpdateLastFlightTimeAsync(
                                steward.StewardId,
                                flightAssignment.Flight.ArrivalTime);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating assignment for economy steward {steward.StewardId} on flight {flightAssignment.Flight.FlightId}: {ex.Message}");
                            throw; // Re-throw to be caught by outer exception handler
                        }
                    }
                }

                Console.WriteLine($"Created {assignmentCount} new assignments");

                // STEP 7: Save changes
                Console.WriteLine("Saving changes to database...");
                int changes = await _unitOfWork.CompleteAsync();

                Console.WriteLine($"Successfully saved {changes} changes to database.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception with full details
                Console.WriteLine($"ERROR saving schedule: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }

                return false;
            }
        }        // Get a previously saved schedule for a week
        public async Task<WeeklySchedule> GetScheduleForWeekAsync(DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = weekStart.Date.AddDays(-(int)weekStart.DayOfWeek + 1);
            if (weekStart.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(1);

            var weekEnd = weekStart.AddDays(7);

            // Get all flights for the week
            var flights = await GetFlightsForWeekAsync(weekStart, weekEnd);

            // Get all stewards
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);

            // Create a mapping from ID to steward for quick lookup
            var stewardMap = stewards.ToDictionary(s => s.StewardId);

            // Create schedule
            var schedule = new WeeklySchedule
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };

            // Get assignments for flights
            foreach (var flight in flights)
            {
                var flightAssignment = new FlightAssignment { Flight = flight };

                // Get stewards assigned to this flight
                var assignments = await _unitOfWork.Assignments.GetAssignmentsByFlightAsync(flight.FlightId);

                foreach (var assignment in assignments)
                {
                    if (stewardMap.TryGetValue(assignment.StewardId, out var steward))
                    {
                        if (steward.Role == "Business")
                        {
                            flightAssignment.BusinessStewards.Add(steward);
                        }
                        else if (steward.Role == "Economy")
                        {
                            flightAssignment.EconomyStewards.Add(steward);
                        }

                        // Add to steward's schedule
                        if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                            schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

                        schedule.StewardSchedules[steward.StewardId].Add(flight);
                    }
                }

                schedule.FlightAssignments.Add(flightAssignment);
            }

            // Initialize steward hours
            schedule.InitializeStewardHours(stewards);

            // Calculate fitness score
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }

        // Get a steward's schedule for a specific week
        public async Task<List<FlightDto>> GetStewardScheduleAsync(int stewardId, DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = weekStart.Date.AddDays(-(int)weekStart.DayOfWeek + 1);
            if (weekStart.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(1);

            var weekEnd = weekStart.AddDays(7);

            // Get steward assignments
            var assignments = await _unitOfWork.Assignments.GetAssignmentsByStewardAsync(stewardId);

            // Filter to this week and map to flight DTOs
            var flights = assignments
                .Where(a => a.Flight.DepartureTime >= weekStart && a.Flight.DepartureTime < weekEnd)
                .Select(a => MapFlightToDto(a.Flight))
                .OrderBy(f => f.DepartureTime)
                .ToList();

           

            return flights;
        }

        // Helper methods

        private async Task<List<FlightDto>> GetFlightsForWeekAsync(DateTime weekStart, DateTime weekEnd)
        {
            // Get all flights departing within the week
            var flights = await _unitOfWork.Flights.FindAsync(f =>
                f.DepartureTime >= weekStart && f.DepartureTime < weekEnd);

            var flightDtos = new List<FlightDto>();

            foreach (var flight in flights)
            {
                var dto = MapFlightToDto(flight);

                // Get required crew counts
                if (flight.Aircraft != null)
                {
                    dto.RequiredBusinessCrew = flight.Aircraft.BusinessClassCrew;
                    dto.RequiredEconomyCrew = flight.Aircraft.EconomyClassCrew;
                }
                else
                {
                    // Fallback if aircraft data is missing
                    var aircraft = await _unitOfWork.AircraftTypes.GetAircraftTypeByNameAsync(flight.AircraftType);
                    if (aircraft != null)
                    {
                        dto.RequiredBusinessCrew = aircraft.BusinessClassCrew;
                        dto.RequiredEconomyCrew = aircraft.EconomyClassCrew;
                    }
                    else
                    {
                        // Default values if aircraft not found
                        dto.RequiredBusinessCrew = 2;
                        dto.RequiredEconomyCrew = 3;
                    }
                }

                flightDtos.Add(dto);
            }

            return flightDtos;
        }

        private async Task<List<StewardDto>> GetAllStewardsWithDetailsAsync(DateTime weekStart)
        {
            var stewards = await _unitOfWork.Stewards.GetAllAsync();
            var dtos = new List<StewardDto>();

            // Determine which month we need to load (the month of the start date)
            int year = weekStart.Year;
            int month = weekStart.Month;


            foreach (var steward in stewards)
            {
                var dto = new StewardDto
                {
                    StewardId = steward.StewardId,
                    FirstName = steward.FirstName,
                    LastName = steward.LastName,
                    Role = steward.Role.ToString(),
                    IsSenior = steward.IsSenior,
                    JoiningDate = steward.JoiningDate,
                    LastFlightEndTime = steward.LastFlightEndTime,
                };

                // Load hours for the current month only
                float monthlyHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(
                    steward.StewardId, year, month);

                // Set the monthly hours
                dto.MonthlyHours = monthlyHours;


                // Initialize projected hours to match current hours
                dto.InitializeProjectedHours();

                // Get license IDs
                var licenseIds = await _unitOfWork.Stewards.GetStewardLicenseIdsAsync(steward.StewardId);
                dto.LicenseIds = licenseIds.ToList();

                // Get language IDs
                var languageIds = await _unitOfWork.Stewards.GetStewardLanguageIdsAsync(steward.StewardId);
                dto.LanguageIds = languageIds.ToList();

                // Get feedback counts
                var positiveFeedbackCount = await _unitOfWork.Feedbacks.GetPositiveFeedbackCountAsync(steward.StewardId);
                dto.PositiveFeedbackCount = positiveFeedbackCount;
                var negativeFeedbackCount = await _unitOfWork.Feedbacks.GetNegativeFeedbackCountAsync(steward.StewardId);
                dto.NegativeFeedbackCount = negativeFeedbackCount;

                dtos.Add(dto);
            }

            return dtos;
        }
        private FlightDto MapFlightToDto(Flight flight)
        {
            return new FlightDto
            {
                FlightId = flight.FlightId,
                FlightNumber = flight.FlightNumber,
                DepartureTime = flight.DepartureTime,
                ArrivalTime = flight.ArrivalTime,
                AircraftType = flight.AircraftType,
                Destination = flight.Destination,
                RequiredLanguageId = flight.RequiredLanguageId,
                FlightTime = flight.FlightTime,
                Priority = flight.Priority
            };
        }
    }
}