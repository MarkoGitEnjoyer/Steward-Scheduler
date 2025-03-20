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

            // Add experience-focused weights (Heavy focus)
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.7f,
                FeedbackWeight = 0.1f,
                WorkloadBalanceWeight = 0.1f,
                LanguageWeight = 0.1f
            });

            // Add feedback-focused weights (Heavy focus)
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.1f,
                FeedbackWeight = 0.7f,
                WorkloadBalanceWeight = 0.1f,
                LanguageWeight = 0.1f
            });

            // Add workload-focused weights (Heavy focus)
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.1f,
                FeedbackWeight = 0.1f,
                WorkloadBalanceWeight = 0.7f,
                LanguageWeight = 0.1f
            });

            // Add language-focused weights (Heavy focus)
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.1f,
                FeedbackWeight = 0.1f,
                WorkloadBalanceWeight = 0.1f,
                LanguageWeight = 0.7f
            });

            // Add moderate experience-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.4f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.2f
            });

            // Add moderate feedback-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.4f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.2f
            });

            // Add moderate workload-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.4f,
                LanguageWeight = 0.2f
            });

            // Add moderate language-focused weights
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.2f,
                FeedbackWeight = 0.2f,
                WorkloadBalanceWeight = 0.2f,
                LanguageWeight = 0.4f
            });

            // Add dual-focus configurations
            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.4f,
                FeedbackWeight = 0.4f,
                WorkloadBalanceWeight = 0.1f,
                LanguageWeight = 0.1f
            });

            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.1f,
                FeedbackWeight = 0.1f,
                WorkloadBalanceWeight = 0.4f,
                LanguageWeight = 0.4f
            });

            variations.Add(new SchedulingWeights
            {
                ExperienceWeight = 0.4f,
                FeedbackWeight = 0.1f,
                WorkloadBalanceWeight = 0.4f,
                LanguageWeight = 0.1f
            });

            // Generate additional random variations if needed
            for (int i = variations.Count; i < count; i++)
            {
                // Generate random weights
                float e = (float)random.NextDouble() * 0.7f + 0.1f; // Between 0.1 and 0.8
                float f = (float)random.NextDouble() * 0.7f + 0.1f;
                float w = (float)random.NextDouble() * 0.7f + 0.1f;
                float l = (float)random.NextDouble() * 0.7f + 0.1f;

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