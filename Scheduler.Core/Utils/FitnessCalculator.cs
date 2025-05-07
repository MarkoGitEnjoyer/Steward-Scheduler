using Scheduler.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Utils
{
    public static class FitnessCalculator
    {
        // Calculate the fitness score for an entire weekly schedule
        public static float CalculateScheduleFitness(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return 0;

            // Get the fitness components
            float flightCoverageRate = CalculateFlightCoverageRate(schedule);
            float completionRate = CalculateCompletionRate(schedule);
            float workloadBalance = CalculateWorkloadBalance(schedule, allStewards);
            float languageMatchRate = CalculateLanguageMatchRate(schedule);
            float qualityMatchRate = CalculateQualityMatchRate(schedule);

            // Weighted combination of fitness components
            float fitnessScore = 0.62f * flightCoverageRate +       // Increased weight for coverage
                               0.08f * completionRate +             // Reduced weight
                               0.1f * workloadBalance +            // Reduced weight
                               0.1f * languageMatchRate +          // Reduced weight
                               0.1f * qualityMatchRate;           // Reduced weight

            return fitnessScore;
        }

        // Calculate coverage rate of flights
        private static float CalculateFlightCoverageRate(WeeklySchedule schedule)
        {
            // Track total flights that should be scheduled this week
            int totalPossibleFlights = schedule.TotalFlightCount > 0 ?
                schedule.TotalFlightCount : schedule.FlightAssignments.Count;

            // Calculate the number of *fully completed* flight assignments.
            int fullyAssignedFlights = schedule.FlightAssignments.Count(fa => fa.IsComplete());

            // Calculate coverage rate based on *completed* assignments.
            return (float)fullyAssignedFlights / totalPossibleFlights;
        }

        // Calculate completion rate of scheduled flights
        private static float CalculateCompletionRate(WeeklySchedule schedule)
        {
            return schedule.FlightAssignments.Count(fa => fa.IsComplete()) /
                  (float)Math.Max(1, schedule.FlightAssignments.Count);
        }

        // Calculate how evenly the work is distributed
        private static float CalculateWorkloadBalance(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (allStewards.Count == 0)
                return 0;

            // Calculate standard deviation of hours for active stewards
            float balanceScore = CalculateHoursBalanceScore(schedule);

            return balanceScore;
        }
       
        private static float CalculateHoursBalanceScore(WeeklySchedule schedule)
        {
            // Get hours for all active stewards (those with hours in the current schedule)
            var activeHours = schedule.StewardHours.Values.ToList();

            if (activeHours.Count == 0)
                return 0;

            // Calculate standard deviation
            float avgHours = activeHours.Average();
            float sumSquaredDiff = activeHours.Sum(h => (h - avgHours) * (h - avgHours));
            float stdDev = (float)Math.Sqrt(sumSquaredDiff / activeHours.Count);

            // Convert to a 0-1 score (lower std dev is better)
            float maxStdDev = 20.0f;
            return Math.Max(0, 1 - (stdDev / maxStdDev));
        }

        // Calculate how well language requirements are met
        private static float CalculateLanguageMatchRate(WeeklySchedule schedule)
        {
            int matchCount = 0;
            int totalFlights = schedule.FlightAssignments.Count;

            // If there are no flights, return 1.0 (perfect score)
            if (totalFlights == 0)
                return 1.0f;

            foreach (var assignment in schedule.FlightAssignments)
            {
                // Only check flights that have language requirements
                if (assignment.Flight.RequiredLanguageId.HasValue && assignment.Flight.RequiredLanguageId.Value > 0)
                {
                    // Check if any assigned steward speaks the required language
                    bool hasLanguageMatch = HasStewardWithRequiredLanguage(assignment);

                    if (hasLanguageMatch)
                        matchCount++;
                }
                else
                {
                    // Flights without language requirements are considered matched
                    matchCount++;
                }
            }

            // Return the ratio of matches to total flights
            return (float)matchCount / totalFlights;
        }

        private static bool HasStewardWithRequiredLanguage(FlightAssignment assignment)
        {
            int requiredLanguageId = assignment.Flight.RequiredLanguageId.Value;

            return assignment.BusinessStewards.Any(s => s.LanguageIds.Contains(requiredLanguageId)) ||
                   assignment.EconomyStewards.Any(s => s.LanguageIds.Contains(requiredLanguageId));
        }

        // Calculate how well steward quality matches flight priority
        private static float CalculateQualityMatchRate(WeeklySchedule schedule)
        {
            float totalScore = 0;
            int flightCount = schedule.FlightAssignments.Count;

            if (flightCount == 0)
                return 0;

            foreach (var assignment in schedule.FlightAssignments)
            {
                float matchScore = CalculateFlightQualityMatch(assignment);
                totalScore += matchScore;
            }

            return totalScore / flightCount;
        }

        private static float CalculateFlightQualityMatch(FlightAssignment assignment)
        {
            float flightImportance = Math.Min(1.0f, assignment.Flight.Priority / 5.0f); // Normalize to 0-1 for 1-5 scale
            float stewardQuality = CalculateAverageStewardQuality(assignment);

            // Higher score if high-quality stewards are assigned to high-priority flights
            float matchScore;

            // If flight is high priority, we want high quality stewards
            if (flightImportance > 0.7f)
            {
                // For high priority flights, we want quality >= importance
                matchScore = stewardQuality >= flightImportance ? 1.0f : stewardQuality / flightImportance;
            }
            else if (flightImportance <= 0.3f)
            {
                // For low priority flights, we don't need high quality stewards
                matchScore = 1.0f - Math.Max(0, stewardQuality - (flightImportance + 0.2f));
            }
            else
            {
                // For medium priority flights, we want a close match
                matchScore = 1.0f - Math.Abs(flightImportance - stewardQuality);
            }

            return matchScore;
        }

        private static float CalculateAverageStewardQuality(FlightAssignment assignment)
        {
            var allAssignedStewards = new List<StewardDto>();
            allAssignedStewards.AddRange(assignment.BusinessStewards);
            allAssignedStewards.AddRange(assignment.EconomyStewards);

            if (allAssignedStewards.Count == 0)
                return 0;

            // Calculate average quality score for assigned stewards
            float avgExperience = allAssignedStewards.Average(s => Math.Min(1.0f, s.ExperienceYears / 10.0f)); // Normalize to 0-1
            float avgFeedback = allAssignedStewards.Average(s =>
                Math.Min(1.0f, Math.Max(0, s.FeedbackScore / 5.0f))); // Normalize to 0-1

            return (avgExperience + avgFeedback) / 2.0f;
        }

       
    }
}