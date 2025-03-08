using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class GeneticAlgorithmConfig
    {
        public int PopulationSize { get; set; } = 20;
        public int MaxGenerations { get; set; } = 100;
        public float MutationRate { get; set; } = 0.1f;
        public float CrossoverRate { get; set; } = 0.8f;
        public float ElitismRate { get; set; } = 0.1f; // Top percentage to keep unchanged
    }
}
