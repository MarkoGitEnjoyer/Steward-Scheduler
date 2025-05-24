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
        // calculate the fitness score for an entire weekly schedule
        public static float CalculateScheduleFitness(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (schedule.FlightAssignments.Count == 0)
                return 0;

            // get the fitness components
            float flightCoverageRate = CalculateFlightCoverageRate(schedule);
            float workloadBalance = CalculateWorkloadBalance(schedule, allStewards);
            float languageMatchRate = CalculateLanguageMatchRate(schedule);
            float qualityMatchRate = CalculateQualityMatchRate(schedule);

            // weighted combination of fitness components
            float fitnessScore = 0.65f * flightCoverageRate +      
                               0.1f * workloadBalance +            
                               0.1f * languageMatchRate +         
                               0.15f * qualityMatchRate;           

            return fitnessScore;
        }

        // calculate coverage rate of flights
        private static float CalculateFlightCoverageRate(WeeklySchedule schedule)
        {
            if (schedule.TotalFlightCount == 0)
                return 0.0f;

            // calculate the number of *fully completed* flight assignments.
            int fullyAssignedFlights = schedule.FlightAssignments.Count(fa => fa.IsComplete());

            // calculate coverage rate based on *completed* assignments.
            return (float)fullyAssignedFlights / schedule.TotalFlightCount;
        }


        // calculate how evenly the work is distributed
        private static float CalculateWorkloadBalance(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (allStewards.Count == 0)
                return 0;

            // calculate standard deviation of hours for ALL stewards, not just active ones
            float balanceScore = CalculateHoursBalanceScoreForAllStewards(schedule, allStewards);

            // add penalty for stewards with zero hours
            float inclusionScore = CalculateInclusionScore(schedule, allStewards);

            // combined score with emphasis on including all stewards
            return (balanceScore * 0.7f) + (inclusionScore * 0.3f);
        }

        private static float CalculateHoursBalanceScoreForAllStewards(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // create dictionary of total hours (monthly + scheduled) for ALL stewards
            var allStewardHours = new List<float>();

            foreach (var steward in allStewards)
            {
                float scheduledHours = schedule.GetStewardScheduledHours(steward.StewardId);
                float totalHours = steward.MonthlyHours + scheduledHours;
                allStewardHours.Add(totalHours);
            }

            // calculate standard deviation
            float avgHours = allStewardHours.Average();
            float sumSquaredDiff = allStewardHours.Sum(h => (h - avgHours) * (h - avgHours));
            float stdDev = (float)Math.Sqrt(sumSquaredDiff / allStewardHours.Count);

            //convert to a 0-1 score (lower std dev is better)
            float maxStdDev = 20.0f;
            return Math.Max(0, 1 - (stdDev / maxStdDev));
        }

        private static float CalculateInclusionScore(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // count stewards who have at least one flight assigned
            int stewardsWithFlights = 0;

            foreach (var steward in allStewards)
            {
                if (schedule.StewardHours.ContainsKey(steward.StewardId) &&
                    schedule.StewardHours[steward.StewardId] > 0)
                {
                    stewardsWithFlights++;
                }
            }

            // return ratio of stewards with flights to total stewards
            return (float)stewardsWithFlights / allStewards.Count;
        }

        // calculate how well language requirements are met
        private static float CalculateLanguageMatchRate(WeeklySchedule schedule)
        {
            int totalStewardAssignments = 0;
            int matchedStewardAssignments = 0;

            foreach (var flightAssignment in schedule.FlightAssignments)
            {
                // only process flights with language requirements
                if (flightAssignment.Flight.RequiredLanguageId.HasValue &&
                    flightAssignment.Flight.RequiredLanguageId.Value > 0)
                {
                    int requiredLanguageId = flightAssignment.Flight.RequiredLanguageId.Value;

                    foreach (var steward in flightAssignment.BusinessStewards.Concat(flightAssignment.EconomyStewards))
                    {
                        totalStewardAssignments++;

                        if (steward.DoesSpeakLanguage(requiredLanguageId))
                        {
                            matchedStewardAssignments++;
                        }
                    }
                }
                else
                {
                    // for flights without language requirements, all stewards are considered matched
                    totalStewardAssignments += flightAssignment.BusinessStewards.Count +
                                             flightAssignment.EconomyStewards.Count;
                    matchedStewardAssignments += flightAssignment.BusinessStewards.Count +
                                               flightAssignment.EconomyStewards.Count;
                }
            }

            // return the ratio of matched stewards to total steward assignments
            return totalStewardAssignments == 0 ? 1.0f : (float)matchedStewardAssignments / totalStewardAssignments;
        }

        // calculate how well steward quality matches flight priority
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

            return Math.Clamp(matchFactor, 0.0f, 1.0f);

        }

        private static float CalculateAverageStewardQuality(FlightAssignment assignment)
        {
            var allAssignedStewards = new List<StewardDto>();
            allAssignedStewards.AddRange(assignment.BusinessStewards);
            allAssignedStewards.AddRange(assignment.EconomyStewards);

            if (allAssignedStewards.Count == 0)
                return 0;

            // calculate average quality score for assigned stewards
            float avgExperience = allAssignedStewards.Average(s => Math.Min(1.0f, s.ExperienceYears / 10.0f)); // normalize to 0-1
            float avgFeedback = allAssignedStewards.Average(s =>
                Math.Min(1.0f, Math.Max(0, s.FeedbackScore / 7.0f))); // normalize to 0-1

            return (avgExperience + avgFeedback) / 2.0f;
        }

    }
}