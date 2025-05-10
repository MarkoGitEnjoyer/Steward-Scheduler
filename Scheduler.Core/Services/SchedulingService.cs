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
            weekStart = NormalizeToWeekStart(weekStart);
            var weekEnd = weekStart.AddDays(7);

            // Get all flights for the week
            var flights = await GetFlightsForWeekAsync(weekStart, weekEnd);
            int totalFlights = flights.Count;

            // Get all stewards with their current monthly hours
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);

            Console.WriteLine($"Generating schedule with {stewards.Count} stewards and {flights.Count} flights.");

            // Generate schedule using genetic algorithm directly
            var optimizedSchedule = GenerateScheduleWithGA(flights, stewards, weekStart);

            // Report final stats
            Console.WriteLine($"Final schedule: {optimizedSchedule.FlightAssignments.Where(fl => fl.IsComplete()).Count()} flights scheduled");

            return optimizedSchedule;
        }

        private DateTime NormalizeToWeekStart(DateTime date)
        {
            // Normalize to the start of the week (Monday)
            date = date.Date.AddDays(-(int)date.DayOfWeek + 1);
            if (date.DayOfWeek == DayOfWeek.Sunday)
                date = date.AddDays(1);

            return date;
        }

        private WeeklySchedule GenerateScheduleWithGA(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            // Run the genetic scheduler directly
            var geneticConfig = new GeneticAlgorithmConfig();
            var geneticScheduler = new GeneticScheduler(geneticConfig);
            return geneticScheduler.OptimizeSchedule(flights, stewards, weekStart);
        }

        // Save a generated schedule to the database
        public async Task<bool> SaveScheduleAsync(WeeklySchedule schedule)
        {
            try
            {
                // Get the month for this scheduling period
                int scheduleMonth = schedule.WeekStart.Month;
                int scheduleYear = schedule.WeekStart.Year;

                Console.WriteLine($"Validating schedule for month: {scheduleYear}-{scheduleMonth}");
                Console.WriteLine($"Schedule contains {schedule.FlightAssignments.Where(fl=>fl.IsComplete()).Count()} flight assignments");

                // Track additional hours for each steward
                var additionalHours = CalculateAdditionalHours(schedule, scheduleMonth, scheduleYear);

                // Clear existing assignments and create new ones
                await ClearAndCreateNewAssignments(schedule);

                // Save all changes
                int changes = await _unitOfWork.CompleteAsync();
                Console.WriteLine($"Successfully saved {changes} changes to database.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception
                LogException(ex);
                return false;
            }
        }

        private Dictionary<int, float> CalculateAdditionalHours(
            WeeklySchedule schedule,
            int scheduleMonth,
            int scheduleYear)
        {
            var additionalHours = new Dictionary<int, float>();

            foreach (var flightAssignment in schedule.FlightAssignments)
            {
                // Track flight hours for assigned stewards
                TrackFlightHoursForStewards(flightAssignment, additionalHours);
            }

            return additionalHours;
        }

        private void TrackFlightHoursForStewards(
            FlightAssignment flightAssignment,
            Dictionary<int, float> additionalHours)
        {
            float flightTime = flightAssignment.Flight.FlightTime;

            // Log flight information
            Console.WriteLine($"Processing flight {flightAssignment.Flight.FlightId}: {flightAssignment.Flight.FlightTime}h");

            // Add hours for all assigned stewards
            foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
            {
                if (!additionalHours.ContainsKey(steward.StewardId))
                {
                    additionalHours[steward.StewardId] = 0;
                }

                additionalHours[steward.StewardId] += flightTime;
            }

            // Log assignment counts
            Console.WriteLine($"  - Business: {flightAssignment.BusinessStewards.Count}, " +
                            $"Economy: {flightAssignment.EconomyStewards.Count}");
        }

        private async Task ClearAndCreateNewAssignments(WeeklySchedule schedule)
        {
            // Clear all existing assignments for the week
            var weekStart = schedule.WeekStart;
            var weekEnd = schedule.WeekEnd;

            Console.WriteLine($"Clearing all existing flight assignments for the week {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}...");

            // Get all flights for the week
            var weeklyFlights = await _unitOfWork.Flights.FindAsync(f =>
                f.DepartureTime >= weekStart && f.DepartureTime < weekEnd);

            var weeklyFlightIds = weeklyFlights.Select(f => f.FlightId).ToList();

            Console.WriteLine($"Found {weeklyFlightIds.Count} flights for the week");

            // Get and remove all assignments for these flights
            var existingAssignments = await _unitOfWork.Assignments.FindAsync(a => weeklyFlightIds.Contains(a.FlightId));

            Console.WriteLine($"Found {existingAssignments.Count()} existing assignments to remove");
            foreach (var assignment in existingAssignments)
            {
                await _unitOfWork.Assignments.RemoveAsync(assignment);
            }

            // Create new assignments
            Console.WriteLine("Creating new assignments...");
            int assignmentCount = await CreateNewAssignments(schedule);

            Console.WriteLine($"Created {assignmentCount} new assignments");
        }

        private async Task<int> CreateNewAssignments(WeeklySchedule schedule)
        {
            int assignmentCount = 0;

            foreach (var flightAssignment in schedule.FlightAssignments)
            {
                var month = flightAssignment.Flight.DepartureTime.Month;
                var year = flightAssignment.Flight.DepartureTime.Year;

                // Add business stewards
                assignmentCount += await CreateStewardAssignments(
                    flightAssignment.BusinessStewards,
                    flightAssignment.Flight,
                    year,
                    month);

                // Add economy stewards
                assignmentCount += await CreateStewardAssignments(
                    flightAssignment.EconomyStewards,
                    flightAssignment.Flight,
                    year,
                    month);
            }

            return assignmentCount;
        }

        private async Task<int> CreateStewardAssignments(
            List<StewardDto> stewards,
            FlightDto flight,
            int year,
            int month)
        {
            int count = 0;

            foreach (var steward in stewards)
            {
                try
                {
                    var assignment = new Assignment
                    {
                        StewardId = steward.StewardId,
                        FlightId = flight.FlightId
                    };

                    await _unitOfWork.Assignments.AddAsync(assignment);
                    count++;

                    // Update the steward's monthly hours
                    await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                        steward.StewardId,
                        year,
                        month,
                        flight.FlightTime);

                    // Update last flight end time
                    await _unitOfWork.Stewards.UpdateLastFlightTimeAsync(
                        steward.StewardId,
                        flight.ArrivalTime);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating assignment for steward {steward.StewardId} " +
                                    $"on flight {flight.FlightId}: {ex.Message}");
                    throw; // Re-throw to be caught by outer exception handler
                }
            }

            return count;
        }

        private void LogException(Exception ex)
        {
            Console.WriteLine($"ERROR saving schedule: {ex.Message}");
            Console.WriteLine($"Exception type: {ex.GetType().Name}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }

        // Get a previously saved schedule for a week
        public async Task<WeeklySchedule> GetScheduleForWeekAsync(DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = NormalizeToWeekStart(weekStart);
            var weekEnd = weekStart.AddDays(7);

            // Get all flights and stewards for the week
            var flights = await GetFlightsForWeekAsync(weekStart, weekEnd);
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);
            var stewardMap = stewards.ToDictionary(s => s.StewardId);

            // Create schedule
            var schedule = new WeeklySchedule
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };

            // Load flight assignments
            await LoadFlightAssignments(schedule, flights, stewardMap);

            // Calculate fitness score
            schedule.FitnessScore = FitnessCalculator.CalculateScheduleFitness(schedule, stewards);

            return schedule;
        }

        private async Task LoadFlightAssignments(
            WeeklySchedule schedule,
            List<FlightDto> flights,
            Dictionary<int, StewardDto> stewardMap)
        {
            foreach (var flight in flights)
            {
                var flightAssignment = new FlightAssignment { Flight = flight };

                // Get stewards assigned to this flight
                var assignments = await _unitOfWork.Assignments.GetAssignmentsByFlightAsync(flight.FlightId);

                // Add stewards to the assignment
                ProcessFlightAssignments(assignments, flightAssignment, stewardMap, schedule, flight);

                schedule.FlightAssignments.Add(flightAssignment);
            }
        }

        private void ProcessFlightAssignments(
            IEnumerable<Assignment> assignments,
            FlightAssignment flightAssignment,
            Dictionary<int, StewardDto> stewardMap,
            WeeklySchedule schedule,
            FlightDto flight)
        {
            foreach (var assignment in assignments)
            {
                if (stewardMap.TryGetValue(assignment.StewardId, out var steward))
                {
                    // Add to the correct steward category
                    if (steward.Role == "Business")
                    {
                        flightAssignment.BusinessStewards.Add(steward);
                    }
                    else if (steward.Role == "Economy")
                    {
                        flightAssignment.EconomyStewards.Add(steward);
                    }

                    // Update steward's schedule
                    UpdateStewardScheduleInformation(steward, flight, schedule);
                }
            }
        }

        private void UpdateStewardScheduleInformation(
            StewardDto steward,
            FlightDto flight,
            WeeklySchedule schedule)
        {
            // Add to steward's schedule
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

            schedule.StewardSchedules[steward.StewardId].Add(flight);

            // Add flight hours to schedule tracking
            schedule.AddStewardHours(steward.StewardId, flight.FlightTime);
        }

        // Get a steward's schedule for a specific week
        public async Task<List<FlightDto>> GetStewardScheduleAsync(int stewardId, DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = NormalizeToWeekStart(weekStart);
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

        // Helper methods for data access

        private async Task<List<FlightDto>> GetFlightsForWeekAsync(DateTime weekStart, DateTime weekEnd)
        {
            // Get all flights departing within the week
            var flights = await _unitOfWork.Flights.FindAsync(f =>
                f.DepartureTime >= weekStart && f.DepartureTime < weekEnd);

            var flightDtos = new List<FlightDto>();

            foreach (var flight in flights)
            {
                var dto = MapFlightToDto(flight);
                await LoadFlightCrewRequirements(flight, dto);
                flightDtos.Add(dto);
            }

            return flightDtos;
        }

        private async Task LoadFlightCrewRequirements(Flight flight, FlightDto dto)
        {
            // Get required crew counts from aircraft
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
                var dto = MapStewardToDto(steward);

                // Load additional steward information
                await LoadStewardDetails(dto, steward.StewardId, year, month);

                dtos.Add(dto);
            }

            return dtos;
        }

        private StewardDto MapStewardToDto(Steward steward)
        {
            return new StewardDto
            {
                StewardId = steward.StewardId,
                FirstName = steward.FirstName,
                LastName = steward.LastName,
                Role = steward.Role.ToString(),
                IsSenior = steward.IsSenior,
                JoiningDate = steward.JoiningDate,
                LastFlightEndTime = steward.LastFlightEndTime,
            };
        }

        private async Task LoadStewardDetails(StewardDto dto, int stewardId, int year, int month)
        {
            // Load hours for the current month
            float monthlyHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(stewardId, year, month);
            dto.MonthlyHours = monthlyHours;

            // Get license IDs
            var licenseIds = await _unitOfWork.Stewards.GetStewardLicenseIdsAsync(stewardId);
            dto.LicenseIds = licenseIds.ToList();

            // Get language IDs
            var languageIds = await _unitOfWork.Stewards.GetStewardLanguageIdsAsync(stewardId);
            dto.LanguageIds = languageIds.ToList();

            var licenseNames = await _unitOfWork.Stewards.GetStewardLicenseNamesAsync(stewardId);
            dto.LicensedAircraftTypes = licenseNames.ToList();

            // Get feedback counts
            var positiveFeedbackCount = await _unitOfWork.Feedbacks.GetPositiveFeedbackCountAsync(stewardId);
            dto.PositiveFeedbackCount = positiveFeedbackCount;
            var negativeFeedbackCount = await _unitOfWork.Feedbacks.GetNegativeFeedbackCountAsync(stewardId);
            dto.NegativeFeedbackCount = negativeFeedbackCount;
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