using Scheduler.Core.Services;
using Scheduler.Data;
using Scheduler.UI.Models;

namespace Scheduler.UI.Controllers
{
    public class StewardController
    {
        private readonly ISchedulingService _schedulingService;
        private readonly IUnitOfWork _unitOfWork;

        public StewardController(ISchedulingService schedulingService, IUnitOfWork unitOfWork)
        {
            _schedulingService = schedulingService;
            _unitOfWork = unitOfWork;
        }

        public async Task<StewardScheduleViewModel> GetStewardScheduleAsync(int stewardId, DateTime weekStart)
        {
            var viewModel = new StewardScheduleViewModel { IsLoading = true };

            try
            {
                // Adjust to Monday of the selected week
                weekStart = AdjustToMonday(weekStart);
                viewModel.WeekStart = weekStart;

                // Load all stewards for the dropdown
                var stewards = await _unitOfWork.Stewards.GetAllAsync();
                viewModel.AvailableStewards = stewards.Select(s => new StewardViewModel
                {
                    StewardId = s.StewardId,
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    Role = s.Role.ToString()
                }).ToList();

                // If a steward is selected
                if (stewardId > 0)
                {
                    // Load steward details
                    var steward = await _unitOfWork.Stewards.GetByIdAsync(stewardId);
                    if (steward != null)
                    {
                        // Map to ViewModel
                        viewModel.SelectedSteward = new StewardViewModel
                        {
                            StewardId = steward.StewardId,
                            FirstName = steward.FirstName,
                            LastName = steward.LastName,
                            Role = steward.Role.ToString(),
                            IsSenior = steward.IsSenior,
                            JoiningDate = steward.JoiningDate,
                            MonthlyHours = await _unitOfWork.Stewards.GetMonthlyHoursAsync(
                                steward.StewardId, DateTime.Now.Year, DateTime.Now.Month),
                            ExperienceYears = (float)(DateTime.Now - steward.JoiningDate).TotalDays / 365
                        };

                        // Get steward's languages names for display
                        viewModel.SelectedSteward.Languages = (await _unitOfWork.Stewards.GetStewardLanguageNamesAsync(steward.StewardId)).ToList();

                        // Get steward's licenses names for display
                        viewModel.SelectedSteward.Licenses = (await _unitOfWork.Stewards.GetStewardLicenseNamesAsync(steward.StewardId)).ToList();

                        // Get feedback counts
                        viewModel.SelectedSteward.PositiveFeedbackCount = await _unitOfWork.Feedbacks.GetPositiveFeedbackCountAsync(steward.StewardId);
                        viewModel.SelectedSteward.NegativeFeedbackCount = await _unitOfWork.Feedbacks.GetNegativeFeedbackCountAsync(steward.StewardId);

                        // Load the steward's schedule for the selected week
                        var flights = await _schedulingService.GetStewardScheduleAsync(stewardId, weekStart);
                        viewModel.ScheduledFlights = flights.Select(f => new FlightViewModel
                        {
                            FlightId = f.FlightId,
                            FlightNumber = f.FlightNumber,
                            DepartureTime = f.DepartureTime,
                            ArrivalTime = f.ArrivalTime,
                            AircraftType = f.AircraftType,
                            Destination = f.Destination,
                            FlightTime = f.FlightTime,
                            RequiredLanguageId = f.RequiredLanguageId,
                            Priority = f.Priority,
                            RequiredBusinessCrew = f.RequiredBusinessCrew,
                            RequiredEconomyCrew = f.RequiredEconomyCrew
                        }).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                viewModel.ErrorMessage = $"Error loading steward schedule: {ex.Message}";
            }
            finally
            {
                viewModel.IsLoading = false;
            }

            return viewModel;
        }

        private DateTime AdjustToMonday(DateTime date)
        {
            int daysUntilMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
            return date.AddDays(-daysUntilMonday);
        }
    }
}