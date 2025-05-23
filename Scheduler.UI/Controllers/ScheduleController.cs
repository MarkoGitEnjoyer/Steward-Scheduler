using Scheduler.Core.Models;
using Scheduler.Core.Services;
using Scheduler.UI.Models;

namespace Scheduler.UI.Controllers
{
    public class ScheduleController
    {
        private readonly ISchedulingService _schedulingService;

        public ScheduleController(ISchedulingService schedulingService)
        {
            _schedulingService = schedulingService;
        }

        public async Task<ScheduleViewModel> GetScheduleForWeekAsync(DateTime weekStart)
        {
            var viewModel = new ScheduleViewModel { IsLoading = true };

            try
            {
                // normalize to the start of the week (Monday)
                weekStart = AdjustToMonday(weekStart);
                viewModel.WeekStart = weekStart;
                viewModel.WeekEnd = weekStart.AddDays(7);

                // load the schedule for the selected week
                var schedule = await _schedulingService.GetScheduleForWeekAsync(weekStart);

                if (schedule != null)
                {
                    viewModel.FlightAssignments = schedule.FlightAssignments
                        .Select(fa => MapFlightAssignmentToViewModel(fa))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                viewModel.ErrorMessage = $"Error loading schedule: {ex.Message}";
            }
            finally
            {
                viewModel.IsLoading = false;
            }

            return viewModel;
        }

        public async Task<bool> GenerateScheduleAsync(DateTime selectedDate)
        {
            // adjust to Monday of the selected week
            selectedDate = AdjustToMonday(selectedDate);

            // generate the schedule
            var generatedSchedule = await _schedulingService.GenerateWeeklyScheduleAsync(selectedDate);

            // save the generated schedule
            await _schedulingService.SaveScheduleAsync(generatedSchedule);

            // return success
            return true;
        }

        private DateTime AdjustToMonday(DateTime date)
        {
            return date.AddDays(-(int)date.DayOfWeek + 1);
        }

        private FlightAssignmentViewModel MapFlightAssignmentToViewModel(FlightAssignment assignment)
        {
            var viewModel = new FlightAssignmentViewModel
            {
                Flight = new FlightViewModel
                {
                    FlightId = assignment.Flight.FlightId,
                    FlightNumber = assignment.Flight.FlightNumber,
                    DepartureTime = assignment.Flight.DepartureTime,
                    ArrivalTime = assignment.Flight.ArrivalTime,
                    AircraftType = assignment.Flight.AircraftType,
                    Destination = assignment.Flight.Destination,
                    RequiredLanguageId = assignment.Flight.RequiredLanguageId,
                    FlightTime = assignment.Flight.FlightTime,
                    Priority = assignment.Flight.Priority,
                    RequiredBusinessCrew = assignment.Flight.RequiredBusinessCrew,
                    RequiredEconomyCrew = assignment.Flight.RequiredEconomyCrew
                },
                BusinessStewards = assignment.BusinessStewards
                    .Select(s => new StewardViewModel
                    {
                        StewardId = s.StewardId,
                        FirstName = s.FirstName,
                        LastName = s.LastName,
                        Role = s.Role,
                        IsSenior = s.IsSenior
                    })
                    .ToList(),
                EconomyStewards = assignment.EconomyStewards
                    .Select(s => new StewardViewModel
                    {
                        StewardId = s.StewardId,
                        FirstName = s.FirstName,
                        LastName = s.LastName,
                        Role = s.Role,
                        IsSenior = s.IsSenior
                    })
                    .ToList(),
                IsComplete = assignment.IsComplete()
            };

            return viewModel;
        }
    }
}