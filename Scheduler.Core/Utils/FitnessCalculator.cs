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
            int totalStewardAssignments = 0;
            int matchedStewardAssignments = 0;

            foreach (var flightAssignment in schedule.FlightAssignments)
            {
                // Only process flights with language requirements
                if (flightAssignment.Flight.RequiredLanguageId.HasValue &&
                    flightAssignment.Flight.RequiredLanguageId.Value > 0)
                {
                    int requiredLanguageId = flightAssignment.Flight.RequiredLanguageId.Value;

                    foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
                    {
                        totalStewardAssignments++;

                        if (steward.LanguageIds.Contains(requiredLanguageId))
                        {
                            matchedStewardAssignments++;
                        }
                    }
                }
                else
                {
                    // For flights without language requirements, all stewards are considered matched
                    totalStewardAssignments += flightAssignment.BusinessStewards.Count +
                                             flightAssignment.EconomyStewards.Count;
                    matchedStewardAssignments += flightAssignment.BusinessStewards.Count +
                                               flightAssignment.EconomyStewards.Count;
                }
            }

            // Return the ratio of matched stewards to total steward assignments
            return totalStewardAssignments == 0 ? 1.0f : (float)matchedStewardAssignments / totalStewardAssignments;
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
            if (assignment == null || assignment.Flight == null)
                return 0.0f;

            // making priority from 0.2 to 1
            float flightImportance = Math.Clamp(assignment.Flight.Priority / 5.0f, 0.0f, 1.0f);

            float stewardQuality = CalculateAverageStewardQuality(assignment);

            // calculating how much quality of steward matches the flight
            float matchFactor = 1.0f - Math.Abs(flightImportance - stewardQuality);

            // the minimum score we would apply bonus
            float matchBonusThreshold = 0.6f;

            // the quadratic root exponent to apply bonus
            float matchScoreExponent = 0.5f;

            // applying bonus if score is higher than the limit
            float finalScore = matchFactor > matchBonusThreshold ? (float)Math.Pow(matchFactor, matchScoreExponent) : matchFactor;

            return Math.Clamp(finalScore, 0.0f, 1.0f);
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