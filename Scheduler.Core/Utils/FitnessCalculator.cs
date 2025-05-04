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

            // Calculate hours per steward
            var stewardTotalHours = InitializeStewardHours(allStewards);

            // Add hours from the schedule
            AddScheduledHoursToTotals(schedule, stewardTotalHours);

            // Calculate standard deviation of hours for active stewards
            float balanceScore = CalculateHoursBalanceScore(schedule, stewardTotalHours);

            return balanceScore;
        }

        private static Dictionary<int, float> InitializeStewardHours(List<StewardDto> allStewards)
        {
            var stewardTotalHours = new Dictionary<int, float>();

            // Initialize with current monthly hours
            foreach (var steward in allStewards)
            {
                stewardTotalHours[steward.StewardId] = steward.MonthlyHours;
            }

            return stewardTotalHours;
        }

        private static void AddScheduledHoursToTotals(
            WeeklySchedule schedule,
            Dictionary<int, float> stewardTotalHours)
        {
            foreach (var kvp in schedule.StewardHours)
            {
                int stewardId = kvp.Key;
                float scheduledHours = kvp.Value;

                if (stewardTotalHours.ContainsKey(stewardId))
                {
                    stewardTotalHours[stewardId] += scheduledHours;
                }
            }
        }

        private static float CalculateHoursBalanceScore(
            WeeklySchedule schedule,
            Dictionary<int, float> stewardTotalHours)
        {
            // Only consider stewards who are actually assigned to flights
            var activeHours = stewardTotalHours
                .Where(kv => schedule.StewardHours.ContainsKey(kv.Key) && schedule.StewardHours[kv.Key] > 0)
                .Select(kv => kv.Value)
                .ToList();

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
            int totalFlightsWithLanguageReq = 0;

            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.Flight.RequiredLanguageId.HasValue && assignment.Flight.RequiredLanguageId.Value > 0)
                {
                    totalFlightsWithLanguageReq++;

                    // Check if any assigned steward speaks the required language
                    bool hasLanguageMatch = HasStewardWithRequiredLanguage(assignment);

                    if (hasLanguageMatch)
                        matchCount++;
                }
            }

            return totalFlightsWithLanguageReq == 0 ? 1.0f : (float)matchCount / totalFlightsWithLanguageReq;
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

        // Calculate a steward's suitability score for a specific flight
        public static float CalculateStewardScore(StewardDto steward, FlightDto flight, SchedulingWeights weights, float averageMonthlyHours)
        {
            if (steward == null || flight == null)
                return 0;

            // Check hard constraints first - if any is violated, return 0
            if (!steward.HasLicenseForAircraft(flight.AircraftType) ||
                !steward.IsAvailable(flight.DepartureTime, flight.FlightTime))
                return 0;

            // Calculate soft constraint scores
            var scores = CalculateStewardScoreComponents(steward, flight, averageMonthlyHours);

            // Calculate weighted score - ensure it stays within 0-1 range
            float totalScore = weights.ExperienceWeight * scores.ExperienceScore +
                             weights.FeedbackWeight * scores.FeedbackScore +
                             weights.WorkloadBalanceWeight * scores.WorkloadScore +
                             weights.LanguageWeight * scores.LanguageScore;

            return Math.Min(1.0f, totalScore);
        }

        private static (float ExperienceScore, float FeedbackScore, float WorkloadScore, float LanguageScore)
            CalculateStewardScoreComponents(StewardDto steward, FlightDto flight, float averageMonthlyHours)
        {
            // Experience score (0-1): More experienced stewards score higher
            float experienceScore = Math.Min(1.0f, steward.ExperienceYears / 10.0f);

            // Feedback score (0-1): Stewards with more positive feedback score higher
            float feedbackScore = (steward.PositiveFeedbackCount - steward.NegativeFeedbackCount);
            feedbackScore = Math.Min(1.0f, Math.Max(0, feedbackScore / 5.0f)); // Normalize to 0-1

            // Workload balance score (0-1): Stewards with fewer flight hours score higher
            float workloadScore = 1.0f - (steward.MonthlyHours / Math.Max(1, averageMonthlyHours));
            workloadScore = Math.Max(0, Math.Min(1.0f, workloadScore)); // Clamp to 0-1

            // Language match score (0-1): Stewards who speak the required language score higher
            float languageScore = 0;
            if (flight.RequiredLanguageId.HasValue &&
                flight.RequiredLanguageId.Value > 0 &&
                steward.LanguageIds.Contains(flight.RequiredLanguageId.Value))
            {
                languageScore = 1.0f;
            }

            return (experienceScore, feedbackScore, workloadScore, languageScore);
        }
    }
}