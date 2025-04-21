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

            // Track total flights that should be scheduled this week
            int totalPossibleFlights = schedule.TotalFlightCount > 0 ? schedule.TotalFlightCount : schedule.FlightAssignments.Count;

            // --- MODIFICATION START ---
            // Calculate the number of *fully completed* flight assignments.
            int fullyAssignedFlights = schedule.FlightAssignments.Count(fa => fa.IsComplete()); // Use the IsComplete method from FlightAssignment

            // Calculate coverage rate based on *completed* assignments.
            float flightCoverageRate = (float)fullyAssignedFlights / totalPossibleFlights;
            // --- MODIFICATION END ---

            // Calculate completion rate of scheduled flights
            float completionRate = schedule.FlightAssignments.Count(fa => fa.IsComplete()) /
                                  (float)Math.Max(1, schedule.FlightAssignments.Count);

            // Calculate workload balance across all stewards
            float workloadBalance = CalculateWorkloadBalance(schedule, allStewards);

            // Calculate language match rate for all flights
            float languageMatchRate = CalculateLanguageMatchRate(schedule);

            // Calculate steward quality match for flights (based on flight priority and steward quality)
            float qualityMatchRate = CalculateQualityMatchRate(schedule);

            // Inside FitnessCalculator.CalculateScheduleFitness
            float fitnessScore = 0.62f * flightCoverageRate +       // Increased weight for coverage
                                   0.08f * completionRate +             // Reduced weight
                                   0.1f * workloadBalance +            // Reduced weight
                                   0.1f * languageMatchRate +          // Reduced weight
                                   0.1f * qualityMatchRate;           // Reduced weight

            // Ensure you have removed the clamping as suggested above
            return fitnessScore;
        }

        // Calculate how evenly the work is distributed
        private static float CalculateWorkloadBalance(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (allStewards.Count == 0)
                return 0;

            // Calculate hours per steward
            Dictionary<int, float> stewardHours = new Dictionary<int, float>();

            // Initialize with current monthly hours
            foreach (var steward in allStewards)
            {
                stewardHours[steward.StewardId] = steward.MonthlyHours;
            }

            // Add hours from the schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (stewardHours.ContainsKey(steward.StewardId))
                        stewardHours[steward.StewardId] += assignment.Flight.FlightTime;
                    else
                        stewardHours[steward.StewardId] = assignment.Flight.FlightTime;
                }
            }

            // Only consider stewards who are actually assigned to flights
            var activeHours = stewardHours.Where(kv => kv.Value > 0).Select(kv => kv.Value).ToList();

            if (activeHours.Count == 0)
                return 0;

            // Calculate standard deviation
            float avgHours = activeHours.Average();
            float sumSquaredDiff = activeHours.Sum(h => (h - avgHours) * (h - avgHours));
            float stdDev = (float)Math.Sqrt(sumSquaredDiff / activeHours.Count);

            // Convert to a 0-1 score (lower std dev is better)
            float maxStdDev = 20.0f;
            float balance = Math.Max(0, 1 - (stdDev / maxStdDev));

            return balance;
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
                    bool hasLanguageMatch = assignment.BusinessStewards.Any(s =>
                        s.LanguageIds.Contains(assignment.Flight.RequiredLanguageId.Value)) ||
                        assignment.EconomyStewards.Any(s =>
                        s.LanguageIds.Contains(assignment.Flight.RequiredLanguageId.Value));

                    if (hasLanguageMatch)
                        matchCount++;
                }
            }

            return totalFlightsWithLanguageReq == 0 ? 1.0f : (float)matchCount / totalFlightsWithLanguageReq;
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
                float flightImportance = Math.Min(1.0f, assignment.Flight.Priority / 5.0f); // Normalize to 0-1 for 1-5 scale
                float stewardQuality = 0;

                var allAssignedStewards = new List<StewardDto>();
                allAssignedStewards.AddRange(assignment.BusinessStewards);
                allAssignedStewards.AddRange(assignment.EconomyStewards);

                if (allAssignedStewards.Count > 0)
                {
                    // Calculate average quality score for assigned stewards
                    float avgExperience = allAssignedStewards.Average(s => Math.Min(1.0f, s.ExperienceYears / 10.0f)); // Normalize to 0-1
                    float avgFeedback = allAssignedStewards.Average(s =>
                        Math.Min(1.0f, Math.Max(0, s.FeedbackScore / 5.0f))); // Normalize to 0-1

                    stewardQuality = (avgExperience + avgFeedback) / 2.0f;
                }

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

                totalScore += matchScore;
            }

            return totalScore / flightCount;
        }

        // Calculate a steward's suitability score for a specific flight
        public static float CalculateStewardScore(StewardDto steward, FlightDto flight, SchedulingWeights weights, float averageMonthlyHours)
        {
            if (steward == null || flight == null)
                return 0;

            // Check hard constraints first - if any is violated, return 0
            // Check if steward has license for the aircraft
            if (!steward.HasLicenseForAircraft(flight.AircraftType))
                return 0;

            // Check if steward is available during flight time (considering rest periods)
            if (!steward.IsAvailable(flight.DepartureTime, flight.FlightTime))
                return 0;

            // Calculate soft constraint scores
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

            // Calculate weighted score - ensure it stays within 0-1 range
            float totalScore = weights.ExperienceWeight * experienceScore +
                             weights.FeedbackWeight * feedbackScore +
                             weights.WorkloadBalanceWeight * workloadScore +
                             weights.LanguageWeight * languageScore;

            return Math.Min(1.0f, totalScore);
        }
    }
}