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

            // Check if there are critical violations that should drop fitness to zero
            if (HasCriticalViolations(schedule, allStewards))
                return 0;

            float fitnessScore = 0;

            // Check if all flights are completely assigned
            float completionRate = schedule.FlightAssignments.Count(fa => fa.IsComplete()) /
                                  (float)schedule.FlightAssignments.Count;

            // Calculate workload balance across all stewards
            float workloadBalance = CalculateWorkloadBalance(schedule, allStewards);

            // Calculate language match rate for all flights
            float languageMatchRate = CalculateLanguageMatchRate(schedule);

            // Calculate steward quality match for flights (based on flight priority and steward quality)
            float qualityMatchRate = CalculateQualityMatchRate(schedule);

            // Calculate crew pairing consistency (same crew for paired flights)
            float pairingConsistency = CalculatePairingConsistency(schedule);

            // Calculate license compliance rate
            float licenseComplianceRate = CalculateLicenseComplianceRate(schedule);

            // Final fitness is a weighted combination of all factors
            fitnessScore = 0.30f * completionRate +          // Completion is most important
                           0.15f * workloadBalance +          // Workload balance
                           0.15f * languageMatchRate +       // Language matching
                           0.15f * qualityMatchRate +        // Quality matching
                           0.10f * pairingConsistency +      // Paired flights consistency
                           0.15f * licenseComplianceRate;    // License compliance

            return fitnessScore;
        }
        private static bool HasLicenseViolations(WeeklySchedule schedule)
        {
            foreach (var assignment in schedule.FlightAssignments)
            {
                string aircraftType = assignment.Flight.AircraftType;

                // Check business stewards
                if (assignment.BusinessStewards.Any(s => !s.HasLicenseForAircraft(aircraftType)))
                    return true;

                // Check economy stewards
                if (assignment.EconomyStewards.Any(s => !s.HasLicenseForAircraft(aircraftType)))
                    return true;
            }

            return false;
        }
        // Check for critical violations that would make a schedule invalid
        private static bool HasCriticalViolations(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            // Check for monthly hour limit violations
            if (HasHourLimitViolations(schedule, allStewards))
                return true;

            // Check for multiple senior stewards on flights
            if (HasMultipleSeniorStewardsViolation(schedule))
                return true;

            // Check for rest period violations
            if (HasRestViolations(schedule))
                return true;

            // Check for license violations
            if (HasLicenseViolations(schedule))
                return true;

            return false;
        }
        private static float CalculateLicenseComplianceRate(WeeklySchedule schedule)
        {
            int totalStewardAssignments = 0;
            int compliantAssignments = 0;

            foreach (var assignment in schedule.FlightAssignments)
            {
                string aircraftType = assignment.Flight.AircraftType;

                // Check business stewards
                foreach (var steward in assignment.BusinessStewards)
                {
                    totalStewardAssignments++;
                    if (steward.HasLicenseForAircraft(aircraftType))
                        compliantAssignments++;
                }

                // Check economy stewards
                foreach (var steward in assignment.EconomyStewards)
                {
                    totalStewardAssignments++;
                    if (steward.HasLicenseForAircraft(aircraftType))
                        compliantAssignments++;
                }
            }

            return totalStewardAssignments > 0 ? (float)compliantAssignments / totalStewardAssignments : 1.0f;
        }

        // Check if any steward exceeds the 90-hour monthly limit
        private static bool HasHourLimitViolations(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            var stewardHours = new Dictionary<int, float>();

            // Initialize with current monthly hours
            foreach (var steward in allStewards)
            {
                stewardHours[steward.StewardId] = steward.MonthlyHours;
            }

            // Calculate hours from this schedule
            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!stewardHours.ContainsKey(steward.StewardId))
                        stewardHours[steward.StewardId] = 0;

                    stewardHours[steward.StewardId] += assignment.Flight.FlightTime;

                    // If any steward exceeds 90 hours, the schedule is invalid
                    if (stewardHours[steward.StewardId] > 90)
                        return true;
                }
            }

            return false;
        }

        // Check if any flight has more than one senior steward
        private static bool HasMultipleSeniorStewardsViolation(WeeklySchedule schedule)
        {
            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.BusinessStewards.Count(s => s.IsSenior) > 1)
                    return true;
            }
            return false;
        }

        // Check if there are any rest period violations
        private static bool HasRestViolations(WeeklySchedule schedule)
        {
            // Create a dictionary of steward ID to their assigned flights
            var stewardFlights = new Dictionary<int, List<FlightDto>>();

            foreach (var assignment in schedule.FlightAssignments)
            {
                foreach (var steward in assignment.BusinessStewards.Concat(assignment.EconomyStewards))
                {
                    if (!stewardFlights.ContainsKey(steward.StewardId))
                        stewardFlights[steward.StewardId] = new List<FlightDto>();

                    stewardFlights[steward.StewardId].Add(assignment.Flight);
                }
            }

            // Check each steward's flights for rest period violations
            foreach (var entry in stewardFlights)
            {
                var flights = entry.Value.OrderBy(f => f.DepartureTime).ToList();

                for (int i = 0; i < flights.Count - 1; i++)
                {
                    var currentFlight = flights[i];
                    var nextFlight = flights[i + 1];

                    // Check if there's at least 12 hours between flights
                    TimeSpan restTime = nextFlight.DepartureTime - currentFlight.ArrivalTime;
                    if (restTime.TotalHours < 12)
                        return true;
                }
            }

            return false;
        }

        // Calculate how well paired flights maintain the same crew
        private static float CalculatePairingConsistency(WeeklySchedule schedule)
        {
            // Find all flight pairs
            Dictionary<int, int> flightPairs = new Dictionary<int, int>();

            foreach (var assignment in schedule.FlightAssignments)
            {
                if (assignment.Flight.ReturnFlightId.HasValue)
                {
                    flightPairs[assignment.Flight.FlightId] = assignment.Flight.ReturnFlightId.Value;
                }
            }

            // If there are no paired flights, return perfect score
            if (flightPairs.Count == 0)
                return 1.0f;

            int consistentPairs = 0;
            int totalPairs = 0;

            // Check each pair for crew consistency
            foreach (var pair in flightPairs)
            {
                var flight1 = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Key);

                var flight2 = schedule.FlightAssignments
                    .FirstOrDefault(fa => fa.Flight.FlightId == pair.Value);

                if (flight1 != null && flight2 != null)
                {
                    totalPairs++;

                    // Check business crew consistency
                    var business1 = flight1.BusinessStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();
                    var business2 = flight2.BusinessStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();

                    // Check economy crew consistency
                    var economy1 = flight1.EconomyStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();
                    var economy2 = flight2.EconomyStewards.Select(s => s.StewardId).OrderBy(id => id).ToList();

                    // Pair is consistent if both business and economy crews match
                    if (business1.SequenceEqual(business2) && economy1.SequenceEqual(economy2))
                    {
                        consistentPairs++;
                    }
                }
            }

            // Calculate consistency rate
            return totalPairs > 0 ? (float)consistentPairs / totalPairs : 1.0f;
        }

        // Calculate how evenly the work is distributed
        private static float CalculateWorkloadBalance(WeeklySchedule schedule, List<StewardDto> allStewards)
        {
            if (allStewards.Count == 0)
                return 0;

            // Calculate average hours per steward
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
            // Note: We'll assume a max standard deviation of 20 hours
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
                float flightImportance = Math.Min(1.0f, assignment.Flight.Priority / 10.0f); // Normalize to 0-1
                float stewardQuality = 0;

                var allAssignedStewards = new List<StewardDto>();
                allAssignedStewards.AddRange(assignment.BusinessStewards);
                allAssignedStewards.AddRange(assignment.EconomyStewards);

                if (allAssignedStewards.Count > 0)
                {
                    // Calculate average quality score for assigned stewards
                    float avgExperience = allAssignedStewards.Average(s => s.ExperienceYears) / 10.0f; // Normalize to 0-1
                    float avgFeedback = allAssignedStewards.Average(s =>
                        Math.Min(1.0f, Math.Max(0, s.FeedbackScore / 5.0f))); // Normalize to 0-1

                    stewardQuality = (avgExperience + avgFeedback) / 2.0f;
                }

                // Higher score if high-quality stewards are assigned to high-priority flights
                float matchScore = 1.0f - Math.Abs(flightImportance - stewardQuality);
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

            // Check if steward has license for the aircraft (NEW)
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

            // Calculate weighted score
            float totalScore = weights.ExperienceWeight * experienceScore +
                             weights.FeedbackWeight * feedbackScore +
                             weights.WorkloadBalanceWeight * workloadScore +
                             weights.LanguageWeight * languageScore;

            return totalScore;
        }
    }
}