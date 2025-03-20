using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class GeneticAlgorithmConfig
    {
        // Increased population size for more diversity
        public int PopulationSize { get; set; } = 20;

        // More generations to allow for evolutionary improvements
        public int MaxGenerations { get; set; } = 100;

        // Increased mutation rate for more exploration
        public float MutationRate { get; set; } = 0.3f;

        // Slightly reduced to allow more exploration
        public float CrossoverRate { get; set; } = 0.7f;

        // Keep more of the best solutions
        public float ElitismRate { get; set; } = 0.1f; // Top percentage to keep unchanged
    }
}