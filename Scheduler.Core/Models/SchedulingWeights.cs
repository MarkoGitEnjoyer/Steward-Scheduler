using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class SchedulingWeights
    {
        public float ExperienceWeight { get; set; } = 0.25f;
        public float FeedbackWeight { get; set; } = 0.25f;
        public float WorkloadBalanceWeight { get; set; } = 0.25f;
        public float LanguageWeight { get; set; } = 0.25f;

        // Generate variations for initial population
        public static List<SchedulingWeights> GenerateVariations(int count = 5)
        {
            var variations = new List<SchedulingWeights>();
            var random = new Random();

            // Add default weights
            variations.Add(new SchedulingWeights());

            // Add experience-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.4f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.2f
            });

            // Add feedback-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.4f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.2f
            });

            // Add workload-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.4f,
                LanguageWeight = 0.2f
            });

            // Add language-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.4f
            });

            // Generate additional random variations if needed
            for (int i = variations.Count; i < count; i++)
            {
                // Generate random weights
                float e = (float)random.NextDouble() * 0.5f + 0.1f; // Between 0.1 and 0.6
                float f = (float)random.NextDouble() * 0.5f + 0.1f;
                float w = (float)random.NextDouble() * 0.5f + 0.1f;
                float l = (float)random.NextDouble() * 0.5f + 0.1f;

                // Normalize to sum to 1
                float sum = e + f + w + l;

                variations.Add(new SchedulingWeights
                {
                    ExperienceWeight = e / sum,
                    FeedbackWeight = f / sum,
                    WorkloadBalanceWeight = w / sum,
                    LanguageWeight = l / sum
                });
            }

            return variations;
        }
    }
}
