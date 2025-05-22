using Scheduler.Core.Models;
using Scheduler.Core.Services;
using Scheduler.UI.Models;

namespace Scheduler.UI.Controllers
{
    public class DashboardController
    {
        private readonly ISchedulingService _schedulingService;

        public DashboardController(ISchedulingService schedulingService)
        {
            _schedulingService = schedulingService;
        }

        public async Task<DashboardViewModel> GetDashboardDataAsync()
        {
            var viewModel = new DashboardViewModel { IsLoading = true };

            try
            {
                // Get current week's start date (Monday)
                var today = new DateTime(2025, 2, 17);
                var currentWeekStart = today.AddDays(-(int)today.DayOfWeek + 1);

                // Load current schedule
                var currentSchedule = await _schedulingService.GetScheduleForWeekAsync(currentWeekStart);

                if (currentSchedule != null)
                {
                    // Calculate dashboard metrics
                    viewModel.CurrentWeekFlights = currentSchedule.TotalFlightCount;
                    viewModel.AssignedFlights = currentSchedule.FlightAssignments.Count(fa => fa.IsComplete());

                    var allStewardIds = new HashSet<int>();
                    foreach (var fa in currentSchedule.FlightAssignments)
                    {
                        foreach (var steward in fa.BusinessStewards)
                            allStewardIds.Add(steward.StewardId);

                        foreach (var steward in fa.EconomyStewards)
                            allStewardIds.Add(steward.StewardId);
                    }

                    viewModel.ActiveStewards = allStewardIds.Count;

                    // Get upcoming flights (next 24 hours)
                    var nextDay = today.AddDays(1);
                    viewModel.UpcomingFlights = currentSchedule.FlightAssignments
                        .Where(fa => fa.Flight.DepartureTime >= today && fa.Flight.DepartureTime <= nextDay)
                        .Select(fa => MapFlightToViewModel(fa.Flight, IsFlightFullyAssigned(fa)))
                        .OrderBy(f => f.DepartureTime)
                        .ToList();
                }

            }
            catch (Exception ex)
            {
                viewModel.ErrorMessage = $"Error loading dashboard: {ex.Message}";
            }
            finally
            {
                viewModel.IsLoading = false;
            }

            return viewModel;
        }

        private FlightViewModel MapFlightToViewModel(FlightDto flight, bool isFullyAssigned)
        {
            return new FlightViewModel
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
                RequiredBusinessCrew = flight.RequiredBusinessCrew,
                RequiredEconomyCrew = flight.RequiredEconomyCrew,
                IsFullyAssigned = isFullyAssigned
            };
        }

        private bool IsFlightFullyAssigned(FlightAssignment assignment)
        {
            return assignment.IsComplete();
        }
    }
}