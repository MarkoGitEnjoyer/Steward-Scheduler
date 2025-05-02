using Scheduler.Core.Models;
using Scheduler.Core.Services;
using Scheduler.UI.Models;

namespace Scheduler.UI.Controllers
{
    public class ScheduleController
    {
        private readonly ISchedulingService _schedulingService;
        private ScheduleGenerationViewModel _generationState = new();
        private System.Threading.Timer? _progressTimer;

        public ScheduleController(ISchedulingService schedulingService)
        {
            _schedulingService = schedulingService;
        }

        public async Task<ScheduleViewModel> GetScheduleForWeekAsync(DateTime weekStart)
        {
            var viewModel = new ScheduleViewModel { IsLoading = true };

            try
            {
                // Normalize to the start of the week (Monday)
                weekStart = AdjustToMonday(weekStart);
                viewModel.WeekStart = weekStart;
                viewModel.WeekEnd = weekStart.AddDays(7);

                // Load the schedule for the selected week
                var schedule = await _schedulingService.GetScheduleForWeekAsync(weekStart);

                if (schedule != null)
                {
                    viewModel.FitnessScore = schedule.FitnessScore;
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

        public ScheduleGenerationViewModel GetGenerationState()
        {
            return _generationState;
        }

        public async Task StartGenerationAsync(ScheduleGenerationViewModel model)
        {
            try
            {
                _generationState = model;
                _generationState.IsLoading = true;
                _generationState.GenerationProgress = 0;
                _generationState.CurrentStep = "Initializing...";
                _generationState.ErrorMessage = null;
                _generationState.GenerationCompleted = false;

                // Adjust to Monday of the selected week
                _generationState.SelectedDate = AdjustToMonday(_generationState.SelectedDate);

                // Start a timer to simulate progress (since we can't get real-time updates from the algorithm)
                StartProgressSimulation();

                // Call the scheduling service to generate a schedule
                _generationState.CurrentStep = "Generating schedule...";
                var generatedSchedule = await _schedulingService.GenerateWeeklyScheduleAsync(_generationState.SelectedDate);

                // Save the generated schedule
                _generationState.CurrentStep = "Saving schedule to database...";
                await _schedulingService.SaveScheduleAsync(generatedSchedule);

                // Complete the progress
                StopProgressSimulation();
                _generationState.GenerationProgress = 100;
                _generationState.CurrentStep = "Schedule generation completed!";
                _generationState.GenerationCompleted = true;
            }
            catch (Exception ex)
            {
                StopProgressSimulation();
                _generationState.ErrorMessage = $"An error occurred while generating the schedule: {ex.Message}";
            }
            finally
            {
                _generationState.IsLoading = false;
            }
        }

        private void StartProgressSimulation()
        {
            // Use a timer to simulate progress
            _progressTimer = new System.Threading.Timer(UpdateProgress, null, 0, 500);
        }

        private void StopProgressSimulation()
        {
            _progressTimer?.Dispose();
            _progressTimer = null;
        }

        private void UpdateProgress(object? state)
        {
            // Simulate progressive updates
            if (_generationState.GenerationProgress < 95)
            {
                // Randomly increase progress
                int increment = new Random().Next(1, 5);
                _generationState.GenerationProgress = Math.Min(95, _generationState.GenerationProgress + increment);

                // Update the current step based on progress
                if (_generationState.GenerationProgress < 30)
                    _generationState.CurrentStep = "Loading flight and steward data...";
                else if (_generationState.GenerationProgress < 50)
                    _generationState.CurrentStep = "Creating initial population...";
                else if (_generationState.GenerationProgress < 80)
                    _generationState.CurrentStep = "Running genetic algorithm...";
                else
                    _generationState.CurrentStep = "Finalizing optimal schedule...";
            }
        }

        private DateTime AdjustToMonday(DateTime date)
        {
            int daysUntilMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
            return date.AddDays(-daysUntilMonday);
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