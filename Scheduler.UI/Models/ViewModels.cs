using Scheduler.Core.Models;
using System.ComponentModel.DataAnnotations;

namespace Scheduler.UI.Models
{
    // Base class for view models with common properties
    public abstract class BaseViewModel
    {
        public bool IsLoading { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // Dashboard view models
    public class DashboardViewModel : BaseViewModel
    {
        public int CurrentWeekFlights { get; set; }
        public int AssignedFlights { get; set; }
        public int ActiveStewards { get; set; }
        public List<FlightViewModel> UpcomingFlights { get; set; } = new();
    }

    // Schedule view models
    public class ScheduleViewModel : BaseViewModel
    {
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public List<FlightAssignmentViewModel> FlightAssignments { get; set; } = new();

        // Fixed formatted week range calculation
        public string FormattedWeekRange
        {
            get
            {
                try
                {
                    // Safely calculate the end of the week
                    DateTime displayEndDate = WeekStart.AddDays(6);
                    return $"{WeekStart:MMM dd} - {displayEndDate:MMM dd, yyyy}";
                }
                catch
                {
                    // Fallback if there's any issue with date calculations
                    return $"{WeekStart:MMM dd, yyyy} week";
                }
            }
        }
    }

    public class ScheduleGenerationViewModel : BaseViewModel
    {
        [Required]
        [Display(Name = "Week Start Date")]
        public DateTime SelectedDate { get; set; } = new DateTime(2025, 2, 17)  ;

        public bool GenerationCompleted { get; set; }
    }

    public class FlightViewModel
    {
        public int FlightId { get; set; }
        public string FlightNumber { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string AircraftType { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int? RequiredLanguageId { get; set; }
        public string RequiredLanguageName { get; set; } = string.Empty;
        public float FlightTime { get; set; }
        public int Priority { get; set; }
        public int RequiredBusinessCrew { get; set; }
        public int RequiredEconomyCrew { get; set; }
        public bool IsFullyAssigned { get; set; }
        public bool StewardSpeaksLanguage { get; set; }
    }

    public class FlightAssignmentViewModel
    {
        public FlightViewModel Flight { get; set; } = new();
        public List<StewardViewModel> BusinessStewards { get; set; } = new();
        public List<StewardViewModel> EconomyStewards { get; set; } = new();
        public bool IsComplete { get; set; }
    }

    // Steward view models
    public class StewardViewModel : BaseViewModel
    {
        public int StewardId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string Role { get; set; } = string.Empty;
        public bool IsSenior { get; set; }
        public DateTime JoiningDate { get; set; }
        public float MonthlyHours { get; set; }
        public float ExperienceYears { get; set; }
        public int PositiveFeedbackCount { get; set; }
        public int NegativeFeedbackCount { get; set; }
        public List<int> LanguageIds { get; set; } = new();
        public List<string> Languages { get; set; } = new();
        public List<string> Licenses { get; set; } = new();
        public List<FlightViewModel> ScheduledFlights { get; set; } = new();
    }

    public class StewardScheduleViewModel : BaseViewModel
    {
        public StewardViewModel? SelectedSteward { get; set; }
        public DateTime WeekStart { get; set; }
        public List<FlightViewModel> ScheduledFlights { get; set; } = new();

        // Safe implementation for formatted week range
        public string FormattedWeekRange
        {
            get
            {
                try
                {
                    DateTime endDate = WeekStart.AddDays(6);
                    return $"{WeekStart:MMM dd} - {endDate:MMM dd, yyyy}";
                }
                catch
                {
                    return $"{WeekStart:MMM yyyy} week";
                }
            }
        }

        public List<StewardViewModel> AvailableStewards { get; set; } = new();
    }
}