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

            // Get all stewards
            var stewards = await GetAllStewardsWithDetailsAsync();

            // Run the genetic scheduler
            var geneticScheduler = new GeneticScheduler();
            var schedule = geneticScheduler.OptimizeSchedule(flights, stewards, weekStart);

            return schedule;
        }

        // Save a generated schedule to the database
        public async Task<bool> SaveScheduleAsync(WeeklySchedule schedule)
        {
            try
            {
                // Clear any existing assignments for the flights in this schedule
                var flightIds = schedule.FlightAssignments.Select(fa => fa.Flight.FlightId).ToList();
                var existingAssignments = await _unitOfWork.Assignments.FindAsync(a => flightIds.Contains(a.FlightId));

                foreach (var assignment in existingAssignments)
                {
                    await _unitOfWork.Assignments.RemoveAsync(assignment);
                }

                // Create new assignments
                foreach (var flightAssignment in schedule.FlightAssignments)
                {
                    // Add business stewards
                    foreach (var steward in flightAssignment.BusinessStewards)
                    {
                        var assignment = new Assignment
                        {
                            StewardId = steward.StewardId,
                            FlightId = flightAssignment.Flight.FlightId
                        };

                        await _unitOfWork.Assignments.AddAsync(assignment);

                        // Update steward's monthly hours
                        var month = flightAssignment.Flight.DepartureTime.Month;
                        var year = flightAssignment.Flight.DepartureTime.Year;
                        await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                            steward.StewardId,
                            year,
                            month,
                            flightAssignment.Flight.FlightTime);
                    }

                    // Add economy stewards
                    foreach (var steward in flightAssignment.EconomyStewards)
                    {
                        var assignment = new Assignment
                        {
                            StewardId = steward.StewardId,
                            FlightId = flightAssignment.Flight.FlightId
                        };

                        await _unitOfWork.Assignments.AddAsync(assignment);

                        // Update steward's monthly hours
                        var month = flightAssignment.Flight.DepartureTime.Month;
                        var year = flightAssignment.Flight.DepartureTime.Year;
                        await _unitOfWork.Stewards.UpdateMonthlyHoursAsync(
                            steward.StewardId,
                            year,
                            month,
                            flightAssignment.Flight.FlightTime);
                    }
                }

                // Save changes
                await _unitOfWork.CompleteAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Get a previously saved schedule for a week
        public async Task<WeeklySchedule> GetScheduleForWeekAsync(DateTime weekStart)
        {
            // Normalize to the start of the week (Monday)
            weekStart = weekStart.Date.AddDays(-(int)weekStart.DayOfWeek + 1);
            if (weekStart.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(1);

            var weekEnd = weekStart.AddDays(7);

            // Get all flights for the week
            var flights = await GetFlightsForWeekAsync(weekStart, weekEnd);

            // Get all stewards
            var stewards = await GetAllStewardsWithDetailsAsync();

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

        private async Task<List<StewardDto>> GetAllStewardsWithDetailsAsync()
        {
            var stewards = await _unitOfWork.Stewards.GetAllAsync();
            var dtos = new List<StewardDto>();

            foreach (var steward in stewards)
            {
                var dto = new StewardDto
                {
                    StewardId = steward.StewardId,
                    FirstName = steward.FirstName,
                    LastName = steward.LastName,
                    Role = steward.Role.ToString(), // Use the property accessor that handles the conversion
                    IsSenior = steward.IsSenior,
                    JoiningDate = steward.JoiningDate,
                    LastFlightEndTime = steward.LastFlightEndTime,

                    // Calculate current month's hours
                    MonthlyHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(
                        steward.StewardId,
                        DateTime.Now.Year,
                        DateTime.Now.Month)
                };

                // Get licenses
                var licenses = await _unitOfWork.Stewards.FindAsync(s =>
                    s.StewardId == steward.StewardId);

                dto.LicenseIds = steward.StewardLicenses.Select(sl => sl.LicenseId).ToList();

                // Get languages
                dto.LanguageIds = steward.StewardLanguages.Select(sl => sl.LanguageId).ToList();

                // Get feedback counts
                dto.PositiveFeedbackCount = await _unitOfWork.Feedbacks.GetPositiveFeedbackCountAsync(steward.StewardId);
                dto.NegativeFeedbackCount = await _unitOfWork.Feedbacks.GetNegativeFeedbackCountAsync(steward.StewardId);

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
                Priority = flight.Priority,
                ReturnFlightId = flight.ReturnFlightId
            };
        }
    }
}
