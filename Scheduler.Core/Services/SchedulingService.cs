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

        // generate a weekly schedule
        public async Task<WeeklySchedule> GenerateWeeklyScheduleAsync(DateTime weekStart)
        {
            // normalize to the start of the week (Monday)
            weekStart = NormalizeToWeekStart(weekStart);

            // get all flights for the week
            var flights = await GetFlightsForWeekAsync(weekStart);

            // get all stewards with their current monthly hours
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);

            Console.WriteLine($"Generating schedule with {stewards.Count} stewards and {flights.Count} flights.");

            // generate schedule using genetic algorithm directly
            var optimizedSchedule = GenerateScheduleWithGA(flights, stewards, weekStart);

            // report final stats
            Console.WriteLine($"Final schedule: {optimizedSchedule.FlightAssignments.Where(fl => fl.IsComplete()).Count()} flights scheduled");

            return optimizedSchedule;
        }

        private DateTime NormalizeToWeekStart(DateTime date)
        {
            return date.Date.AddDays(-(int)date.DayOfWeek + 1);
        }

        private WeeklySchedule GenerateScheduleWithGA(
            List<FlightDto> flights,
            List<StewardDto> stewards,
            DateTime weekStart)
        {
            // create new config for GA
            var geneticConfig = new GeneticAlgorithmConfig();
            // create instance of GA with config
            var geneticScheduler = new GeneticScheduler(geneticConfig);
            // run GA
            return geneticScheduler.OptimizeSchedule(flights, stewards, weekStart);
        }

        // save a generated schedule to the database
        public async Task<bool> SaveScheduleAsync(WeeklySchedule schedule)
        {
            try
            {
                // get the month for this scheduling period
                int scheduleMonth = schedule.WeekStart.Month;
                int scheduleYear = schedule.WeekStart.Year;

                Console.WriteLine($"Validating schedule for month: {scheduleYear}-{scheduleMonth}");
                Console.WriteLine($"Schedule contains {schedule.FlightAssignments.Where(fl=>fl.IsComplete()).Count()} flight assignments");

                // clear existing assignments and create new ones
                await ClearAndCreateNewAssignments(schedule);

                // save all changes
                int changes = await _unitOfWork.CompleteAsync();
                Console.WriteLine($"Successfully saved {changes} changes to database.");
                return true;
            }
            catch (Exception ex)
            {
                // log the exception
                LogException(ex);
                return false;
            }
        }

      
        private async Task ClearAndCreateNewAssignments(WeeklySchedule schedule)
        {
            // ccear all existing assignments for the week
            var weekStart = schedule.WeekStart;
            var weekEnd = schedule.WeekEnd;

            Console.WriteLine($"Clearing all existing flight assignments for the week {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}...");

            // get all flights for the week
            var weeklyFlights = await _unitOfWork.Flights.GetFlightsForAWeek(weekStart);

            var weeklyFlightIds = weeklyFlights.Select(f => f.FlightId).ToList();

            Console.WriteLine($"Found {weeklyFlightIds.Count} flights for the week");

            // get and remove all assignments for these flights
            var existingAssignments = await _unitOfWork.Assignments.FindAsync(a => weeklyFlightIds.Contains(a.FlightId));

            Console.WriteLine($"Found {existingAssignments.Count()} existing assignments to remove");
            foreach (var assignment in existingAssignments)
            {
                await _unitOfWork.Assignments.RemoveAsync(assignment);
            }

            // create new assignments
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

                // add stewards
                assignmentCount += await CreateStewardAssignments(
                    flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards).ToList(),
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

                    // update the steward's monthly hours
                    await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                        steward.StewardId,
                        year,
                        month,
                        flight.FlightTime);

                    // update last flight end time
                    await _unitOfWork.Stewards.UpdateLastFlightTimeAsync(
                        steward.StewardId,
                        flight.ArrivalTime);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating assignment for steward {steward.StewardId} " +
                                    $"on flight {flight.FlightId}: {ex.Message}");
                }
            }

            return count;
        }

        private void LogException(Exception ex)
        {
            Console.WriteLine($"ERROR saving schedule: {ex.Message}");
        }

        // get a previously saved schedule for a week
        public async Task<WeeklySchedule> GetScheduleForWeekAsync(DateTime weekStart)
        {
            // normalize to the start of the week (Monday)
            weekStart = NormalizeToWeekStart(weekStart);
            var weekEnd = weekStart.AddDays(7);

            // get all flights and stewards for the week
            var flights = await GetFlightsForWeekAsync(weekStart);
            var stewards = await GetAllStewardsWithDetailsAsync(weekStart);
            var stewardMap = stewards.ToDictionary(s => s.StewardId);

            // create schedule
            var schedule = new WeeklySchedule
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };
            schedule.TotalFlightCount = flights.Count;

            // load flight assignments
            await LoadFlightAssignments(schedule, flights, stewardMap);

            // calculate fitness score
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

                // get stewards assigned to this flight
                var assignments = await _unitOfWork.Assignments.GetAssignmentsByFlightAsync(flight.FlightId);

                // add stewards to the assignment
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
                    // add to the correct steward category
                    if (steward.Role == "Business")
                    {
                        flightAssignment.BusinessStewards.Add(steward);
                    }
                    else if (steward.Role == "Economy")
                    {
                        flightAssignment.EconomyStewards.Add(steward);
                    }

                    // update steward's schedule
                    UpdateStewardScheduleInformation(steward, flight, schedule);
                }
            }
        }

        private void UpdateStewardScheduleInformation(
            StewardDto steward,
            FlightDto flight,
            WeeklySchedule schedule)
        {
            // add to steward's schedule
            if (!schedule.StewardSchedules.ContainsKey(steward.StewardId))
                schedule.StewardSchedules[steward.StewardId] = new List<FlightDto>();

            schedule.StewardSchedules[steward.StewardId].Add(flight);

            // add flight hours to schedule tracking
            schedule.AddStewardHours(steward.StewardId, flight.FlightTime);
        }

        // get a steward's schedule for a specific week
        public async Task<List<FlightDto>> GetStewardScheduleAsync(int stewardId, DateTime weekStart)
        {
            // normalize to the start of the week (Monday)
            weekStart = NormalizeToWeekStart(weekStart);
            var weekEnd = weekStart.AddDays(7);

            // get steward assignments
            var assignments = await _unitOfWork.Assignments.GetAssignmentsByStewardAsync(stewardId);

            // filter to this week and map to flight DTOs
            var flights = assignments
                .Where(a => a.Flight.DepartureTime >= weekStart && a.Flight.DepartureTime < weekEnd)
                .Select(a => MapFlightToDto(a.Flight))
                .OrderBy(f => f.DepartureTime)
                .ToList();

            return flights;
        }

        // helper methods for data access

        private async Task<List<FlightDto>> GetFlightsForWeekAsync(DateTime weekStart)
        {
            // get all flights departing within the week
            var flights = await _unitOfWork.Flights.GetFlightsForAWeek(weekStart);

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
             var aircraft = await _unitOfWork.AircraftTypes.GetAircraftTypeByNameAsync(flight.AircraftType);
             if (aircraft != null)
               {
                    dto.RequiredBusinessCrew = aircraft.BusinessClassCrew;
                    dto.RequiredEconomyCrew = aircraft.EconomyClassCrew;
               }
         
        }

        private async Task<List<StewardDto>> GetAllStewardsWithDetailsAsync(DateTime weekStart)
        {
            var stewards = await _unitOfWork.Stewards.GetAllAsync();
            var dtos = new List<StewardDto>();

            // determine which month we need to load (the month of the start date)
            int year = weekStart.Year;
            int month = weekStart.Month;

            foreach (var steward in stewards)
            {
                var dto = MapStewardToDto(steward);

                // load additional steward information
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
            // load hours for the current month
            float monthlyHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(stewardId, year, month);
            dto.MonthlyHours = monthlyHours;

            // get license IDs
            var licenseIds = await _unitOfWork.Stewards.GetStewardLicenseIdsAsync(stewardId);
            dto.LicenseIds = licenseIds.ToList();

            // get language IDs
            var languageIds = await _unitOfWork.Stewards.GetStewardLanguageIdsAsync(stewardId);
            dto.LanguageIds = languageIds.ToList();

            var licenseNames = await _unitOfWork.Stewards.GetStewardLicenseNamesAsync(stewardId);
            dto.LicensedAircraftTypes = licenseNames.ToList();

            // get feedback counts
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